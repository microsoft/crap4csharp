namespace Microsoft.Crap4CSharp;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Faithful port of crap4java's JavaMethodParser, re-hosted on Roslyn (docs/decisions.md: Parser row).
// Java compiled the source with the JDK tool API and walked the javac tree; the C# analog parses the
// source with Roslyn and walks the syntax tree. Two ratified departures apply (docs/decisions.md):
//   * D-T9 -- the entry point is Parse(string source). Java's className -> source-path / file-URI
//     plumbing (sourcePath/sourceUri) has no C# analog and is dropped, along with its two tests.
//   * #2 (richer CC) -- ComplexityWalker uses the augmented node set and descends into lambdas and
//     local functions, so absolute CC is not numerically comparable to crap4java on modern code.
// TypeName (the coverage-lookup key -- docs/decisions.md "Coverage key = enclosing-type FQN") is the
// one new descriptor field: the enclosing type's namespace-qualified, arity-annotated FQN.
public static class CSharpMethodParser
{
    // Ports JavaMethodParser.parse, adapted to Parse(string source) per D-T9. Syntax-only: no
    // CSharpCompilation, no SemanticModel, no metadata references -- undefined types parse fine, so
    // sibling types are never resolved. Methods are collected in document (source) order, not sorted.
    public static IReadOnlyList<MethodDescriptor> Parse(string source)
    {
        // Java implicitly NPEs when the compiler reads a null source; the idiomatic C# equivalent (and
        // CA1062's required guard, since suppressions are banned) is a fail-fast ArgumentNullException.
        ArgumentNullException.ThrowIfNull(source);

        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

        List<MethodDescriptor> methods = [];

        // One unified emitter shared by every member kind: the bodyless-skip guard (the analog of
        // Java's getBody() == null), then StartLine (the emitted node's own decl start), EndLine (body
        // end) and the augmented CC over the member's own body. declNode drives both StartLine and the
        // enclosing-type FQN, so a single descriptor path serves methods, accessors, operators, etc.
        void Emit(SyntaxNode declNode, SyntaxNode? blockBody, SyntaxNode? exprBody, string name)
        {
            if (blockBody is null && exprBody is null)
            {
                // Bodyless: auto/abstract accessors, static-abstract/partial-defining operators, etc.
                return;
            }

            SyntaxNode body = blockBody ?? exprBody!;
            int startLine = declNode.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            int endLine = body.GetLocation().GetLineSpan().EndLinePosition.Line + 1;

            methods.Add(new MethodDescriptor(
                Name: name,
                StartLine: startLine,
                EndLine: endLine,
                Complexity: ComplexityWalker.Count(body),
                TypeName: TypeNameOf(declNode)));
        }

        // Emits one row per bodied accessor, with the accessor's OWN decl start as StartLine (the
        // nearest-line disambiguator against coverlet's per-accessor min-line). memberName carries the
        // declaring member's name ("Item" for indexers, the CLR default); the prefix maps get/set/init/
        // add/remove to the CLR method spelling. init lowers to set_, so both share the set_ prefix.
        void EmitAccessors(AccessorListSyntax? list, string memberName)
        {
            if (list is null)
            {
                return;
            }

            foreach (AccessorDeclarationSyntax accessor in list.Accessors)
            {
                string? prefix = AccessorPrefix(accessor);
                if (prefix is not null)
                {
                    Emit(accessor, accessor.Body, accessor.ExpressionBody, prefix + memberName);
                }
            }
        }

        // ONE document-order (depth-first pre-order = source order) walk over every declaration, so the
        // emitted descriptors keep source order across member kinds. Each member is handled at its own
        // node visit; accessors are emitted when their PARENT (property/indexer/event) node is reached,
        // so there is no AccessorDeclarationSyntax case (that would double-count) and field-like events
        // (EventFieldDeclarationSyntax) are intentionally not a case (their add/remove are compiler-gen).
        foreach (SyntaxNode node in root.DescendantNodes())
        {
            switch (node)
            {
                case MethodDeclarationSyntax m:
                    Emit(m, m.Body, m.ExpressionBody, m.Identifier.Text);
                    break;

                case OperatorDeclarationSyntax op:
                    // op_Checked* variants are deferred (docs/decisions.md D-T26e): a checked operator
                    // can only be declared alongside its unchecked sibling, which IS scored.
                    if (!op.CheckedKeyword.IsKind(SyntaxKind.CheckedKeyword))
                    {
                        Emit(op, op.Body, op.ExpressionBody, OperatorName(op));
                    }

                    break;

                case ConversionOperatorDeclarationSyntax conv:
                    if (!conv.CheckedKeyword.IsKind(SyntaxKind.CheckedKeyword))
                    {
                        string convName = conv.ImplicitOrExplicitKeyword.IsKind(SyntaxKind.ImplicitKeyword) ? "op_Implicit" : "op_Explicit";
                        Emit(conv, conv.Body, conv.ExpressionBody, convName);
                    }

                    break;

                case DestructorDeclarationSyntax dtor:
                    Emit(dtor, dtor.Body, dtor.ExpressionBody, "Finalize");
                    break;

                case PropertyDeclarationSyntax prop:
                    if (prop.ExpressionBody is not null)
                    {
                        Emit(prop, null, prop.ExpressionBody, "get_" + prop.Identifier.Text);
                    }
                    else
                    {
                        EmitAccessors(prop.AccessorList, prop.Identifier.Text);
                    }

                    break;

                case IndexerDeclarationSyntax idx:
                    if (idx.ExpressionBody is not null)
                    {
                        Emit(idx, null, idx.ExpressionBody, "get_Item");
                    }
                    else
                    {
                        EmitAccessors(idx.AccessorList, "Item");
                    }

                    break;

                case EventDeclarationSyntax evt:
                    EmitAccessors(evt.AccessorList, evt.Identifier.Text);
                    break;
            }
        }

        return methods;
    }

    // Builds the enclosing-type FQN coverage key: dotted namespace + '.' + dot-joined containing-type
    // chain, each type carrying `N when it declares N type parameters (docs/decisions.md). The global
    // namespace yields no prefix. Examples: Demo.Sample; Demo.Outer.Inner; Demo.Container`1;
    // Demo.Outer`1.Inner`2; Sample (no namespace).
    private static string TypeNameOf(SyntaxNode node)
    {
        IEnumerable<string> types = node.Ancestors()
            .OfType<TypeDeclarationSyntax>()
            .Select(t =>
            {
                int arity = t.TypeParameterList?.Parameters.Count ?? 0;
                return arity > 0 ? $"{t.Identifier.Text}`{arity}" : t.Identifier.Text;
            })
            .Reverse();
        IEnumerable<string> namespaces = node.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Select(n => n.Name.ToString())
            .Reverse();
        string typeChain = string.Join(".", types);
        string ns = string.Join(".", namespaces);
        return ns.Length == 0 ? typeChain : $"{ns}.{typeChain}";
    }

    // Maps a user-defined operator to its CLR method name (docs/decisions.md D-T26b). The overloadable
    // operator set is closed, so the switch is exhaustive for valid C#. Only '+' and '-' are declarable
    // as both unary and binary, so those two rows disambiguate by arity (1 = unary, 2 = binary); every
    // other token has a single CLR name. The defensive default emits the raw token text (never matches a
    // coverlet op_* entry -> safe N/A, never a crash).
    private static string OperatorName(OperatorDeclarationSyntax op)
    {
        int arity = op.ParameterList.Parameters.Count;
        return op.OperatorToken.Kind() switch
        {
            SyntaxKind.PlusToken => arity == 1 ? "op_UnaryPlus" : "op_Addition",
            SyntaxKind.MinusToken => arity == 1 ? "op_UnaryNegation" : "op_Subtraction",
            SyntaxKind.AsteriskToken => "op_Multiply",
            SyntaxKind.SlashToken => "op_Division",
            SyntaxKind.PercentToken => "op_Modulus",
            SyntaxKind.AmpersandToken => "op_BitwiseAnd",
            SyntaxKind.BarToken => "op_BitwiseOr",
            SyntaxKind.CaretToken => "op_ExclusiveOr",
            SyntaxKind.LessThanLessThanToken => "op_LeftShift",
            SyntaxKind.GreaterThanGreaterThanToken => "op_RightShift",
            SyntaxKind.GreaterThanGreaterThanGreaterThanToken => "op_UnsignedRightShift",
            SyntaxKind.EqualsEqualsToken => "op_Equality",
            SyntaxKind.ExclamationEqualsToken => "op_Inequality",
            SyntaxKind.LessThanToken => "op_LessThan",
            SyntaxKind.GreaterThanToken => "op_GreaterThan",
            SyntaxKind.LessThanEqualsToken => "op_LessThanOrEqual",
            SyntaxKind.GreaterThanEqualsToken => "op_GreaterThanOrEqual",
            SyntaxKind.ExclamationToken => "op_LogicalNot",
            SyntaxKind.TildeToken => "op_OnesComplement",
            SyntaxKind.PlusPlusToken => "op_Increment",
            SyntaxKind.MinusMinusToken => "op_Decrement",
            SyntaxKind.TrueKeyword => "op_True",
            SyntaxKind.FalseKeyword => "op_False",
            _ => op.OperatorToken.Text,
        };
    }

    // Maps an accessor keyword to its CLR method-name prefix (docs/decisions.md D-T26b). init lowers to a
    // set_ method, so it shares the set_ prefix. An unrecognized (error-recovery) accessor yields null,
    // signalling EmitAccessors to skip it.
    private static string? AccessorPrefix(AccessorDeclarationSyntax a) => a.Keyword.Kind() switch
    {
        SyntaxKind.GetKeyword => "get_",
        SyntaxKind.SetKeyword => "set_",
        SyntaxKind.InitKeyword => "set_",
        SyntaxKind.AddKeyword => "add_",
        SyntaxKind.RemoveKeyword => "remove_",
        _ => null,
    };

    // The augmented cyclomatic-complexity counter (docs/decisions.md "authoritative node set"). Ports
    // crap4java's ComplexityCounter and adds the modern-C# decision nodes. Base CC = 1; every counted
    // node adds 1; nested type/enum declarations are pruned; lambdas and local functions are descended
    // into (departure #2). A fresh walker is used per method, and CC is only ever verified through
    // Parse. The type is private (zero surface outside CSharpMethodParser) and uses no `internal`;
    // Count is `public` only so the enclosing type can call it -- the private nested type caps its
    // effective accessibility to private (a literal `private` would be uncallable, CS0122).
    private sealed class ComplexityWalker : CSharpSyntaxWalker
    {
        private int _complexity = 1;

        public static int Count(SyntaxNode body)
        {
            ComplexityWalker walker = new();
            walker.Visit(body);
            return walker._complexity;
        }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            _complexity++;
            base.VisitIfStatement(node);
        }

        public override void VisitForStatement(ForStatementSyntax node)
        {
            _complexity++;
            base.VisitForStatement(node);
        }

        public override void VisitForEachStatement(ForEachStatementSyntax node)
        {
            _complexity++;
            base.VisitForEachStatement(node);
        }

        public override void VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
        {
            _complexity++;
            base.VisitForEachVariableStatement(node);
        }

        public override void VisitWhileStatement(WhileStatementSyntax node)
        {
            _complexity++;
            base.VisitWhileStatement(node);
        }

        public override void VisitDoStatement(DoStatementSyntax node)
        {
            _complexity++;
            base.VisitDoStatement(node);
        }

        public override void VisitCatchClause(CatchClauseSyntax node)
        {
            _complexity++;
            base.VisitCatchClause(node);
        }

        public override void VisitCatchFilterClause(CatchFilterClauseSyntax node)
        {
            _complexity++;
            base.VisitCatchFilterClause(node);
        }

        public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            _complexity++;
            base.VisitConditionalExpression(node);
        }

        public override void VisitCaseSwitchLabel(CaseSwitchLabelSyntax node)
        {
            _complexity++;
            base.VisitCaseSwitchLabel(node);
        }

        public override void VisitCasePatternSwitchLabel(CasePatternSwitchLabelSyntax node)
        {
            _complexity++;
            base.VisitCasePatternSwitchLabel(node);
        }

        public override void VisitDefaultSwitchLabel(DefaultSwitchLabelSyntax node)
        {
            _complexity++;
            base.VisitDefaultSwitchLabel(node);
        }

        public override void VisitSwitchExpressionArm(SwitchExpressionArmSyntax node)
        {
            _complexity++;
            base.VisitSwitchExpressionArm(node);
        }

        public override void VisitWhenClause(WhenClauseSyntax node)
        {
            _complexity++;
            base.VisitWhenClause(node);
        }

        public override void VisitBinaryPattern(BinaryPatternSyntax node)
        {
            _complexity++;
            base.VisitBinaryPattern(node);
        }

        public override void VisitUnaryPattern(UnaryPatternSyntax node)
        {
            _complexity++;
            base.VisitUnaryPattern(node);
        }

        // Only the short-circuiting logical operators (&&, ||) and null-coalescing (??) are decision
        // points; the bitwise operators (&, |, ^) are not.
        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (node.Kind() is SyntaxKind.LogicalAndExpression
                or SyntaxKind.LogicalOrExpression
                or SyntaxKind.CoalesceExpression)
            {
                _complexity++;
            }

            base.VisitBinaryExpression(node);
        }

        // Only the null-coalescing assignment (??=) is a decision point; ordinary and compound
        // assignments are not.
        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            if (node.Kind() is SyntaxKind.CoalesceAssignmentExpression)
            {
                _complexity++;
            }

            base.VisitAssignmentExpression(node);
        }

        // Prune at nested type/enum declarations (base is intentionally not called): their members'
        // decision nodes belong to their own methods, not the enclosing one -- the analog of
        // crap4java's visitClass prune.
        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
        }

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
        }

        public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
        }
    }
}

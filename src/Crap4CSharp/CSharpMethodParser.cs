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
        foreach (MethodDeclarationSyntax m in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            // Skip abstract/partial/extern declarations that carry no body -- the analog of Java's
            // getBody() == null guard. Constructors, finalizers, operators, accessors, local
            // functions and lambdas are excluded automatically: none is a MethodDeclarationSyntax.
            if (m.Body is null && m.ExpressionBody is null)
            {
                continue;
            }

            FileLinePositionSpan span = m.GetLocation().GetLineSpan();
            int startLine = span.StartLinePosition.Line + 1;
            SyntaxNode body = (SyntaxNode?)m.Body ?? m.ExpressionBody!;
            int endLine = body.GetLocation().GetLineSpan().EndLinePosition.Line + 1;
            int complexity = ComplexityWalker.Count(body);

            methods.Add(new MethodDescriptor(
                Name: m.Identifier.Text,
                StartLine: startLine,
                EndLine: endLine,
                Complexity: complexity,
                TypeName: TypeNameOf(m)));
        }

        return methods;
    }

    // Builds the enclosing-type FQN coverage key: dotted namespace + '.' + dot-joined containing-type
    // chain, each type carrying `N when it declares N type parameters (docs/decisions.md). The global
    // namespace yields no prefix. Examples: Demo.Sample; Demo.Outer.Inner; Demo.Container`1;
    // Demo.Outer`1.Inner`2; Sample (no namespace).
    private static string TypeNameOf(MethodDeclarationSyntax m)
    {
        IEnumerable<string> types = m.Ancestors()
            .OfType<TypeDeclarationSyntax>()
            .Select(t =>
            {
                int arity = t.TypeParameterList?.Parameters.Count ?? 0;
                return arity > 0 ? $"{t.Identifier.Text}`{arity}" : t.Identifier.Text;
            })
            .Reverse();
        IEnumerable<string> namespaces = m.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Select(n => n.Name.ToString())
            .Reverse();
        string typeChain = string.Join(".", types);
        string ns = string.Join(".", namespaces);
        return ns.Length == 0 ? typeChain : $"{ns}.{typeChain}";
    }

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

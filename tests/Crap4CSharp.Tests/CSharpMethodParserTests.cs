namespace Microsoft.Crap4CSharp.Tests;

// Faithful port of crap4java's JavaMethodParserTest, re-hosted on Roslyn. Per Mr. Das's ruling D-T9
// the entry point is Parse(string source): the two Java tests that exercised the className ->
// source-path / file-URI plumbing (buildsSourcePathAndUriFromClassNames, acceptsClassNamesWithJavaSuffix)
// have no C# analog and are dropped; the other seven Java tests are ported with identical intent and
// oracle values (alpha=3, beta=4, score=10, nested=13, switched=4, stable=1, helper=1). Fixtures use
// C# raw string literals (fixture member names live inside the strings, so CA1707 does not apply);
// Java's `boolean`/`package`/anonymous-class constructs are adapted to `bool`/`namespace`/lambda +
// local function. Ten new tests pin the C#-only behaviour: the TypeName coverage key
// (docs/decisions.md "Coverage key = enclosing-type FQN") and the augmented complexity node set
// (docs/decisions.md, departure #2). Descriptors are asserted for structural equality in document
// (source) order.
public class CSharpMethodParserTests
{
    [Fact]
    public void ExtractsConcreteMethodsWithLinesAndComplexity()
    {
        string source = """
            namespace Demo;
            class Sample
            {
                int Alpha(bool a, bool b)
                {
                    if (a && b)
                    {
                        return 1;
                    }
                    return 0;
                }

                int Beta(int x)
                {
                    switch (x)
                    {
                        case 1: return 1;
                        case 2: return 2;
                        default: return 0;
                    }
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(
            new MethodDescriptor("Alpha", 4, 11, 3, "Demo.Sample"),
            new MethodDescriptor("Beta", 13, 21, 4, "Demo.Sample"));
    }

    [Fact]
    public void IgnoresConstructorsAndAbstractMethods()
    {
        string source = """
            abstract class Sample
            {
                Sample()
                {
                }

                abstract int Missing();

                int Present()
                {
                    return 1;
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(new MethodDescriptor("Present", 9, 12, 1, "Sample"));
    }

    [Fact]
    public void ParsesMethodsWithoutResolvingSiblingTypes()
    {
        string source = """
            namespace Demo;
            class Sample
            {
                Helper helper() => new Helper();
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(new MethodDescriptor("helper", 4, 4, 1, "Demo.Sample"));
    }

    [Fact]
    public void IgnoresKeywordsInsideCommentsAndStrings()
    {
        string source = """
            class Sample
            {
                int Stable()
                {
                    string text = "if && || ? case default catch";
                    // if && || ? case default catch
                    /* if && || ? case default catch */
                    return 1;
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(new MethodDescriptor("Stable", 3, 9, 1, "Sample"));
    }

    [Fact]
    public void CountsDecisionNodesFromTheSyntaxTree()
    {
        string source = """
            class Sample
            {
                int Score(bool a, bool b, int[] values)
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                    }
                    foreach (int value in values)
                    {
                    }
                    while (a)
                    {
                        a = false;
                    }
                    do
                    {
                        b = false;
                    }
                    while (b);
                    if (a && b || values.Length > 0)
                    {
                    }
                    try
                    {
                        return a ? 1 : 0;
                    }
                    catch (Exception ex)
                    {
                        return 2;
                    }
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(new MethodDescriptor("Score", 3, 31, 10, "Sample"));
    }

    [Fact]
    public void VisitsNestedDecisionNodesInsideOtherDecisionNodes()
    {
        string source = """
            class Sample
            {
                int Nested(bool a, bool b, int[] values)
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        if (a)
                        {
                        }
                    }
                    foreach (int value in values)
                    {
                        if (b)
                        {
                        }
                    }
                    while (a)
                    {
                        if (b)
                        {
                        }
                        a = false;
                    }
                    do
                    {
                        if (a)
                        {
                        }
                        b = false;
                    }
                    while (b);
                    try
                    {
                        return a ? (b ? 1 : 0) : 2;
                    }
                    catch (Exception ex)
                    {
                        if (values.Length > 0)
                        {
                            return values[0];
                        }
                        return 3;
                    }
                }

                int Switched(int value)
                {
                    switch (value)
                    {
                        case 1:
                            if (value > 0)
                            {
                                return 1;
                            }
                            return 0;
                        default:
                            return 2;
                    }
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(
            new MethodDescriptor("Nested", 3, 44, 13, "Sample"),
            new MethodDescriptor("Switched", 46, 59, 4, "Sample"));
    }

    [Fact]
    public void DoesNotCollectLambdaOrLocalFunctionBodiesAsSeparateMethods()
    {
        // C# analog of Java's ignoresMethodsDeclaredInsideAnonymousClasses: Outer holds a decision-free
        // lambda AND local function. Neither is a MethodDeclarationSyntax, so only Outer is collected,
        // and (being decision-free) Outer's complexity stays 1.
        string source = """
            class Sample
            {
                int Outer()
                {
                    Action action = () =>
                    {
                        int x = 1;
                    };
                    int Local()
                    {
                        return 2;
                    }
                    action();
                    return Local();
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(new MethodDescriptor("Outer", 3, 15, 1, "Sample"));
    }

    [Fact]
    public void PopulatesTypeNameForNamespaceQualifiedType()
    {
        string source = """
            namespace Demo;
            class Sample
            {
                int M()
                {
                    return 1;
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(new MethodDescriptor("M", 4, 7, 1, "Demo.Sample"));
    }

    [Fact]
    public void PopulatesTypeNameForNestedTypes()
    {
        string source = """
            namespace Demo;
            class Outer
            {
                class Inner
                {
                    int M()
                    {
                        return 1;
                    }
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(new MethodDescriptor("M", 6, 9, 1, "Demo.Outer.Inner"));
    }

    [Fact]
    public void PopulatesTypeNameForGenericArity()
    {
        string source = """
            namespace Demo;
            class Container<T>
            {
                int First()
                {
                    return 1;
                }
            }
            class Outer<T>
            {
                class Inner<U, V>
                {
                    int Second()
                    {
                        return 2;
                    }
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(
            new MethodDescriptor("First", 4, 7, 1, "Demo.Container`1"),
            new MethodDescriptor("Second", 13, 16, 1, "Demo.Outer`1.Inner`2"));
    }

    [Fact]
    public void PopulatesTypeNameForGlobalNamespace()
    {
        string source = """
            class Sample
            {
                int M()
                {
                    return 1;
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(new MethodDescriptor("M", 3, 6, 1, "Sample"));
    }

    [Fact]
    public void IncludesExpressionBodiedMethods()
    {
        string source = """
            class Sample
            {
                int M() => 1;
                int N(bool a) => a ? 1 : 0;
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(
            new MethodDescriptor("M", 3, 3, 1, "Sample"),
            new MethodDescriptor("N", 4, 4, 2, "Sample"));
    }

    [Fact]
    public void CountsSwitchExpressionArmsAndWhenGuards()
    {
        // base(1) + arm1 SwitchExpressionArm(+1) + `and` BinaryPattern(+1)
        //         + arm2 SwitchExpressionArm(+1) + WhenClause(+1)
        //         + arm3 SwitchExpressionArm(+1) = 6.
        string source = """
            class Sample
            {
                int M(int x, bool cond)
                {
                    return x switch
                    {
                        > 0 and < 10 => 1,
                        _ when cond => 2,
                        _ => 0,
                    };
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(new MethodDescriptor("M", 3, 11, 6, "Sample"));
    }

    [Fact]
    public void CountsNullCoalescingAndPatternCombinators()
    {
        // One decision family per method: ?? (CoalesceExpression), ??= (CoalesceAssignmentExpression),
        // pattern `not` (UnaryPattern), pattern `or` (BinaryPattern) -- each raises base(1) to 2.
        string source = """
            class Sample
            {
                int Coalesce(int? a)
                {
                    return a ?? 0;
                }

                void CoalesceAssign(ref int? a)
                {
                    a ??= 0;
                }

                bool NotNull(object? o)
                {
                    return o is not null;
                }

                bool OrPattern(object o)
                {
                    return o is int or string;
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(
            new MethodDescriptor("Coalesce", 3, 6, 2, "Sample"),
            new MethodDescriptor("CoalesceAssign", 8, 11, 2, "Sample"),
            new MethodDescriptor("NotNull", 13, 16, 2, "Sample"),
            new MethodDescriptor("OrPattern", 18, 21, 2, "Sample"));
    }

    [Fact]
    public void CountsGuardedCatchFilter()
    {
        // catch (E) when (c) is a deliberate double-count: CatchClause(+1) AND CatchFilterClause(+1),
        // raising base(1) to 3.
        string source = """
            class Sample
            {
                int M(bool c)
                {
                    try
                    {
                        return 1;
                    }
                    catch (Exception) when (c)
                    {
                        return 2;
                    }
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(new MethodDescriptor("M", 3, 13, 3, "Sample"));
    }

    [Fact]
    public void CountsDecisionsInsideLambdasAndLocalFunctions()
    {
        // Departure #2: the walker descends into lambdas and local functions, so their decisions raise
        // the enclosing method's CC -- base(1) + lambda if(+1) + lambda &&(+1) + local-fn ?:(+1) = 4 --
        // yet neither body is collected as a separate descriptor.
        string source = """
            class Sample
            {
                int Outer(bool a, bool b)
                {
                    Action lambda = () =>
                    {
                        if (a && b)
                        {
                        }
                    };
                    int Local(int x)
                    {
                        return x > 0 ? 1 : 0;
                    }
                    lambda();
                    return Local(1);
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(new MethodDescriptor("Outer", 3, 17, 4, "Sample"));
    }

    [Fact]
    public void DoesNotCountElseTryFinallyJumpsOrBitwise()
    {
        // Negative oracle for the don't-count list: else, try/finally, throw, bitwise &/|/^, ?. and a
        // combinator-free `is` pattern all add nothing -- only the single `if` counts, so base(1) -> 2.
        string source = """
            class Sample
            {
                int M(bool a, int x, int y, string? s, object o)
                {
                    if (a)
                    {
                        throw new Exception();
                    }
                    else
                    {
                        x = y & 1;
                        x = y | 2;
                        x = y ^ 3;
                    }

                    try
                    {
                        int? len = s?.Length;
                    }
                    finally
                    {
                        bool flag = o is string;
                    }

                    return x;
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(new MethodDescriptor("M", 3, 26, 2, "Sample"));
    }

    // T26 (departure #14): executable-member decomposition. The parser now emits a descriptor for every
    // logic-bearing member with a body -- property/indexer accessors, operators, conversions, finalizers
    // and custom event accessors -- under its CLR method name, keeping document (source) order across
    // member kinds. Bodyless members (auto/abstract accessors, field-like events) get no row; constructors
    // stay excluded. Accessor StartLine is the accessor's OWN decl line (the nearest-line disambiguator).
    [Fact]
    public void EmitsBlockPropertyAccessorsAsGetAndSetRows()
    {
        string source = """
            class Sample
            {
                int _v;
                int Value
                {
                    get { return _v; }
                    set { if (value > 0) { _v = value; } }
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(
            new MethodDescriptor("get_Value", 6, 6, 1, "Sample"),
            new MethodDescriptor("set_Value", 7, 7, 2, "Sample"));
    }

    [Fact]
    public void EmitsExpressionBodiedAccessorsAsSeparateRows()
    {
        string source = """
            class Sample
            {
                int _v;
                int Value
                {
                    get => _v;
                    set => _v = value;
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(
            new MethodDescriptor("get_Value", 6, 6, 1, "Sample"),
            new MethodDescriptor("set_Value", 7, 7, 1, "Sample"));
    }

    [Fact]
    public void EmitsExpressionBodiedPropertyAsSingleGetRow()
    {
        string source = """
            class Sample
            {
                int _v;
                int Value => _v > 0 ? _v : 0;
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        // A single get_ row (base 1 + one ?:); the expression-bodied property never emits a set_.
        methods.Should().Equal(new MethodDescriptor("get_Value", 4, 4, 2, "Sample"));
    }

    [Fact]
    public void SkipsBodylessAutoAndInitAccessors()
    {
        string source = """
            class Sample
            {
                int Value { get; set; }
                int ReadOnly { get; }
                int Init { get; init; }
                int M() => 1;
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        // Every auto/init accessor is bodyless -> skipped; only the bodied method survives.
        methods.Should().Equal(new MethodDescriptor("M", 6, 6, 1, "Sample"));
    }

    [Fact]
    public void EmitsIndexerAccessorsUsingItemName()
    {
        string source = """
            class Sample
            {
                int[] _data;
                int this[int i]
                {
                    get { return _data[i]; }
                    set { _data[i] = value; }
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(
            new MethodDescriptor("get_Item", 6, 6, 1, "Sample"),
            new MethodDescriptor("set_Item", 7, 7, 1, "Sample"));
    }

    [Fact]
    public void EmitsExpressionBodiedIndexerAsSingleGetItemRow()
    {
        string source = """
            class Sample
            {
                int[] _data;
                int this[int i] => _data[i];
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(new MethodDescriptor("get_Item", 4, 4, 1, "Sample"));
    }

    // T28 (docs/decisions.md D-T28a): a LITERAL [IndexerName("X")] renames an indexer's CLR accessors to
    // get_X/set_X -- the spelling coverlet emits -- so the parser derives the base name from the attribute
    // instead of hardcoding `Item`. A missing attribute (EmitsIndexerAccessorsUsingItemName above) or a
    // non-literal argument falls back to `Item`. Syntax-only: no using directive is needed to parse it.
    [Fact]
    public void EmitsRenamedIndexerAccessorsFromLiteralIndexerName()
    {
        string source = """
            class Sample
            {
                int[] _data;
                [IndexerName("Foo")]
                int this[int i]
                {
                    get { return _data[i]; }
                    set { _data[i] = value; }
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(
            new MethodDescriptor("get_Foo", 7, 7, 1, "Sample"),
            new MethodDescriptor("set_Foo", 8, 8, 1, "Sample"));
    }

    [Fact]
    public void EmitsExpressionBodiedRenamedIndexerAsSingleGetRow()
    {
        // Attribute inline with the declaration so the single-row StartLine (the whole indexer node,
        // attributes included) is unambiguous. This pins the get_ + base-name path of the fix.
        string source = """
            class Sample
            {
                int[] _data;
                [IndexerName("Foo")] int this[int i] => _data[i];
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(new MethodDescriptor("get_Foo", 4, 4, 1, "Sample"));
    }

    [Fact]
    public void FallsBackToItemWhenIndexerNameArgumentIsNotLiteral()
    {
        string source = """
            class Sample
            {
                const string Name = "Foo";
                int[] _data;
                [IndexerName(Name)]
                int this[int i]
                {
                    get { return _data[i]; }
                    set { _data[i] = value; }
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        // A non-literal [IndexerName(<const>)] argument is not statically readable by the syntax-only
        // parser, so the base name stays the default `Item` (docs/decisions.md #14 residual (b)).
        methods.Should().Equal(
            new MethodDescriptor("get_Item", 8, 8, 1, "Sample"),
            new MethodDescriptor("set_Item", 9, 9, 1, "Sample"));
    }

    // T28 (docs/decisions.md D-T28a): IndexerBaseName matches [IndexerName] on its SIMPLE name
    // (QualifiedNameSyntax.Right / SimpleNameSyntax.Identifier) and accepts the `Attribute` suffix, so the
    // fully-qualified and suffixed spellings rename the accessors exactly like the bare form above -- locked
    // here directly (the qualified form is otherwise only exercised end-to-end). Same layout: get/set 7/8.
    [Fact]
    public void EmitsRenamedIndexerAccessorsFromQualifiedIndexerName()
    {
        string source = """
            class Sample
            {
                int[] _data;
                [System.Runtime.CompilerServices.IndexerName("Foo")]
                int this[int i]
                {
                    get { return _data[i]; }
                    set { _data[i] = value; }
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(
            new MethodDescriptor("get_Foo", 7, 7, 1, "Sample"),
            new MethodDescriptor("set_Foo", 8, 8, 1, "Sample"));
    }

    [Fact]
    public void EmitsRenamedIndexerAccessorsFromAttributeSuffixedIndexerName()
    {
        string source = """
            class Sample
            {
                int[] _data;
                [IndexerNameAttribute("Foo")]
                int this[int i]
                {
                    get { return _data[i]; }
                    set { _data[i] = value; }
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(
            new MethodDescriptor("get_Foo", 7, 7, 1, "Sample"),
            new MethodDescriptor("set_Foo", 8, 8, 1, "Sample"));
    }

    [Fact]
    public void DisambiguatesUnaryAndBinaryOperatorsByArity()
    {
        string source = """
            struct Vec
            {
                int X;
                public static Vec operator +(Vec a, Vec b) => a;
                public static Vec operator -(Vec a) => a;
                public static Vec operator -(Vec a, Vec b) => a;
                public static Vec operator +(Vec a) => a;
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        // Only '+'/'-' are declarable as both unary and binary; arity (2 vs 1) splits them.
        methods.Should().Equal(
            new MethodDescriptor("op_Addition", 4, 4, 1, "Vec"),
            new MethodDescriptor("op_UnaryNegation", 5, 5, 1, "Vec"),
            new MethodDescriptor("op_Subtraction", 6, 6, 1, "Vec"),
            new MethodDescriptor("op_UnaryPlus", 7, 7, 1, "Vec"));
    }

    [Fact]
    public void EmitsImplicitAndExplicitConversionRows()
    {
        string source = """
            struct Money
            {
                int Amount;
                public static implicit operator int(Money m) => m.Amount;
                public static explicit operator Money(int n) => new();
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(
            new MethodDescriptor("op_Implicit", 4, 4, 1, "Money"),
            new MethodDescriptor("op_Explicit", 5, 5, 1, "Money"));
    }

    [Fact]
    public void EmitsFinalizerAsFinalizeRow()
    {
        string source = """
            class Handle
            {
                bool _disposed;
                ~Handle()
                {
                    if (!_disposed)
                    {
                        _disposed = true;
                    }
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        // ~T -> CLR Finalize; StartLine is the destructor decl line, EndLine the body end; base 1 + if.
        methods.Should().Equal(new MethodDescriptor("Finalize", 4, 10, 2, "Handle"));
    }

    [Fact]
    public void EmitsCustomEventAddRemoveRows()
    {
        string source = """
            class Bus
            {
                EventHandler? _h;
                event EventHandler Changed
                {
                    add { _h += value; }
                    remove { _h -= value; }
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(
            new MethodDescriptor("add_Changed", 6, 6, 1, "Bus"),
            new MethodDescriptor("remove_Changed", 7, 7, 1, "Bus"));
    }

    [Fact]
    public void SkipsFieldLikeEventAccessors()
    {
        string source = """
            class Bus
            {
                event EventHandler? Changed;
                int M() => 1;
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        // A field-like event is an EventFieldDeclarationSyntax (compiler-generated add/remove) -> no row.
        methods.Should().Equal(new MethodDescriptor("M", 4, 4, 1, "Bus"));
    }

    [Fact]
    public void EmitsUnsignedRightShiftOperator()
    {
        string source = """
            struct N
            {
                int V;
                public static N operator >>>(N a, int b) => a;
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(new MethodDescriptor("op_UnsignedRightShift", 4, 4, 1, "N"));
    }

    // T30 (docs/decisions.md departure #15 / D-T30a): coverlet #507 records only the FIRST accessor on a
    // shared physical source line and drops the rest, so the parser flags CoverletBlind on an accessor
    // whose declaration start line equals an EARLIER accessor's of the same member. First-on-line wins;
    // the flag is kind-agnostic (get/set/init/add/remove) and keyed on the accessor KEYWORD line, not the
    // body end. These pins assert the blind flag directly via full-record equality (P-b1..8).

    // P-b1 -- one-line expression-bodied get/set: get is first-on-line (not blind); set shares the line
    // (blind). The set expression carries three nested ?: -> CC 4 (the gate-relevant CC>=3 escape shape).
    [Fact]
    public void FlagsSecondExpressionAccessorOnSharedLineAsCoverletBlind()
    {
        string source = """
            class Sample
            {
                int _v;
                int Value { get => _v; set => _v = value > 0 ? value > 1 ? value > 2 ? value : 0 : 0 : 0; }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(
            new MethodDescriptor("get_Value", 4, 4, 1, "Sample", false),
            new MethodDescriptor("set_Value", 4, 4, 4, "Sample", true));
    }

    // P-b2 -- one-line block get/set: set shares get's line -> blind.
    [Fact]
    public void FlagsSecondBlockAccessorOnSharedLineAsCoverletBlind()
    {
        string source = """
            class Sample
            {
                int _v;
                int Value { get { return _v; } set { if (value > 0) { _v = value; } } }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(
            new MethodDescriptor("get_Value", 4, 4, 1, "Sample", false),
            new MethodDescriptor("set_Value", 4, 4, 2, "Sample", true));
    }

    // P-b3 -- accessors on SEPARATE lines: neither blind, even though the property spans many lines.
    [Fact]
    public void DoesNotFlagAccessorsOnSeparateLines()
    {
        string source = """
            class Sample
            {
                int _v;
                int Value
                {
                    get { return _v; }
                    set { _v = value; }
                }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(
            new MethodDescriptor("get_Value", 6, 6, 1, "Sample", false),
            new MethodDescriptor("set_Value", 7, 7, 1, "Sample", false));
    }

    // P-b4 -- get and set KEYWORDS share the line but the set body wraps over several lines: set is still
    // blind (start line, NOT body end). set_Value's EndLine (9) proves the wrap; its StartLine (4) proves
    // the shared-keyword-line detection.
    [Fact]
    public void FlagsSecondAccessorWhenKeywordsShareLineAndBodyWraps()
    {
        string source = """
            class Sample
            {
                int _v;
                int Value { get => _v; set {
                    if (value > 0)
                    {
                        _v = value;
                    }
                } }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(
            new MethodDescriptor("get_Value", 4, 4, 1, "Sample", false),
            new MethodDescriptor("set_Value", 4, 9, 2, "Sample", true));
    }

    // P-b5 -- one-line indexer: set_Item shares get_Item's line -> blind.
    [Fact]
    public void FlagsSecondIndexerAccessorOnSharedLineAsCoverletBlind()
    {
        string source = """
            class Sample
            {
                int[] _data;
                int this[int i] { get { return _data[i]; } set { _data[i] = value; } }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(
            new MethodDescriptor("get_Item", 4, 4, 1, "Sample", false),
            new MethodDescriptor("set_Item", 4, 4, 1, "Sample", true));
    }

    // P-b6 -- custom event: one-line add/remove -> remove_ blind; the multi-line form -> neither blind.
    [Fact]
    public void FlagsSecondEventAccessorOnSharedLineButNotAcrossLines()
    {
        string oneLine = """
            class Bus
            {
                EventHandler? _h;
                event EventHandler Changed { add { _h += value; } remove { _h -= value; } }
            }
            """;

        CSharpMethodParser.Parse(oneLine).Should().Equal(
            new MethodDescriptor("add_Changed", 4, 4, 1, "Bus", false),
            new MethodDescriptor("remove_Changed", 4, 4, 1, "Bus", true));

        string multiLine = """
            class Bus
            {
                EventHandler? _h;
                event EventHandler Changed
                {
                    add { _h += value; }
                    remove { _h -= value; }
                }
            }
            """;

        CSharpMethodParser.Parse(multiLine).Should().Equal(
            new MethodDescriptor("add_Changed", 6, 6, 1, "Bus", false),
            new MethodDescriptor("remove_Changed", 7, 7, 1, "Bus", false));
    }

    // P-b7 -- reverse order on one line: set is first-on-line (not blind), get is blind. The rule is
    // kind-agnostic -- first-on-line wins regardless of get/set kind, and document order drives emission.
    [Fact]
    public void FlagsFirstOnLineWinnerRegardlessOfAccessorKind()
    {
        string source = """
            class Sample
            {
                int _v;
                int Value { set { _v = value; } get { return _v; } }
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(
            new MethodDescriptor("set_Value", 4, 4, 1, "Sample", false),
            new MethodDescriptor("get_Value", 4, 4, 1, "Sample", true));
    }

    // P-b8 -- an expression-bodied get-only property is a single accessor: never routed through the
    // shared-line detection -> never blind.
    [Fact]
    public void DoesNotFlagExpressionBodiedGetOnlyProperty()
    {
        string source = """
            class Sample
            {
                int _v;
                int Value => _v;
            }
            """;

        IReadOnlyList<MethodDescriptor> methods = CSharpMethodParser.Parse(source);

        methods.Should().Equal(new MethodDescriptor("get_Value", 4, 4, 1, "Sample", false));
    }
}

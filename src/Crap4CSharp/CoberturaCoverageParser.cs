namespace Microsoft.Crap4CSharp;

using System.Globalization;
using System.Xml;
using System.Xml.Linq;

// Faithful port of crap4java's JacocoCoverageParser, re-hosted on the Coverlet -> Cobertura XML
// coverage format (docs/decisions.md: Coverage row). Java read JaCoCo's INSTRUCTION counters; this
// port reads Cobertura per-method <line> elements and counts LINES (departure #4; risk R1). The
// parsing algorithm, keying scheme, null/missing/malformed handling and skip semantics are otherwise
// preserved. Ratified adaptations, all recorded in docs/decisions.md:
//   * D-T10a -- secure XML read via XmlReaderSettings (XmlResolver = null so no external DTD/entity/
//     XInclude is ever fetched) instead of Java's DocumentBuilderFactory feature flags. DtdProcessing
//     is Ignore (not Prohibit): the resolver already blocks every external fetch, and Ignore keeps the
//     DOCTYPE-tolerance test faithful while still failing safely on a malicious entity reference.
//   * D-T10b -- NormalizeTypeName keeps the backtick generic arity untouched, emitting the frozen
//     reciprocal TypeName form that CSharpMethodParser.TypeNameOf produces (the load-bearing coverage
//     key -- any drift silently collapses a method's coverage to N/A).
//   * Key line -- Cobertura's <method> carries no declaration-line attribute, so the key uses the MIN
//     of the method's <line @number> values; T11's exact->nearest lookup (a faithful port of
//     CrapAnalyzer.lookupCoverage) resolves it into the method's own StartLine range.
// Java's non-instantiable `final class` with a private ctor maps to the idiomatic C# `static class`
// (public because the `internal` modifier is banned in the single-assembly design -- ratified C1).
public static class CoberturaCoverageParser
{
    private static readonly char[] AngleBrackets = ['<', '>'];

    // Ports parse(Path). A null or non-existent path yields an empty map (Java returns Map.of()), so
    // the parameter is nullable BY DESIGN and this method never throws for those two cases; module-level
    // fail-fast (departure #1) handles the empty map downstream. Any parse failure is wrapped as
    // InvalidOperationException (the repo's analog of Java's IllegalStateException).
    public static IReadOnlyDictionary<string, CoverageData> Parse(string? coberturaXmlPath)
    {
        if (coberturaXmlPath is null || !File.Exists(coberturaXmlPath))
        {
            return new Dictionary<string, CoverageData>(StringComparer.Ordinal);
        }

        try
        {
            // D-T10a: XmlResolver = null blocks every external fetch (DTD/entity/XInclude) -- the actual
            // XXE surface; DtdProcessing.Ignore tolerates (but never resolves) a DOCTYPE.
            XmlReaderSettings settings = new()
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
            };
            using XmlReader reader = XmlReader.Create(coberturaXmlPath, settings);
            XDocument document = XDocument.Load(reader);

            // StringComparer.Ordinal mirrors Java HashMap's ordinal key semantics and keeps keys aligned
            // with T11's ordinal prefix lookup (departure #3 determinism).
            Dictionary<string, CoverageData> coverage = new(StringComparer.Ordinal);
            foreach (XElement classElement in document.Descendants("class"))
            {
                string rawClassName = classElement.Attribute("name")?.Value ?? string.Empty;
                string typeName = NormalizeTypeName(rawClassName);
                ReadClassMethods(classElement, rawClassName, typeName, coverage);
            }

            return coverage;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Unable to parse Cobertura XML: " + coberturaXmlPath, ex);
        }
    }

    // The frozen-reciprocal normalizer (docs/decisions.md "Frozen reciprocal contract"). Coverlet already
    // emits the namespace dotted, the backtick arity per level, and a bare chain in the global namespace;
    // the only work is turning the nested-type separators ('/' or '+') into '.', which is the mechanical
    // inverse of CSharpMethodParser.TypeNameOf. Public so the reciprocity-table tests can call it directly
    // (the `internal` modifier is banned -- C1).
    public static string NormalizeTypeName(string rawClassName)
    {
        ArgumentNullException.ThrowIfNull(rawClassName);
        return rawClassName.Replace('/', '.').Replace('+', '.');
    }

    // The compiler-generated skip predicate (W-T9b/W-T10c). Returns true (skip) when EITHER the method's
    // own name OR its containing class is synthetic. Takes the raw (un-normalized) class name so the
    // angle-bracket state-machine / display-class markers are still visible. Public for the same
    // least-privilege-testable reason as NormalizeTypeName.
    public static bool IsCompilerGeneratedMethod(string rawClassName, string methodName)
    {
        ArgumentNullException.ThrowIfNull(rawClassName);
        ArgumentNullException.ThrowIfNull(methodName);

        // Rule 1: a synthetic containing class (async/iterator state machines Outer+<M>d__N, lambda
        // display classes Outer+<>c / Outer+<>c__DisplayClassN) -- skips all of its methods, including a
        // synthetic MoveNext (W-T10c: a user-authored MoveNext lives on a real class and is kept).
        if (rawClassName.IndexOfAny(AngleBrackets) >= 0)
        {
            return true;
        }

        // Rule 2: an angle-bracket display method hosted on a real class (lambdas <M>b__x_y, local
        // functions <M>g__L|x_y).
        if (methodName.IndexOfAny(AngleBrackets) >= 0)
        {
            return true;
        }

        // Rule 3: property/event accessors -- T9 never emits these (they are accessor declarations, not
        // method declarations).
        if (methodName.StartsWith("get_", StringComparison.Ordinal)
            || methodName.StartsWith("set_", StringComparison.Ordinal)
            || methodName.StartsWith("add_", StringComparison.Ordinal)
            || methodName.StartsWith("remove_", StringComparison.Ordinal))
        {
            return true;
        }

        // Rule 4: instance/static constructors -- T9 excludes constructors.
        return methodName is ".ctor" or ".cctor";
    }

    // Ports readClassMethods: walk ONLY <class>/<methods>/<method>, and per method ONLY its <lines>/<line>
    // children. The sibling class-level <lines> aggregate (which duplicates the union of all method lines)
    // is deliberately never descended into, and <line> attributes/children beyond @number/@hits (branch,
    // condition-coverage, <conditions>) are ignored.
    private static void ReadClassMethods(
        XElement classElement,
        string rawClassName,
        string typeName,
        Dictionary<string, CoverageData> coverage)
    {
        XElement? methodsElement = classElement.Element("methods");
        if (methodsElement is null)
        {
            return;
        }

        foreach (XElement method in methodsElement.Elements("method"))
        {
            string methodName = method.Attribute("name")?.Value ?? string.Empty;
            if (IsCompilerGeneratedMethod(rawClassName, methodName))
            {
                continue;
            }

            List<XElement> lines = method.Element("lines")?.Elements("line").ToList() ?? [];
            if (lines.Count == 0)
            {
                // Faithful analog of Java's null INSTRUCTION counter -> skip. A method that HAS lines but
                // all hits == 0 is a legitimate 0% entry and is kept.
                continue;
            }

            int coveredLines = lines.Count(line => ParseIntOrZero(line.Attribute("hits")?.Value) > 0);
            int missedLines = lines.Count(line => ParseIntOrZero(line.Attribute("hits")?.Value) == 0);
            int minChildLine = lines.Min(line => ParseIntOrZero(line.Attribute("number")?.Value));

            string key = typeName + "#" + methodName + ":" + minChildLine.ToString(CultureInfo.InvariantCulture);
            coverage[key] = new CoverageData(missedLines, coveredLines);
        }
    }

    // Ports parseInt(String)->0-on-failure, applied to both @hits and @number; InvariantCulture keeps it
    // locale-independent (departure #3).
    private static int ParseIntOrZero(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
}

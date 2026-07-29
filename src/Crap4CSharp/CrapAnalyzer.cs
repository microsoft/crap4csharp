namespace Microsoft.Crap4CSharp;

using System.Globalization;

// Faithful port of crap4java's CrapAnalyzer -- the flat composition layer (Slice S4) that reads the
// changed files itself and drives both parsers itself, exactly as the Java original does. Two ratified
// adaptations apply (docs/decisions.md):
//   * D-T9 -- Java derived one className per file via classNameFromSource (package regex + filename);
//     C# instead uses the per-method MethodDescriptor.TypeName produced by CSharpMethodParser (the
//     enclosing-type FQN coverage key), so classNameFromSource has no C# analog and is dropped (its
//     "no namespace -> bare name" behaviour is already pinned by CSharpMethodParserTests' global-
//     namespace form). Java's dead projectRoot param is dropped with it (behaviour-preserving).
//   * #7 (resolve-once) -- no module-grouping code lives here in Java either; the analyzer is already
//     flat (one file list, one coverage map), so this port has zero grouping structure (grouping is a
//     T14/CliApplication concern).
// The lookup helpers are public only because CrapAnalyzerTest calls them directly and the `internal`
// modifier is banned (ratified C1); ExactCoverage stays private (no test calls it directly). Lookup
// keys are built byte-for-byte from the frozen reciprocal TypeName form emitted by both
// CSharpMethodParser.TypeNameOf and CoberturaCoverageParser.NormalizeTypeName, plus a source-file
// basename segment (T24 / departure #12): TypeName#method#Path.GetFileName(file):line -- any drift
// silently collapses a method's coverage to N/A, and the trailing ':line' is load-bearing (kept LAST so
// ParseTrailingLine's last-':' split and the nearest-line scan are unchanged).
public static class CrapAnalyzer
{
    // Ports analyze: for each existing changed file, parse its methods, resolve each method's coverage
    // via the exact->nearest lookup, compose the CRAP score, and return the metrics sorted by CRAP
    // descending with N/A (null) last. A method absent from a populated report yields a row with null
    // coverage and null CRAP -- never 0.0 (W4) -- preserving the 0%-present vs absent distinction.
    public static IReadOnlyList<MethodMetrics> Analyze(
        IReadOnlyList<string> changedFiles,
        string coberturaXmlPath)
    {
        ArgumentNullException.ThrowIfNull(changedFiles);
        ArgumentNullException.ThrowIfNull(coberturaXmlPath);

        IReadOnlyDictionary<string, CoverageData> coverageMap =
            CoberturaCoverageParser.Parse(coberturaXmlPath);

        List<MethodMetrics> metrics = [];
        foreach (string file in changedFiles)
        {
            if (!File.Exists(file))
            {
                continue;
            }

            string source = File.ReadAllText(file);

            // T24 (departure #12): the basename dimension of the coverage key. Computed once per file
            // (every method parsed from this source shares it) and threaded into the lookup so per-file
            // partial-class overloads resolve to their OWN file's coverage. File.Exists gated above, so
            // this is always a real filename (never "").
            string sourceFile = Path.GetFileName(file);

            foreach (MethodDescriptor method in CSharpMethodParser.Parse(source))
            {
                double? coverage = LookupCoverage(
                    coverageMap,
                    typeName: method.TypeName,
                    methodName: method.Name,
                    sourceFile: sourceFile,
                    line: method.StartLine);
                double? crap = CrapScore.Calculate(method.Complexity, coverage);
                metrics.Add(new MethodMetrics(
                    MethodName: method.Name,
                    ClassName: method.TypeName,
                    Complexity: method.Complexity,
                    CoveragePercent: coverage,
                    CrapScore: crap));
            }
        }

        return
        [
            .. metrics
                .OrderBy(m => m.CrapScore is null)
                .ThenByDescending(m => m.CrapScore ?? 0.0),
        ];
    }

    // Ports lookupCoverage: an exact-line match wins; otherwise the nearest line within the same
    // method; otherwise null (N/A).
    public static double? LookupCoverage(
        IReadOnlyDictionary<string, CoverageData> coverageMap,
        string typeName,
        string methodName,
        string sourceFile,
        int line)
    {
        ArgumentNullException.ThrowIfNull(coverageMap);

        double? exact = ExactCoverage(coverageMap, typeName, methodName, sourceFile, line);
        if (exact is not null)
        {
            return exact;
        }

        CoverageData? nearest = NearestCoverage(coverageMap, typeName, methodName, sourceFile, line);
        return nearest?.CoveragePercent;
    }

    // Ports nearestCoverage: scan only entries under "typeName#methodName#sourceFile:" (ordinal prefix)
    // and keep the one whose trailing line is closest. The strict '<' means the first entry in
    // enumeration (document) order wins a distance tie -- the LinkedHashMap analog (W-T11a).
    public static CoverageData? NearestCoverage(
        IReadOnlyDictionary<string, CoverageData> coverageMap,
        string typeName,
        string methodName,
        string sourceFile,
        int line)
    {
        ArgumentNullException.ThrowIfNull(coverageMap);

        string prefix = typeName + "#" + methodName + "#" + sourceFile + ":";
        CoverageData? nearest = null;
        int nearestDistance = int.MaxValue;
        foreach (KeyValuePair<string, CoverageData> entry in coverageMap)
        {
            if (!entry.Key.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            int keyLine = ParseTrailingLine(entry.Key);
            int distance = Math.Abs(keyLine - line);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = entry.Value;
            }
        }

        return nearest;
    }

    // Ports parseTrailingLine: the integer after the last ':'; int.MaxValue when there is no colon, an
    // empty trailer, or a non-numeric trailer (InvariantCulture keeps parsing locale-independent --
    // departure #3).
    public static int ParseTrailingLine(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        int separator = key.LastIndexOf(':');
        if (separator < 0)
        {
            return int.MaxValue;
        }

        string lineText = key[(separator + 1)..];
        if (lineText.Length == 0)
        {
            return int.MaxValue;
        }

        return int.TryParse(lineText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
            ? n
            : int.MaxValue;
    }

    // Ports exactCoverage: private (no test calls it directly). Formats the line with InvariantCulture
    // so the exact key aligns byte-for-byte with T10's emitted keys (departure #3).
    private static double? ExactCoverage(
        IReadOnlyDictionary<string, CoverageData> coverageMap,
        string typeName,
        string methodName,
        string sourceFile,
        int line)
    {
        string exactKey = typeName + "#" + methodName + "#" + sourceFile + ":" + line.ToString(CultureInfo.InvariantCulture);
        return coverageMap.TryGetValue(exactKey, out CoverageData? exact) ? exact.CoveragePercent : null;
    }
}

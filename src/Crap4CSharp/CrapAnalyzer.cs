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

                // T30 (departure #15): coverlet #507 records only the FIRST accessor on a shared physical
                // source line and drops the rest from the cobertura XML, so an emitted same-line SECOND
                // accessor is structurally absent (hit-independent, no config remedy). When such a
                // CoverletBlind descriptor misses BOTH exact and nearest lookup (coverage is null) AND its
                // enclosing type is present in this populated map (evidence the type WAS instrumented),
                // score it as a real 0% -- uninvoked within an instrumented type -- instead of N/A, so an
                // uncalled high-CC second accessor no longer escapes the exit-2 gate. Every other absent-
                // from-populated-report member stays N/A (W4/departure #1); name-mangling residuals
                // (explicit-interface, non-literal/foreign [IndexerName], async/state-machine) are NOT
                // coverlet-blind, so they are untouched. The type-present guard only suppresses the
                // pathological enclosing-type-entirely-absent case (where N/A is correct).
                if (coverage is null
                    && method.CoverletBlind
                    && EnclosingTypePresent(coverageMap, method.TypeName, sourceFile))
                {
                    coverage = 0.0;
                }

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

    // T30 (departure #15): the type-present guard for the coverlet-blind 0%-promotion. Private (least-
    // privilege -- no test calls it directly; exercised through Analyze like ExactCoverage). True iff the
    // populated map holds any key under this exact enclosing type AND source-file basename -- i.e. the
    // surviving first accessor (or any sibling member) of the same type/file WAS instrumented. The '#'
    // after typeName is the exact-type delimiter (so "Foo" never matches "FooBar#"/"Foo.Nested#"), and
    // "#basename:" bounds the frozen key's basename segment (TypeName#method#basename:line, T24). Runs
    // only for the rare blind-AND-absent descriptor, so an O(keys) scan is fine (no precompute -- YAGNI).
    private static bool EnclosingTypePresent(
        IReadOnlyDictionary<string, CoverageData> coverageMap,
        string typeName,
        string sourceFile)
    {
        string typePrefix = typeName + "#";
        string basenameSegment = "#" + sourceFile + ":";
        foreach (string key in coverageMap.Keys)
        {
            if (key.StartsWith(typePrefix, StringComparison.Ordinal)
                && key.Contains(basenameSegment, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

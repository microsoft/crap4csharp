namespace Microsoft.Crap4CSharp;

using System.Globalization;
using System.Text.RegularExpressions;
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
//     reciprocal TypeName form that CSharpMethodParser.TypeNameOf produces -- the TypeName segment of
//     the load-bearing coverage key (TypeName#method#basename:line); any drift silently collapses a
//     method's coverage to N/A.
//   * Key line -- Cobertura's <method> carries no declaration-line attribute, so the key uses the MIN
//     of the method's <line @number> values; T11's exact->nearest lookup (a faithful port of
//     CrapAnalyzer.lookupCoverage) resolves it into the method's own StartLine range.
//   * D-T24 (departure #12) -- the coverage key gains a source-file basename segment before ":line":
//     TypeName#method#Path.GetFileName(<class filename>):line. A C# partial class split across files can
//     declare overloads (and, post-T23, compiler-generated members) at overlapping min-lines -- coverlet
//     emits one <class> per file, so the pre-T24 TypeName#method:line collided. The basename segregates
//     the per-file entries; ":line" stays LAST so ParseTrailingLine is unchanged, and NormalizeTypeName
//     is unchanged (basename is a NEW segment, not part of TypeName). A missing/empty filename yields an
//     empty basename -> an unmatchable Type#method#:line key (per-method N/A, never mis-attributed).
// Java's non-instantiable `final class` with a private ctor maps to the idiomatic C# `static class`
// (public because the `internal` modifier is banned in the single-assembly design -- ratified C1).
public static partial class CoberturaCoverageParser
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

            // T23: compiler-generated MEMBERS (lambdas, local functions, async-locals) are attributed to
            // their enclosing SOURCE method and UNIONed with that method's coverage. Collected in phase 1
            // (document order) then folded in a phase-2 nearest-body merge (sec.3) after the loop -- the
            // inner merge scan is read-only, so the map is never enumerated while mutated.
            List<SyntheticContribution> contributions = [];
            foreach (XElement classElement in document.Descendants("class"))
            {
                string rawClassName = classElement.Attribute("name")?.Value ?? string.Empty;

                // T24 (departure #12): basename segment of the coverage key. Read once per <class> from
                // its filename attribute; Path.GetFileName strips any absolute/relative dir prefix
                // (coverlet may emit either, '/'- or '\'-separated). A missing/empty attribute yields ""
                // -> an unmatchable key (D-T24d), never a mis-attribution.
                string basename = Path.GetFileName(classElement.Attribute("filename")?.Value ?? string.Empty);

                // T23 class-level routing (generalizes T21's state-machine interception to EVERY
                // compiler-generated member). Precedence A -> B -> C (contract sec.1):
                //   A  state-machine class (Enclosing/<inner>d__N), matched by TryParseStateMachine:
                //      A1  inner is ITSELF a synthetic member name (<M>g__.../<M>b__...)  -> ASYNC-LOCAL
                //          (async local function or async lambda): COLLECT its MoveNext coverage as a
                //          phase-2 SyntheticContribution attributed to the outer source method M.
                //      A2  else  -> PLAIN async/iterator (T21): DIRECT-EMIT via the UNCHANGED
                //          ReadStateMachineMoveNext (byte-for-byte T21 behaviour).
                //   B  lambda display class (<>c static cache / <>c__DisplayClassN capturing closure):
                //      COLLECT each <M>b__/<M>g__ member as a SyntheticContribution.
                //   C  real class: ReadClassMethods (which itself COLLECTS real-class-hosted local
                //      functions / lambdas, sec.1.C, before its unchanged skip predicate).
                // Precedence is unambiguous: a plain d__ has inner="M" (A2); an async-local d__ has
                // inner="<M>g__L|N" (A1); a <>c never carries a >d__N tail so it can only reach B.
                if (TryParseStateMachine(rawClassName, out string enclosingRaw, out string inner))
                {
                    Match memberMatch = SyntheticMemberNameRegex().Match(inner);
                    if (memberMatch.Success)
                    {
                        CollectAsyncLocalContribution(
                            classElement, NormalizeTypeName(enclosingRaw), memberMatch.Groups[1].Value, basename, contributions);
                    }
                    else
                    {
                        ReadStateMachineMoveNext(classElement, NormalizeTypeName(enclosingRaw), inner, basename, coverage);
                    }
                }
                else if (TryGetDisplayClassEnclosing(rawClassName, out string displayEnclosingRaw))
                {
                    CollectDisplayClassContributions(
                        classElement, NormalizeTypeName(displayEnclosingRaw), basename, contributions);
                }
                else
                {
                    ReadClassMethods(classElement, rawClassName, NormalizeTypeName(rawClassName), basename, coverage, contributions);
                }
            }

            // Phase 2 (sec.3): union each collected synthetic member into its enclosing source method.
            MergeSyntheticContributions(contributions, coverage);

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

        // Rule 1: a synthetic containing class. Post-T21/T23 the async/iterator state machines
        // (Enclosing/<Method>d__N) AND the lambda display classes (Outer+<>c / Outer+<>c__DisplayClassN)
        // are all diverted upstream in Parse (state machines to A, display classes to B) and never reach
        // this predicate, so rule 1 is effectively dead on the real-class path -- as with T21, every
        // angle-bracket CLASS is routed away before ReadClassMethods calls in.
        if (rawClassName.IndexOfAny(AngleBrackets) >= 0)
        {
            return true;
        }

        // Rule 2: an angle-bracket display method hosted on a real class (lambdas <M>b__x_y, local
        // functions <M>g__L|x_y). Post-T23 ReadClassMethods attributes the RECOGNIZED b__/g__ members to
        // their source method BEFORE calling this predicate, so rule 2 now only catches the residual
        // unrecognized angle-bracket names (e.g. a synthetic that is neither b__ nor g__).
        if (methodName.IndexOfAny(AngleBrackets) >= 0)
        {
            return true;
        }

        // Rule 3: instance/static constructors -- T9 excludes constructors.
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
        string basename,
        Dictionary<string, CoverageData> coverage,
        List<SyntheticContribution> contributions)
    {
        XElement? methodsElement = classElement.Element("methods");
        if (methodsElement is null)
        {
            return;
        }

        foreach (XElement method in methodsElement.Elements("method"))
        {
            string methodName = method.Attribute("name")?.Value ?? string.Empty;

            // 1.C (T23): a real-class-hosted synthetic member -- a local function <M>g__Local|N_M, or a
            // lambda <M>b__N the compiler emitted directly on the real type -- is ATTRIBUTED to its
            // source method M on THIS type and COLLECTED as a phase-2 contribution. Checked BEFORE the
            // skip predicate so it is UNIONed into M's coverage rather than dropped wholesale (closes the
            // finding-#3 false pass). IsCompilerGeneratedMethod is unchanged; only this ordering changes.
            Match memberMatch = SyntheticMemberNameRegex().Match(methodName);
            if (memberMatch.Success)
            {
                CollectSyntheticMember(method, typeName, memberMatch.Groups[1].Value, basename, contributions);
                continue;
            }

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

            // T24: TypeName#method#basename:line -- the basename segregates per-file overloads of a
            // partial class; ":line" (MIN @number) stays LAST for the reciprocal nearest-line lookup.
            string key = typeName + "#" + methodName + "#" + basename + ":" + minChildLine.ToString(CultureInfo.InvariantCulture);
            coverage[key] = new CoverageData(missedLines, coveredLines);
        }
    }

    // R3 (T21) state-machine matcher. Anchored on the class's LAST nested segment: `<Method>d__N`, where
    // the `.+` capture guarantees a NON-EMPTY source-method name between the angle brackets. This is what
    // EXCLUDES the lambda display classes `<>c` / `<>c__DisplayClassN` (empty name -> no match), so those
    // are routed to the T23 display-class collector (B) rather than treated as state machines.
    [GeneratedRegex(@"^<(.+)>d__\d+$")]
    private static partial Regex StateMachineNameRegex();

    // T23 demangler. Extracts the SOURCE method from a compiler-generated MEMBER name `<SourceMethod>b__…`
    // (lambda) or `<SourceMethod>g__…` (local function). `[^<>]+` = the FIRST angle-bracket group; a
    // user SourceMethod never contains angle brackets. Also matches the INNER name of an async-local
    // state machine (`<SourceMethod>g__Local|N_M`), so the same regex re-extracts the outer source method
    // in the A1 case. Deliberately anchored with `[bg]__`: a plain async/iterator inner (`M`, no leading
    // `<`) does NOT match, so it stays on the A2 direct-emit (T21) path.
    [GeneratedRegex(@"^<([^<>]+)>[bg]__")]
    private static partial Regex SyntheticMemberNameRegex();

    // R3 (T21) demangler. Splits a Cobertura class @name into its enclosing type and the source-method
    // name a state machine was lowered from, when -- and only when -- the class's LAST nested segment is
    // `<Method>d__N`. Takes the raw (un-normalized) name so the '/' or '+' nesting separators and the
    // angle-bracket markers are still visible. Returns false (with both out params emptied) for any class
    // that is not a state machine, including top-level classes (no separator) and lambda display classes
    // (<>c / <>c__DisplayClassN -- empty name between the brackets fails the regex's non-empty capture).
    private static bool TryParseStateMachine(string rawClassName, out string enclosingRaw, out string sourceMethod)
    {
        enclosingRaw = string.Empty;
        sourceMethod = string.Empty;

        int sep = Math.Max(rawClassName.LastIndexOf('/'), rawClassName.LastIndexOf('+'));
        if (sep < 0)
        {
            return false;
        }

        string lastSegment = rawClassName[(sep + 1)..];
        Match match = StateMachineNameRegex().Match(lastSegment);
        if (!match.Success)
        {
            return false;
        }

        enclosingRaw = rawClassName[..sep];
        sourceMethod = match.Groups[1].Value;
        return true;
    }

    // R3 (T21) attribution. A state machine's user-authored body is compiler-lowered onto MoveNext, so
    // MoveNext's <line> hits ARE the source method's real coverage. Emit a single entry keyed on the
    // ENCLOSING type + the demangled source-method name + the source-file basename (T24), counted
    // IDENTICALLY to ReadClassMethods (covered = hits > 0, missed = hits == 0, key line = MIN @number).
    // D-T21a: attribute MoveNext ONLY, never aggregate the class -- the sibling synthetics (.ctor /
    // SetStateMachine / IDisposable.Dispose / get_Current / Reset / GetEnumerator) carry only synthetic
    // noise. If MoveNext is absent, or present but line-less, emit NOTHING (the faithful analog of
    // ReadClassMethods' "no lines -> skip"; the method stays N/A and T11's nearest-line lookup resolves
    // it from the source method's StartLine).
    private static void ReadStateMachineMoveNext(
        XElement classElement,
        string normalizedEnclosing,
        string sourceMethod,
        string basename,
        Dictionary<string, CoverageData> coverage)
    {
        XElement? moveNext = classElement.Element("methods")?
            .Elements("method")
            .FirstOrDefault(method =>
                string.Equals(method.Attribute("name")?.Value, "MoveNext", StringComparison.Ordinal));
        if (moveNext is null)
        {
            return;
        }

        List<XElement> lines = moveNext.Element("lines")?.Elements("line").ToList() ?? [];
        if (lines.Count == 0)
        {
            return;
        }

        int coveredLines = lines.Count(line => ParseIntOrZero(line.Attribute("hits")?.Value) > 0);
        int missedLines = lines.Count(line => ParseIntOrZero(line.Attribute("hits")?.Value) == 0);
        int minChildLine = lines.Min(line => ParseIntOrZero(line.Attribute("number")?.Value));

        // T24: EnclosingType#sourceMethod#basename:line -- same shape as ReadClassMethods; ":line" LAST.
        string key = normalizedEnclosing + "#" + sourceMethod + "#" + basename + ":" + minChildLine.ToString(CultureInfo.InvariantCulture);
        coverage[key] = new CoverageData(missedLines, coveredLines);
    }

    // T23 (sec.3) phase-1 carrier: one compiler-generated member's coverage counts, tagged with the
    // enclosing type + demangled source method + source-file basename, plus its own MIN @number so
    // phase 2 can anchor it to the NEAREST body/overload entry.
    private readonly record struct SyntheticContribution(
        string EnclosingType, string SourceMethod, string Basename, int Missed, int Covered, int MinLine);

    // T23 (B-case router). A lambda display class is a nested type whose LAST '/'|'+' segment starts with
    // "<>c" (`<>c` static-lambda cache or `<>c__DisplayClassN_M` capturing closure). Splits IDENTICALLY
    // to TryParseStateMachine to yield the raw enclosing type. A name with no separator yields an empty
    // enclosing -> an unmatchable key (safe N/A, D-T24d family), never a mis-attribution.
    private static bool TryGetDisplayClassEnclosing(string rawClassName, out string enclosingRaw)
    {
        enclosingRaw = string.Empty;

        int sep = Math.Max(rawClassName.LastIndexOf('/'), rawClassName.LastIndexOf('+'));
        string lastSegment = sep < 0 ? rawClassName : rawClassName[(sep + 1)..];
        if (!lastSegment.StartsWith("<>c", StringComparison.Ordinal))
        {
            return false;
        }

        enclosingRaw = sep < 0 ? string.Empty : rawClassName[..sep];
        return true;
    }

    // T23 (A1). An async-local state machine lowers its user body onto MoveNext exactly like a plain
    // async/iterator (T21); the only difference is that its coverage attributes to the OUTER source
    // method and UNIONs with that method's other coverage, so it is COLLECTED (phase-2 merge) instead of
    // direct-emitted. Absent/line-less MoveNext yields no contribution (ReadStateMachineMoveNext analog).
    private static void CollectAsyncLocalContribution(
        XElement classElement,
        string enclosingType,
        string sourceMethod,
        string basename,
        List<SyntheticContribution> contributions)
    {
        XElement? moveNext = classElement.Element("methods")?
            .Elements("method")
            .FirstOrDefault(method =>
                string.Equals(method.Attribute("name")?.Value, "MoveNext", StringComparison.Ordinal));
        if (moveNext is null)
        {
            return;
        }

        CollectSyntheticMember(moveNext, enclosingType, sourceMethod, basename, contributions);
    }

    // T23 (B). Each <method> on a lambda display class whose name demangles via SyntheticMemberNameRegex
    // (<M>b__/<M>g__) is COLLECTED against its source method M; the .ctor/.cctor/cached-delegate helpers
    // (which do NOT demangle) are ignored.
    private static void CollectDisplayClassContributions(
        XElement classElement,
        string enclosingType,
        string basename,
        List<SyntheticContribution> contributions)
    {
        XElement? methodsElement = classElement.Element("methods");
        if (methodsElement is null)
        {
            return;
        }

        foreach (XElement method in methodsElement.Elements("method"))
        {
            string methodName = method.Attribute("name")?.Value ?? string.Empty;
            Match memberMatch = SyntheticMemberNameRegex().Match(methodName);
            if (!memberMatch.Success)
            {
                continue;
            }

            CollectSyntheticMember(method, enclosingType, memberMatch.Groups[1].Value, basename, contributions);
        }
    }

    // T23 (sec.2/3) contribution builder. Counts a synthetic member's OWN <lines> IDENTICALLY to
    // ReadClassMethods (covered = hits > 0, missed = hits == 0, MinLine = MIN @number) and appends ONE
    // SyntheticContribution. A member with zero <line>s yields NO contribution (the faithful analog of
    // ReadClassMethods' "no lines -> skip").
    private static void CollectSyntheticMember(
        XElement method,
        string enclosingType,
        string sourceMethod,
        string basename,
        List<SyntheticContribution> contributions)
    {
        List<XElement> lines = method.Element("lines")?.Elements("line").ToList() ?? [];
        if (lines.Count == 0)
        {
            return;
        }

        int coveredLines = lines.Count(line => ParseIntOrZero(line.Attribute("hits")?.Value) > 0);
        int missedLines = lines.Count(line => ParseIntOrZero(line.Attribute("hits")?.Value) == 0);
        int minChildLine = lines.Min(line => ParseIntOrZero(line.Attribute("number")?.Value));

        contributions.Add(new SyntheticContribution(enclosingType, sourceMethod, basename, missedLines, coveredLines, minChildLine));
    }

    // T23 (sec.3) phase-2 nearest-body MERGE. For each collected contribution (document order), fold it
    // into the NEAREST existing `Type#SourceMethod#basename:*` entry -- else CREATE one. This is a
    // faithful UNION, not a double-count: every physical source line belongs to exactly ONE coverlet
    // <method>, so body lines and synthetic lines are DISJOINT and summing (missed,covered) equals the
    // set union. Nearest-by-line (strict '<', first-in-enumeration wins ties) MIRRORS CrapAnalyzer.-
    // NearestCoverage / W-T11a, so a synthetic anchors to its OWN overload's body span -- distinct
    // same-file overloads are never blended (C6 preserved). A no-anchor method gets a CREATED entry at
    // the synthetic's MinLine; a second no-anchor synthetic of the same method then finds and unions into
    // it. The inner scan is read-only; the add/update happens after it -- no enumerate-while-mutate.
    private static void MergeSyntheticContributions(
        List<SyntheticContribution> contributions,
        Dictionary<string, CoverageData> coverage)
    {
        foreach (SyntheticContribution c in contributions)
        {
            string prefix = c.EnclosingType + "#" + c.SourceMethod + "#" + c.Basename + ":";
            string? bestKey = null;
            int bestDistance = int.MaxValue;
            foreach (KeyValuePair<string, CoverageData> entry in coverage)
            {
                if (!entry.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                int distance = Math.Abs(ParseTrailingLine(entry.Key) - c.MinLine);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestKey = entry.Key;
                }
            }

            if (bestKey is not null)
            {
                CoverageData existing = coverage[bestKey];
                coverage[bestKey] = new CoverageData(existing.MissedLines + c.Missed, existing.CoveredLines + c.Covered);
            }
            else
            {
                coverage[prefix + c.MinLine.ToString(CultureInfo.InvariantCulture)] = new CoverageData(c.Missed, c.Covered);
            }
        }
    }

    // Producer-local last-':' line parse for the phase-2 nearest-anchor scan. Deliberately NOT reusing
    // CrapAnalyzer.ParseTrailingLine (sec.3: the producer must not depend on the consumer); mirrors its
    // semantics -- the integer after the last ':', int.MaxValue for a missing/empty/non-numeric trailer.
    private static int ParseTrailingLine(string key)
    {
        int separator = key.LastIndexOf(':');
        if (separator < 0)
        {
            return int.MaxValue;
        }

        string lineText = key[(separator + 1)..];
        return int.TryParse(lineText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
            ? n
            : int.MaxValue;
    }

    // Ports parseInt(String)->0-on-failure, applied to both @hits and @number; InvariantCulture keeps it
    // locale-independent (departure #3).
    private static int ParseIntOrZero(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
}

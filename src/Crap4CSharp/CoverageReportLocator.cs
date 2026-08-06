namespace Microsoft.Crap4CSharp;

// A C#-specific addition with no Java counterpart. JaCoCo wrote its report to a FIXED path
// (target/site/jacoco/jacoco.xml) that Java's caller existence-checked directly, so crap4java needed no
// locator. Coverlet's `--collect:XPlat Code Coverage` instead writes coverage.cobertura.xml under a
// NON-DETERMINISTIC per-run GUID subdirectory of the results directory, so this locator finds the emitted
// report. Pure function, no process execution -- mirrors the OwningProjectResolver / SourceFileFinder /
// CoberturaCoverageParser precedent (static class; public because the `internal` modifier is banned, C1).
//
// Under Model B (departure #9) exactly ONE report is expected per run: one owning project => one test
// project => one coverage.cobertura.xml. MORE than one report means the test project multi-targets
// (<TargetFrameworks>) so coverlet emitted one report per TFM -- which CliApplication fail-fasts on (T22),
// because a nondeterministic single-pick would flip the process exit code across runs on unchanged code.
// LocateAll therefore surfaces EVERY match instead of single-picking: the list is ordinal-sorted for
// deterministic order (departure #3) but NO LONGER collapses to one winner.
//
// The fail-fast gate is T14/T22, not here: LocateAll returns an EMPTY list when no report was produced (the
// no-report signal CliApplication acts on) and the full match list otherwise. It never chooses an exit code
// and never inspects report contents.
//
// Enumeration filters by an ordinal filename equality (NOT a "*.cobertura.xml" glob) -- mirrors
// OwningProjectResolver's deliberate anti-glob discipline (dodges the Windows 8.3-shortname quirk; exact
// ordinal match to coverlet's always-lowercase name), reading the same results directory CoverageRunner
// wrote (D-T12e). No timestamp/mtime logic: CoverageRunner deletes coverage/ before the run, so every file
// found afterward is fresh.
public static class CoverageReportLocator
{
    // Coverlet always emits this exact lowercase filename.
    private const string CoberturaReportFileName = "coverage.cobertura.xml";

    // Finds every coverage.cobertura.xml coverlet emitted under <projectRoot>/coverage/<GUID>/,
    // ordinal-sorted; an EMPTY list when none was produced (the T14 no-report fail-fast signal). Stays a
    // pure, exit-free, throw-free enumerator: CliApplication owns the 0 / 1 / >1 branch and every exit
    // code (D-T14a). Under Model B a single-TFM test project yields exactly ONE report; a multi-targeted
    // (<TargetFrameworks>) one yields one per TFM, which T22 fail-fasts on at the CliApplication gate.
    public static IReadOnlyList<string> LocateAll(string projectRoot)
    {
        ArgumentNullException.ThrowIfNull(projectRoot);

        string resultsDirectory = Path.Combine(projectRoot, CoverageRunner.ResultsDirectoryName);
        if (!Directory.Exists(resultsDirectory))
        {
            return [];
        }

        return
        [
            .. Directory
                .EnumerateFiles(resultsDirectory, "*", SearchOption.AllDirectories)
                .Where(file => Path.GetFileName(file).Equals(CoberturaReportFileName, StringComparison.Ordinal))
                .OrderBy(file => file, StringComparer.Ordinal),
        ];
    }
}

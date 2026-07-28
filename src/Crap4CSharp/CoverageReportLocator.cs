namespace Microsoft.Crap4CSharp;

// A C#-specific addition with no Java counterpart. JaCoCo wrote its report to a FIXED path
// (target/site/jacoco/jacoco.xml) that Java's caller existence-checked directly, so crap4java needed no
// locator. Coverlet's `--collect:XPlat Code Coverage` instead writes coverage.cobertura.xml under a
// NON-DETERMINISTIC per-run GUID subdirectory of the results directory, so this locator finds the emitted
// report. Pure function, no process execution -- mirrors the OwningProjectResolver / SourceFileFinder /
// CoberturaCoverageParser precedent (static class; public because the `internal` modifier is banned, C1).
//
// Under Model B (departure #9) exactly ONE report is expected per run (one test project => one report; the
// multi-report aggregation scope of the retired departure #8 no longer applies). The ordinal-first
// FirstOrDefault pick is retained as DEFENSIVE DETERMINISM -- harmless with a single match, OS-independent
// should more than one ever appear (departure #3).
//
// The fail-fast gate is T14, not here: Locate returns null when no report was produced (the signal T14 acts
// on). It never chooses an exit code and never inspects report contents.
public static class CoverageReportLocator
{
    // Coverlet always emits this exact lowercase filename.
    private const string CoberturaReportFileName = "coverage.cobertura.xml";

    // Finds the coverage.cobertura.xml coverlet emits under <projectRoot>/coverage/<GUID>/. Returns the
    // absolute path, or null when no report was produced (the T14 fail-fast signal).
    public static string? Locate(string projectRoot)
    {
        ArgumentNullException.ThrowIfNull(projectRoot);

        // Reciprocal contract with CoverageRunner (D-T12e): read the same results directory the runner wrote.
        string resultsDirectory = Path.Combine(projectRoot, CoverageRunner.ResultsDirectoryName);
        if (!Directory.Exists(resultsDirectory))
        {
            return null;
        }

        // Enumerate "*" then filter by an ordinal filename equality (NOT a "*.cobertura.xml" glob) -- mirrors
        // OwningProjectResolver's deliberate anti-glob discipline (dodges the Windows 8.3-shortname quirk; exact
        // ordinal match to coverlet's always-lowercase name). OrderBy(Ordinal) makes a multi-match pick
        // deterministic and OS-independent (departure #3); FirstOrDefault returns null on zero matches (the
        // fail-fast signal). No timestamp/mtime logic: CoverageRunner deletes coverage/ before the run, so
        // every file found afterward is fresh.
        return Directory
            .EnumerateFiles(resultsDirectory, "*", SearchOption.AllDirectories)
            .Where(file => Path.GetFileName(file).Equals(CoberturaReportFileName, StringComparison.Ordinal))
            .OrderBy(file => file, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}

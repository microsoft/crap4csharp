namespace Microsoft.Crap4CSharp;

using System.Globalization;

// Faithful port of crap4java's CoverageRunner, re-hosted on the .NET coverage pipeline (docs/decisions.md:
// Coverage row; O1 ecosystem-adapter swap). Java's `final class` maps to C# `sealed`. generateCoverage
// (a) deletes stale coverage artifacts, (b) runs the coverage command once via the injected CommandExecutor
// seam, and (c) throws on a non-zero exit -- all three preserved 1:1. Ratified adaptations, all recorded in
// docs/decisions.md:
//   * O1 -- the Maven+JaCoCo command becomes `dotnet test <TestProject> --collect:XPlat Code Coverage
//     --filter "type!=IntegrationTests" --results-directory coverage`, and the two JaCoCo artifacts
//     (target/site/jacoco + target/jacoco.exec) collapse to the single "coverage" results directory.
//   * Model B (departure #9): the `dotnet test` target is now the EXPLICIT resolved test-project path
//     (TestProjectResolver), passed as testProject -- no more cwd project discovery. coverageBaseDirectory
//     (the invocation root) is both the working directory and the base for the --results-directory.
//   * Unit-only target-test selection (departure #10): the `--filter "type!=IntegrationTests"` token pair
//     mirrors mutate4csharp's UnitTestFilter, running only the analyzed target's UNIT tests. The EXCLUSION
//     form is mandatory: VSTest treats an ABSENT `type` property as satisfying `!=` any value, so untagged
//     methods are INCLUDED (accepted: type in {UnitTests, Unit, absent}); only type=IntegrationTests is
//     excluded. An inclusion form (e.g. type=UnitTests) would WRONGLY drop untagged tests. UnitTestFilter is
//     the single source of truth for the accepted-trait set; narrowing to {UnitTests, absent} is the one-line
//     "type!=IntegrationTests&type!=Unit" edit -- still exclusion-form, so untagged stays included. crap4csharp
//     carries ONLY this clause; mutate4csharp's mutation-cycle-only Category!=no-mutate clause has no analog.
//   * IllegalStateException -> the domain CoverageException (introduced in T15 per Mr. Das ruling A): the
//     coverage-command failure now throws CoverageException so the entry point can catch it SPECIFICALLY
//     (CA1031-clean, no suppression) and convert it to exit 1. The exit-failure message is byte-identical to
//     Java: "Coverage command failed with exit N".
//
// Resolve-once (departure #7): this type takes a PRE-RESOLVED testProject + coverage base and never resolves
// on its own; it runs coverage exactly once (no module-group loop). The fail-fast gate is T14, not here:
// GenerateCoverage throws on command failure (faithful), and the separate CoverageReportLocator returns null
// when no report was produced (the signal T14 acts on).
public sealed class CoverageRunner
{
    // Shared reciprocal contract with CoverageReportLocator (D-T12e): the writer (this runner) and the reader
    // (the locator) must agree byte-for-byte on the results-directory name, so they share this single source
    // of truth. Matches README "Coverage Pipeline" steps 1-2.
    public const string ResultsDirectoryName = "coverage";

    // Single source of truth for the accepted-trait set (departure #10). EXCLUSION form: VSTest treats an
    // absent `type` property as `!=` any value, so untagged target tests are INCLUDED; only
    // type=IntegrationTests is excluded. Least-privilege private const (golden rule #9 -- never internal);
    // mirrors mutate4csharp's UnitTestFilter constant name for cross-repo legibility.
    private const string UnitTestFilter = "type!=IntegrationTests";

    private readonly ICommandExecutor _executor;

    public CoverageRunner(ICommandExecutor executor)
    {
        // Java implicitly NPEs on a null executor; the idiomatic, suppression-free C# equivalent (and CA1062's
        // required guard) is a fail-fast ArgumentNullException.
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
    }

    // Ports generateCoverage: delete the stale results dir under coverageBaseDirectory, run the coverage
    // command once against the resolved testProject (unit-only via --filter), throw CoverageException on a
    // non-zero exit. Returns void.
    public void GenerateCoverage(string testProject, string coverageBaseDirectory)
    {
        ArgumentNullException.ThrowIfNull(testProject);
        ArgumentNullException.ThrowIfNull(coverageBaseDirectory);

        // Delete stale artifacts (analog of Java deleting target/site/jacoco + target/jacoco.exec; README step
        // 1 deletes coverage/). Java's deleteIfExists no-ops when absent and deletes recursively when present;
        // we map both Java artifacts to the single results directory, so only the directory branch is needed.
        string resultsDirectory = Path.Combine(coverageBaseDirectory, ResultsDirectoryName);
        if (Directory.Exists(resultsDirectory))
        {
            Directory.Delete(resultsDirectory, recursive: true);
        }

        // Run the coverage command via the injected seam against the EXPLICIT resolved test-project token
        // (Model B, departure #9), unit-only via the --filter token pair (departure #10). --results-directory
        // is relative to coverageBaseDirectory (the working directory), forwarded verbatim -- no normalization
        // (faithful: T14 already injects the normalized invocation root).
        int exit = _executor.Run(
            ["dotnet", "test", testProject, "--collect:XPlat Code Coverage",
             "--filter", UnitTestFilter, "--results-directory", ResultsDirectoryName],
            coverageBaseDirectory);

        if (exit != 0)
        {
            // Byte-identical to Java; InvariantCulture keeps the exit number locale-independent (CA1305,
            // departure #3).
            throw new CoverageException(
                "Coverage command failed with exit " + exit.ToString(CultureInfo.InvariantCulture));
        }
    }
}

namespace Microsoft.Crap4CSharp;

using System.Globalization;

// Faithful port of crap4java's CoverageRunner, re-hosted on the .NET coverage pipeline (docs/decisions.md:
// Coverage row; O1 ecosystem-adapter swap). Java's `final class` maps to C# `sealed`. generateCoverage
// (a) deletes stale coverage artifacts, (b) runs the coverage command once at projectRoot via the injected
// CommandExecutor seam, and (c) throws on a non-zero exit -- all three preserved 1:1. Two ratified
// adaptations, both recorded in docs/decisions.md:
//   * O1 -- the Maven+JaCoCo command becomes `dotnet test --collect:XPlat Code Coverage --results-directory
//     coverage`, and the two JaCoCo artifacts (target/site/jacoco + target/jacoco.exec) collapse to the
//     single "coverage" results directory. `dotnet test` with no project/solution argument discovers the
//     project in the working directory (projectRoot), exactly as `mvn` did.
//   * IllegalStateException -> the domain CoverageException (introduced in T15 per Mr. Das ruling A): the
//     coverage-command failure now throws CoverageException so the entry point can catch it SPECIFICALLY
//     (CA1031-clean, no suppression) and convert it to exit 1. The exit-failure message is byte-identical to
//     Java: "Coverage command failed with exit N".
//
// Resolve-once (departure #7): this type takes a PRE-RESOLVED projectRoot and never calls ModuleRootResolver;
// it runs coverage exactly once at the one root (no module-group loop). The fail-fast gate is T14, not here:
// GenerateCoverage throws on command failure (faithful), and the separate CoverageReportLocator returns null
// when no report was produced (the signal T14 acts on).
public sealed class CoverageRunner
{
    // Shared reciprocal contract with CoverageReportLocator (D-T12e): the writer (this runner) and the reader
    // (the locator) must agree byte-for-byte on the results-directory name, so they share this single source
    // of truth. Matches README "Coverage Pipeline" steps 1-2.
    public const string ResultsDirectoryName = "coverage";

    private readonly ICommandExecutor _executor;

    public CoverageRunner(ICommandExecutor executor)
    {
        // Java implicitly NPEs on a null executor; the idiomatic, suppression-free C# equivalent (and CA1062's
        // required guard) is a fail-fast ArgumentNullException.
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
    }

    // Ports generateCoverage(Path): delete the stale results dir, run the coverage command once at projectRoot,
    // throw CoverageException on a non-zero exit. Returns void.
    public void GenerateCoverage(string projectRoot)
    {
        ArgumentNullException.ThrowIfNull(projectRoot);

        // Delete stale artifacts (analog of Java deleting target/site/jacoco + target/jacoco.exec; README step
        // 1 deletes coverage/). Java's deleteIfExists no-ops when absent and deletes recursively when present;
        // we map both Java artifacts to the single results directory, so only the directory branch is needed.
        string resultsDirectory = Path.Combine(projectRoot, ResultsDirectoryName);
        if (Directory.Exists(resultsDirectory))
        {
            Directory.Delete(resultsDirectory, recursive: true);
        }

        // Run the coverage command via the injected seam. projectRoot is forwarded verbatim as the working
        // directory -- no Path.GetFullPath, no normalization (faithful: Java passed projectRoot unchanged to
        // executor.run; T14 already injects the normalized resolved root).
        int exit = _executor.Run(
            ["dotnet", "test", "--collect:XPlat Code Coverage", "--results-directory", ResultsDirectoryName],
            projectRoot);

        if (exit != 0)
        {
            // Byte-identical to Java; InvariantCulture keeps the exit number locale-independent (CA1305,
            // departure #3).
            throw new CoverageException(
                "Coverage command failed with exit " + exit.ToString(CultureInfo.InvariantCulture));
        }
    }
}

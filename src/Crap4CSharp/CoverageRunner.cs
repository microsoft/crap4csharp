namespace Microsoft.Crap4CSharp;

using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

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
// Selection/threshold scope: custom filter replaces the default, all-tests omits it, one framework may
// be requested, and dry-run consumes the same pure BuildCommand as execution.
// Resolve-once (departure #7): this type takes PRE-RESOLVED test projects + coverage base and never resolves
// on its own. The approved multi-suite extension plans one command per project and clears coverage once.
// The fail-fast gate is T14, not here:
// GenerateCoverage throws on command failure (faithful), and the separate CoverageReportLocator returns null
// when no report was produced (the signal T14 acts on).
public sealed class CoverageRunner
{
    // Shared reciprocal contract with CoverageReportLocator (D-T12e): the writer (this runner) and the reader
    // (the locator) must agree byte-for-byte on the results-directory name, so they share this single source
    // of truth. Matches README "Coverage Pipeline" steps 1-2.
    public const string ResultsDirectoryName = "coverage";

    // Shared with the locator so only this tool's TRX deployment copies are excluded.
    public const string DiagnosticFilePrefix = "crap4csharp-native";

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
        => GenerateCoverage(testProject, coverageBaseDirectory, new CliArguments(CliMode.AllSrc, []));

    // Pure token construction shared by preview and execution. Every value remains one ArgumentList token.
    public static IReadOnlyList<string> BuildCommand(
        string testProject, CliArguments options, string resultsDirectory = ResultsDirectoryName)
    {
        ArgumentNullException.ThrowIfNull(testProject);
        ArgumentNullException.ThrowIfNull(options);
        List<string> command = ["dotnet", "test", testProject, "--collect:XPlat Code Coverage"];
        if (!options.AllTests)
        {
            command.Add("--filter");
            command.Add(options.TestFilter ?? UnitTestFilter);
        }

        if (options.Framework is not null)
        {
            command.Add("--framework");
            command.Add(options.Framework);
        }

        command.Add("--results-directory");
        command.Add(resultsDirectory);

        // Prefix mode gives each target framework/run its own file instead of overwriting diagnostics.
        command.Add("--logger");
        command.Add("trx;LogFilePrefix=" + DiagnosticFilePrefix);
        return command;
    }

    public void GenerateCoverage(string testProject, string coverageBaseDirectory, CliArguments options)
    {
        ArgumentNullException.ThrowIfNull(coverageBaseDirectory);
        GenerateCoverageRuns(PlanCoverage([testProject], coverageBaseDirectory, options), coverageBaseDirectory);
    }

    // Resolve/validate the entire batch before any deletion or execution. Preview and execution consume
    // the same immutable token vectors and ordinal-indexed directories, even for identical basenames.
    public static IReadOnlyList<CoverageRun> PlanCoverage(
        IReadOnlyList<string> testProjects, string invocationRoot, CliArguments options)
    {
        ArgumentNullException.ThrowIfNull(testProjects);
        ArgumentNullException.ThrowIfNull(invocationRoot);
        ArgumentNullException.ThrowIfNull(options);
        List<CoverageRun> runs = [];
        foreach (string project in testProjects.Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal))
        {
            ValidateVSTest(project, invocationRoot);
            string directory = testProjects.Count == 1 ? ResultsDirectoryName
                : ResultsDirectoryName + "/project-" + (runs.Count + 1).ToString("D4", CultureInfo.InvariantCulture);
            runs.Add(new(project, directory, BuildCommand(project, options, directory)));
        }

        return runs;
    }

    public void GenerateCoverageRuns(IReadOnlyList<CoverageRun> runs, string coverageBaseDirectory)
    {
        ArgumentNullException.ThrowIfNull(runs);
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
        foreach (CoverageRun run in runs)
        {
            int exit = _executor.Run(run.Command, coverageBaseDirectory);
            if (exit != 0)
            {
                throw new CoverageException(
                    "Coverage command failed with exit " + exit.ToString(CultureInfo.InvariantCulture));
            }

            ValidateDiagnostics(Path.Combine(coverageBaseDirectory, run.ResultsDirectory));
        }
    }

    // Read-only, bounded routing probe, NOT an MSBuild/SDK evaluator. See README for residual limits.
    public static void ValidateVSTest(string testProject, string invocationRoot)
    {
        ArgumentNullException.ThrowIfNull(testProject);
        ArgumentNullException.ThrowIfNull(invocationRoot);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(invocationRoot));
        string? global = FindAncestorFile(root, "global.json", null);
        if (global is not null)
        {
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(global),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            string? selectedRunner = null;
            if (document.RootElement.TryGetProperty("test", out JsonElement test)
                && test.TryGetProperty("runner", out JsonElement runner))
            {
                selectedRunner = runner.GetString();
            }

            // DOTNET_TEST_RUNNER only exists starting with SDK 11 preview 6. Honor it when an
            // explicit SDK pin establishes that boundary; do not guess the installed/rolled-forward SDK.
            if (document.RootElement.TryGetProperty("sdk", out JsonElement sdk)
                && sdk.TryGetProperty("version", out JsonElement version)
                && SupportsRunnerEnvironment(version.GetString()))
            {
                string? environmentRunner = Environment.GetEnvironmentVariable("DOTNET_TEST_RUNNER");
                if (string.Equals(environmentRunner, "Microsoft.Testing.Platform", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(environmentRunner, "VSTest", StringComparison.OrdinalIgnoreCase))
                {
                    selectedRunner = environmentRunner;
                }
            }

            if (string.Equals(selectedRunner, "Microsoft.Testing.Platform", StringComparison.OrdinalIgnoreCase))
            {
                throw new CoverageException($"Microsoft.Testing.Platform routing in '{global}' is unsupported; crap4csharp requires VSTest + coverlet.collector.");
            }
        }

        string project = Path.GetFullPath(Path.Combine(root, testProject));
        string directory = Path.GetDirectoryName(project)!;
        string? props = FindAncestorFile(directory, "Directory.Build.props", root);
        string? targets = FindAncestorFile(directory, "Directory.Build.targets", root);
        string? support = null;
        foreach (string? file in new[] { props, project, targets })
        {
            if (file is null || !File.Exists(file))
            {
                continue;
            }

            XDocument doc = XDocument.Load(file);
            foreach (XElement property in doc.Root?.Elements()
                .Where(e => e.Name.LocalName == "PropertyGroup" && e.Attribute("Condition") is null)
                .Elements()
                .Where(e => string.Equals(e.Name.LocalName, "TestingPlatformDotnetTestSupport", StringComparison.OrdinalIgnoreCase)
                    && e.Attribute("Condition") is null) ?? [])
            {
                support = property.Value.Trim();
            }
        }

        if (string.Equals(support, "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new CoverageException("TestingPlatformDotnetTestSupport=true is unsupported; crap4csharp requires VSTest + coverlet.collector.");
        }
    }

    private static void ValidateDiagnostics(string resultsDirectory)
    {
        try
        {
            string[] reports = Directory.Exists(resultsDirectory)
                ? Directory.GetFiles(resultsDirectory, DiagnosticFilePrefix + "*.trx")
                : [];
            if (reports.Length == 0)
            {
                throw new CoverageException("Coverage command produced no native TRX diagnostics.");
            }

            XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
            foreach (string report in reports)
            {
                XDocument document = XDocument.Load(report);
                XElement? summary = document.Root?.Element(ns + "ResultSummary");
                if (document.Root?.Name != ns + "TestRun" || summary is null)
                {
                    throw new CoverageException($"Invalid native TRX diagnostics in '{report}'.");
                }

                foreach (XElement info in summary.Element(ns + "RunInfos")?.Elements(ns + "RunInfo") ?? [])
                {
                    string message = info.Element(ns + "Text")?.Value ?? string.Empty;

                    // xUnit can return exit 0 and record this as a Warning, even for invalid syntax.
                    // Do not infer failure from test counts: a valid unmatched filter is still allowed.
                    if (string.Equals((string?)info.Attribute("outcome"), "Error", StringComparison.OrdinalIgnoreCase)
                        || message.Contains("Exception filtering tests", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new CoverageException("Coverage command reported a native test error: " + message);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            throw new CoverageException("Could not read native TRX diagnostics.", ex);
        }
    }

    private static bool SupportsRunnerEnvironment(string? sdkVersion)
    {
        if (sdkVersion is null)
        {
            return false;
        }

        string[] parts = sdkVersion.Split('-', 2);
        if (!Version.TryParse(parts[0], out Version? version) || version.Major < 11)
        {
            return false;
        }

        if (version.Major > 11 || parts.Length == 1)
        {
            return true;
        }

        string[] prerelease = parts[1].Split('.');
        return prerelease[0] == "rc"
            || (prerelease.Length > 1 && prerelease[0] == "preview"
                && int.TryParse(prerelease[1], NumberStyles.None, CultureInfo.InvariantCulture, out int preview)
                && preview >= 6);
    }

    private static string? FindAncestorFile(string directory, string name, string? boundary)
    {
        string? current = directory;
        while (current is not null)
        {
            string file = Path.Combine(current, name);
            if (File.Exists(file))
            {
                return file;
            }

            if (string.Equals(current, boundary, StringComparison.Ordinal))
            {
                break;
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }
}

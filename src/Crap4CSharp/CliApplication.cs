namespace Microsoft.Crap4CSharp;

using System.Globalization;

// Faithful port of crap4java's CliApplication, adapted per docs/decisions.md departures #1 (fail-fast),
// #7 (resolve-once), and #9/#10 (Model B resolution + unit-only target-test filter). Orchestrates: parse
// args -> pick files -> resolve the ONE owning project (bounded) -> fail-fast on absence/span -> resolve the
// test project -> fail-fast on absence -> run coverage ONCE -> locate ONCE -> fail-fast gate -> parse ->
// analyze -> format -> print -> threshold. Composes shipped types only (OwningProjectResolver,
// TestProjectResolver, CoverageRunner, CoverageReportLocator, CoberturaCoverageParser, CrapAnalyzer,
// ReportFormatter); contains no resolution/coverage/parse internals of its own.
public sealed class CliApplication
{
    // Adapts crap4java's Main.usage() to the C# ecosystem: "Java" -> "C#", ".java" -> ".cs", and the
    // executable name to the crap4csharp product. Only ParseArguments consumes it (help + parse-error paths).
    private const string Usage = """
        Usage:
          crap4csharp            Analyze all C# files under src/
          crap4csharp --changed  Analyze changed C# files under src/
          crap4csharp <path...>  Analyze files, or for directory args analyze <dir>/src/**/*.cs
          crap4csharp --help     Print this help message
        """;

    private readonly string _projectRoot;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly CoverageRunner _coverageRunner;

    public CliApplication(
        string projectRoot,
        TextWriter output,
        TextWriter error,
        CoverageRunner coverageRunner)
    {
        ArgumentNullException.ThrowIfNull(projectRoot);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(coverageRunner);
        _projectRoot = projectRoot;
        _output = output;
        _error = error;
        _coverageRunner = coverageRunner;
    }

    // Ports execute(String[]). Owns every exit code (§4): 0 (help / no files / max CRAP <= 8.0),
    // 1 (parse error / no owning project / files span multiple projects / no test project / no report /
    // multiple reports / empty report), 2 (threshold exceeded). The coverage-runner throw and any parser throw
    // PROPAGATE (faithful to Java's `throws Exception`); T15's Program.Main converts them to exit 1.
    public int Execute(string[] args)
    {
        ParseOutcome parse = ParseArguments(args);
        if (parse.ExitCode >= 0)
        {
            return parse.ExitCode;
        }

        CliArguments parsed = parse.Arguments!;
        IReadOnlyList<string> filesToAnalyze = FilesForMode(parsed);
        if (filesToAnalyze.Count == 0)
        {
            _output.WriteLine("No C# files to analyze.");
            return 0;
        }

        // Resolve the ONE owning project for the analyzed files (departure #7 resolve-once; #9 Model B),
        // bounded so the walk never climbs above the invocation root _projectRoot.
        IReadOnlyList<string> owningProjects =
            OwningProjectResolver.ResolveOwningProjects(filesToAnalyze, _projectRoot);
        if (owningProjects.Count == 0)
        {
            _error.WriteLine($"No owning .csproj was found at or above the analyzed C# files (searched up to the invocation root '{_projectRoot}'). Ensure the target sources live inside a C# project.");
            return 1;
        }

        if (owningProjects.Count > 1)
        {
            _error.WriteLine($"Analyzed C# files span multiple projects ({string.Join(", ", owningProjects)}); crap4csharp resolves a single owning project per run.");
            return 1;
        }

        string owningProject = owningProjects[0];
        string? testProject = TestProjectResolver.ResolveTestProject(owningProject, _projectRoot);
        if (testProject is null)
        {
            string project = Path.GetFileNameWithoutExtension(owningProject);
            _error.WriteLine($"No test project was found for '{owningProject}' (expected {project}.Tests.csproj or {project}.UnitTests.csproj transitively referencing it, under '{_projectRoot}').");
            return 1;
        }

        _coverageRunner.GenerateCoverage(testProject, _projectRoot);

        // CoverageRunner deletes coverage/ before the run, so every report here is from THIS run: exactly
        // one for a single-TFM test project, one-per-TFM for a multi-targeted one.
        IReadOnlyList<string> reports = CoverageReportLocator.LocateAll(_projectRoot);

        // FAIL-FAST TRIGGER 1 (departure #1): no report produced at all.
        if (reports.Count == 0)
        {
            _error.WriteLine($"No coverage report was produced under '{_projectRoot}'. Ensure the test project references coverlet.collector so 'dotnet test --collect' emits coverage.cobertura.xml.");
            return 1;
        }

        // FAIL-FAST TRIGGER (T22, finding #4): >1 report ⇒ a multi-targeted (<TargetFrameworks>) test
        // project emits one coverage.cobertura.xml per TFM; a nondeterministic single-pick flips the exit
        // code across runs on unchanged code, so refuse deterministically (same ethos as the
        // span-multiple-projects fail-fast, #9).
        if (reports.Count > 1)
        {
            _error.WriteLine($"Found multiple coverage reports under '{_projectRoot}' ({string.Join(", ", reports)}); a multi-targeted test project (<TargetFrameworks>) emits one coverage.cobertura.xml per target framework, which crap4csharp does not support. Use a single <TargetFramework> for the test project.");
            return 1;
        }

        string report = reports[0];

        // FAIL-FAST TRIGGER 2 (departure #1): a report was produced but carries no coverage data. Inspect
        // the parsed map's emptiness -- NOT the metrics: a populated-but-non-matching report ALSO yields
        // all-N/A metrics yet must not fail-fast (that per-method N/A path lives in CrapAnalyzer, unchanged).
        IReadOnlyDictionary<string, CoverageData> coverageMap = CoberturaCoverageParser.Parse(report);
        if (coverageMap.Count == 0)
        {
            _error.WriteLine($"Coverage report '{report}' contained no coverage data. Failing fast.");
            return 1;
        }

        // CrapAnalyzer re-parses `report` (locked double-parse, option A): negligible cost, zero API change.
        IReadOnlyList<MethodMetrics> metrics = CrapAnalyzer.Analyze(filesToAnalyze, report);

        // The report already ends with '\n' and is already sorted by both CrapAnalyzer and ReportFormatter,
        // so the Java pre-format sort is redundant and omitted (behavior-preserving; MaxCrap is order-free).
        _output.Write(ReportFormatter.Format(metrics));

        double max = MaxCrap(metrics);
        if (ThresholdExceeded(max))
        {
            _error.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "CRAP threshold exceeded: {0:F1} > 8.0",
                max));
            return 2;
        }

        return 0;
    }

    // Ports thresholdExceeded: strict '>' against 8.0 via CompareTo (Java's Double.compare analog).
    public static bool ThresholdExceeded(double max) => max.CompareTo(8.0) > 0;

    // Ports Main.maxCrap: the largest non-null CrapScore, or 0.0 when every method is N/A.
    public static double MaxCrap(IReadOnlyList<MethodMetrics> metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        double max = 0.0;
        foreach (MethodMetrics metric in metrics)
        {
            if (metric.CrapScore is not null)
            {
                max = Math.Max(max, metric.CrapScore.Value);
            }
        }

        return max;
    }

    // Ports parseArguments: parse; on --help print usage to stdout and exit 0; on ArgumentException (W6:
    // the broad type, catching the ArgumentNullException null-args case too) print the message to stderr,
    // usage to stdout, and exit 1; otherwise proceed (ExitCode -1).
    private ParseOutcome ParseArguments(string[] args)
    {
        try
        {
            CliArguments parsed = CliArgumentsParser.Parse(args);
            if (parsed.Mode == CliMode.Help)
            {
                _output.WriteLine(Usage);
                return ParseOutcome.Exit(0);
            }

            return ParseOutcome.Ok(parsed);
        }
        catch (ArgumentException ex)
        {
            _error.WriteLine(ex.Message);
            _output.WriteLine(Usage);
            return ParseOutcome.Exit(1);
        }
    }

    // Ports filesForMode: a switch expression over all four CliMode arms plus a default throw (W7). The
    // Help arm is dead (ParseArguments short-circuits it), retained for exhaustiveness.
    private IReadOnlyList<string> FilesForMode(CliArguments parsed)
    {
        return parsed.Mode switch
        {
            CliMode.AllSrc => SourceFileFinder.FindAllCSharpFilesUnderSrc(_projectRoot),
            CliMode.ChangedSrc => ChangedFileDetector.ChangedCSharpFilesUnderSrc(_projectRoot),
            CliMode.ExplicitFiles => ExplicitFiles(parsed.FileArgs),
            CliMode.Help => [],
            _ => throw new InvalidOperationException($"Unhandled CLI mode: {parsed.Mode}"),
        };
    }

    // Ports explicitFiles: resolve each arg against projectRoot; a directory arg expands via SourceFileFinder
    // (routing through the finder keeps the bin/obj exclusion, W12), a file arg is taken verbatim.
    // De-duplicate and sort with StringComparer.Ordinal (departure #3; replaces Java's LinkedHashSet +
    // naturalOrder), matching SourceFileFinder/ChangedFileDetector.
    private IReadOnlyList<string> ExplicitFiles(IReadOnlyList<string> fileArgs)
    {
        HashSet<string> files = new(StringComparer.Ordinal);
        foreach (string arg in fileArgs)
        {
            string path = Path.GetFullPath(Path.Combine(_projectRoot, arg));
            if (Directory.Exists(path))
            {
                foreach (string found in SourceFileFinder.FindAllCSharpFilesUnderSrc(path))
                {
                    files.Add(found);
                }
            }
            else
            {
                files.Add(path);
            }
        }

        return [.. files.OrderBy(path => path, StringComparer.Ordinal)];
    }

    // Ports Java's ParseOutcome: Ok(arguments) proceeds (ExitCode -1); Exit(code) short-circuits with an
    // exit code. Arguments is non-null exactly when ExitCode < 0.
    private sealed class ParseOutcome
    {
        private ParseOutcome(CliArguments? arguments, int exitCode)
        {
            Arguments = arguments;
            ExitCode = exitCode;
        }

        public CliArguments? Arguments { get; }

        public int ExitCode { get; }

        public static ParseOutcome Ok(CliArguments arguments) => new(arguments, -1);

        public static ParseOutcome Exit(int code) => new(null, code);
    }
}

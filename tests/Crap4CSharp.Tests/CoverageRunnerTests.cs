namespace Microsoft.Crap4CSharp.Tests;

// Ports crap4java's CoverageRunnerTest, updated for the T17 (Model B) two-argument
// GenerateCoverage(testProject, coverageBaseDirectory) signature and the unit-only --filter token pair
// (departure #10). Java's two tests -- deletesStaleCoverageAndRunsMavenCoverageCommand and
// failsWhenCoverageCommandFails -- are ported (tests 1-2) with the ecosystem-adapter adaptation recorded in
// docs/decisions.md: the Maven+JaCoCo command becomes the locked `dotnet test <TestProject> --collect ...
// --filter "type!=IntegrationTests" --results-directory coverage` token list and the two JaCoCo artifacts
// collapse to the single "coverage" results dir, so the deletion assertion is one directory. The added tests
// pin the unit-only filter token adjacency and the CA1062 guards. No real `dotnet test` is ever launched --
// the process is faked via RecordingExecutor -- so the suite stays fast and deterministic. Each test uses a
// fresh temp tree removed in a finally.
public class CoverageRunnerTests
{
    private const string TestProject = "tests/Foo.Tests/Foo.Tests.csproj";

    [Fact]
    public void DeletesStaleCoverageAndRunsCoverageCommand()
    {
        WithTempRoot(root =>
        {
            // Pre-create a stale coverage/<guid>/coverage.cobertura.xml plus a stray coverage/old.txt so the
            // whole results dir must be deleted (analog of Java's stale target/site/jacoco + target/jacoco.exec).
            string staleReport = Path.Combine(root, "coverage", "guid", "coverage.cobertura.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(staleReport)!);
            File.WriteAllText(staleReport, "stale");
            File.WriteAllText(Path.Combine(root, "coverage", "old.txt"), "stale");

            RecordingExecutor executor = new(0);
            CoverageRunner runner = new(executor);

            runner.GenerateCoverage(TestProject, root);

            // (a) the stale results dir is gone.
            File.Exists(staleReport).Should().BeFalse();
            File.Exists(Path.Combine(root, "coverage", "old.txt")).Should().BeFalse();

            // (b) the exact locked token list was run (Model B target token + unit-only --filter pair).
            executor.Commands[0].Should().Equal(
                "dotnet", "test", TestProject, "--collect:XPlat Code Coverage", "--filter", "type!=IntegrationTests", "--results-directory", "coverage", "--logger", "trx;LogFilePrefix=crap4csharp-native");

            // (c) the working directory is the verbatim coverage base -- NOT Path.GetFullPath (no normalization).
            executor.Directories[0].Should().Be(root);
        });
    }

    [Fact]
    public void FailsWhenCoverageCommandFails()
    {
        WithTempRoot(root =>
        {
            RecordingExecutor executor = new(2);
            CoverageRunner runner = new(executor);

            Action act = () => runner.GenerateCoverage(TestProject, root);

            act.Should().Throw<CoverageException>()
                .WithMessage("Coverage command failed with exit 2");
        });
    }

    [Fact]
    public void RunsCoverageCommandWhenNoStaleArtifacts()
    {
        WithTempRoot(root =>
        {
            // No coverage/ dir exists: the delete branch no-ops and the command still runs.
            RecordingExecutor executor = new(0);
            CoverageRunner runner = new(executor);

            runner.GenerateCoverage(TestProject, root);

            executor.Commands[0].Should().Equal(
                "dotnet", "test", TestProject, "--collect:XPlat Code Coverage", "--filter", "type!=IntegrationTests", "--results-directory", "coverage", "--logger", "trx;LogFilePrefix=crap4csharp-native");
            executor.Directories[0].Should().Be(root);
        });
    }

    [Fact]
    public void PassesUnitOnlyTraitFilterToExecutor()
    {
        // Focused pin: the exact --filter + exclusion-form value pair, in order, reaches the executor -- the
        // untagged-inclusion invariant (departure #10) at the command level, without a real run.
        WithTempRoot(root =>
        {
            RecordingExecutor executor = new(0);
            CoverageRunner runner = new(executor);

            runner.GenerateCoverage(TestProject, root);

            executor.Commands[0].Should().ContainInOrder("--filter", "type!=IntegrationTests");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildsAndExecutesSameExactTokensForCustomFilterOrAllTests(bool allTests)
    {
        WithTempRoot(root =>
        {
            const string filter = "FullyQualifiedName~A B|type=IntegrationTests";
            CliArguments options = new(CliMode.AllSrc, [])
            {
                TestFilter = allTests ? null : filter,
                AllTests = allTests,
                Framework = "net8.0",
            };
            RecordingExecutor executor = new(0);
            new CoverageRunner(executor).GenerateCoverage(TestProject, root, options);
            var command = CoverageRunner.BuildCommand(TestProject, options);
            executor.Commands.Should().ContainSingle().Which.Should().Equal(command);
            command.Should().ContainInOrder("--framework", "net8.0");
            if (allTests)
            {
                command.Should().NotContain("--filter");
            }
            else
            {
                command.Should().ContainInOrder("--filter", filter);
                command.Should().NotContain("type!=IntegrationTests");
            }
        });
    }

    [Fact]
    public void NativeFilterFailureIsNotRetriedUnfiltered()
    {
        WithTempRoot(root =>
        {
            RecordingExecutor executor = new(1);
            CliArguments options = new(CliMode.AllSrc, []) { TestFilter = "(" };
            FluentActions.Invoking(() => new CoverageRunner(executor).GenerateCoverage(TestProject, root, options))
                .Should().Throw<CoverageException>().WithMessage("*exit 1");
            executor.Commands.Should().ContainSingle().Which.Should().ContainInOrder("--filter", "(");
        });
    }

    [Theory]
    [InlineData("Warning", "Exception filtering tests: Incorrect format for TestCaseFilter")]
    [InlineData("Error", "Native adapter failed")]
    public void NativeRunInfoFailureOverridesSuccessfulProcessExit(string outcome, string message)
    {
        WithTempRoot(root =>
        {
            string diagnostics = $"""
                <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                  <ResultSummary><RunInfos><RunInfo outcome="{outcome}"><Text>{message}</Text></RunInfo></RunInfos></ResultSummary>
                </TestRun>
                """;
            RecordingExecutor executor = new(0, diagnostics);
            FluentActions.Invoking(() => new CoverageRunner(executor).GenerateCoverage(TestProject, root))
                .Should().Throw<CoverageException>().WithMessage("*" + message);
            executor.Commands.Should().ContainSingle();
        });
    }

    [Fact]
    public void ZeroTestsAndUnrelatedWarningsDoNotFailDiagnostics()
    {
        WithTempRoot(root =>
        {
            const string diagnostics = """
                <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                  <ResultSummary outcome="Completed">
                    <Counters total="0" executed="0" />
                    <RunInfos><RunInfo outcome="Warning"><Text>No test matches the given testcase filter.</Text></RunInfo></RunInfos>
                  </ResultSummary>
                </TestRun>
                """;
            FluentActions.Invoking(() => new CoverageRunner(new RecordingExecutor(0, diagnostics))
                .GenerateCoverage(TestProject, root)).Should().NotThrow();
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("<TestRun")]
    [InlineData("<TestRun />")]
    public void MissingOrInvalidNativeDiagnosticsFailExplicitly(string? diagnostics)
    {
        WithTempRoot(root =>
        {
            FluentActions.Invoking(() => new CoverageRunner(new RecordingExecutor(0, diagnostics))
                .GenerateCoverage(TestProject, root)).Should().Throw<CoverageException>();
        });
    }

    [Theory]
    [InlineData("global.json", """{"test":{"runner":"Microsoft.Testing.Platform"}}""")]
    [InlineData("tests/Custom.csproj", "<Project><PropertyGroup><TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport></PropertyGroup></Project>")]
    [InlineData("Directory.Build.props", "<Project><PropertyGroup><TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport></PropertyGroup></Project>")]
    [InlineData("tests/Directory.Build.targets", "<Project><PropertyGroup><TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport></PropertyGroup></Project>")]
    public void KnownMtpRoutingFailsBeforeDeletingCoverage(string config, string contents)
    {
        WithTempRoot(root =>
        {
            string sentinel = Path.Combine(root, "coverage", "sentinel");
            Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
            File.WriteAllText(sentinel, "keep");
            string path = Path.Combine(root, config);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
            RecordingExecutor executor = new(0);
            FluentActions.Invoking(() =>
                new CoverageRunner(executor).GenerateCoverage("tests/Custom.csproj", root))
                .Should().Throw<CoverageException>().WithMessage("*VSTest*");
            File.ReadAllText(sentinel).Should().Be("keep");
            executor.Commands.Should().BeEmpty();
        });
    }

    [Fact]
    public void MtpPackageAloneDoesNotImplyRoutingAndLiteralFalseOverridesProps()
    {
        WithTempRoot(root =>
        {
            File.WriteAllText(
                Path.Combine(root, "Directory.Build.props"),
                "<Project><PropertyGroup><TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport></PropertyGroup></Project>");
            string project = Path.Combine(root, "Custom.csproj");
            const string projectContents = """
                <Project>
                  <PropertyGroup><TestingPlatformDotnetTestSupport>false</TestingPlatformDotnetTestSupport></PropertyGroup>
                  <ItemGroup><PackageReference Include="Microsoft.Testing.Platform" Version="1.9.0" /></ItemGroup>
                </Project>
                """;
            File.WriteAllText(project, projectContents);
            RecordingExecutor executor = new(0);
            new CoverageRunner(executor).GenerateCoverage(project, root);
            executor.Commands.Should().ContainSingle();
        });
    }

    [Fact]
    public void ReadsNearestAncestorGlobalJsonOutsideInvocationRootWithoutModifyingIt()
    {
        WithTempRoot(parent =>
        {
            string global = Path.Combine(parent, "global.json");
            const string contents = """{"test":{"runner":"Microsoft.Testing.Platform"}}""";
            File.WriteAllText(global, contents);
            string root = Path.Combine(parent, "repo");
            Directory.CreateDirectory(root);
            FluentActions.Invoking(() => CoverageRunner.ValidateVSTest("Custom.csproj", root))
                .Should().Throw<CoverageException>();
            File.ReadAllText(global).Should().Be(contents);

            // A nearer config governs even if it doesn't contain a test section.
            File.WriteAllText(Path.Combine(root, "global.json"), "{}");
            FluentActions.Invoking(() => CoverageRunner.ValidateVSTest("Custom.csproj", root))
                .Should().NotThrow();
        });
    }

    [Fact]
    public void ThrowsOnNullExecutor()
    {
        Action act = () => _ = new CoverageRunner(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("executor");
    }

    [Fact]
    public void ThrowsOnNullTestProject()
    {
        RecordingExecutor executor = new(0);
        CoverageRunner runner = new(executor);

        Action act = () => runner.GenerateCoverage(null!, "root");

        act.Should().Throw<ArgumentNullException>().WithParameterName("testProject");
    }

    [Fact]
    public void ThrowsOnNullCoverageBaseDirectory()
    {
        RecordingExecutor executor = new(0);
        CoverageRunner runner = new(executor);

        Action act = () => runner.GenerateCoverage(TestProject, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("coverageBaseDirectory");
    }

    private static void WithTempRoot(Action<string> test)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            test(root);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    // 1:1 with Java's RecordingExecutor: records each command and workingDirectory and returns a fixed exit
    // code. No real process is ever launched.
    private sealed class RecordingExecutor : ICommandExecutor
    {
        private readonly int _exitCode;

        private readonly string? _diagnostics;

        public RecordingExecutor(int exitCode, string? diagnostics = """<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary /></TestRun>""")
        {
            _exitCode = exitCode;
            _diagnostics = diagnostics;
        }

        public List<IReadOnlyList<string>> Commands { get; } = [];

        public List<string> Directories { get; } = [];

        public int Run(IReadOnlyList<string> command, string workingDirectory)
        {
            Commands.Add(command);
            Directories.Add(workingDirectory);
            if (_diagnostics is not null)
            {
                string directory = Path.Combine(workingDirectory, "coverage");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "crap4csharp-native_net8.0.trx"), _diagnostics);
            }

            return _exitCode;
        }
    }
}

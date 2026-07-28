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
            Directory.Exists(Path.Combine(root, "coverage")).Should().BeFalse();

            // (b) the exact locked token list was run (Model B target token + unit-only --filter pair).
            executor.Commands[0].Should().Equal(
                "dotnet", "test", TestProject, "--collect:XPlat Code Coverage", "--filter", "type!=IntegrationTests", "--results-directory", "coverage");

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
                "dotnet", "test", TestProject, "--collect:XPlat Code Coverage", "--filter", "type!=IntegrationTests", "--results-directory", "coverage");
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

        public RecordingExecutor(int exitCode)
        {
            _exitCode = exitCode;
        }

        public List<IReadOnlyList<string>> Commands { get; } = [];

        public List<string> Directories { get; } = [];

        public int Run(IReadOnlyList<string> command, string workingDirectory)
        {
            Commands.Add(command);
            Directories.Add(workingDirectory);
            return _exitCode;
        }
    }
}

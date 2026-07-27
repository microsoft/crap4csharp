namespace Microsoft.Crap4CSharp.Tests;

// Ports crap4java's CoverageRunnerTest. Java's two tests -- deletesStaleCoverageAndRunsMavenCoverageCommand
// and failsWhenCoverageCommandFails -- are both ported (tests 1-2) with the ecosystem-adapter adaptation
// recorded in docs/decisions.md: the Maven+JaCoCo command becomes the locked `dotnet test` token list and
// the two JaCoCo artifacts collapse to the single "coverage" results dir, so the deletion assertion is one
// directory rather than a dir + a file. Tests 3-5 are C#-specific guard/CA1062 additions. No real
// `dotnet test` is ever launched -- the process is faked via RecordingExecutor (1:1 with Java's fake), so
// the suite stays fast and deterministic. Each test uses a fresh temp tree removed in a finally.
public class CoverageRunnerTests
{
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

            runner.GenerateCoverage(root);

            // (a) the stale results dir is gone.
            Directory.Exists(Path.Combine(root, "coverage")).Should().BeFalse();

            // (b) the exact locked token list was run.
            executor.Commands[0].Should().Equal(
                "dotnet", "test", "--collect:XPlat Code Coverage", "--results-directory", "coverage");

            // (c) the working directory is the verbatim root -- NOT Path.GetFullPath(root) (no normalization).
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

            Action act = () => runner.GenerateCoverage(root);

            act.Should().Throw<InvalidOperationException>()
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

            runner.GenerateCoverage(root);

            executor.Commands[0].Should().Equal(
                "dotnet", "test", "--collect:XPlat Code Coverage", "--results-directory", "coverage");
            executor.Directories[0].Should().Be(root);
        });
    }

    [Fact]
    public void ThrowsOnNullExecutor()
    {
        Action act = () => _ = new CoverageRunner(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("executor");
    }

    [Fact]
    public void ThrowsOnNullProjectRoot()
    {
        RecordingExecutor executor = new(0);
        CoverageRunner runner = new(executor);

        Action act = () => runner.GenerateCoverage(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("projectRoot");
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

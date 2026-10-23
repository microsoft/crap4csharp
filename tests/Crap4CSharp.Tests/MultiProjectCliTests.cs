namespace Microsoft.Crap4CSharp.Tests;

using System.Text.Json;

public class MultiProjectCliTests
{
    [Theory]
    [InlineData("default")]
    [InlineData("custom")]
    [InlineData("all")]
    public void DryRunMatchesSequentialExecutionWithUniqueDirectoriesAndOneUnionReport(string filter)
    {
        WithFixture(root =>
        {
            string sentinel = Write(root, "coverage/stale/coverage.cobertura.xml", "not xml");
            Write(root, "coverage/crap4csharp-native_stale.trx", "not xml");
            using StringWriter output = new();
            using StringWriter error = new();
            BatchExecutor executor = new("valid");
            CliApplication app = new(root, output, error, new CoverageRunner(executor));
            List<string> args = ["src/C.cs", "--test-project", "tests/**/*.csproj",
                "--test-project", "tests/B/Same.csproj", "--framework", "net8.0", "--crap-threshold", "2"];
            if (filter == "all")
            {
                args.Add("--all-tests");
            }
            else if (filter == "custom")
            {
                args.AddRange(["--test-filter", "Name~with spaces"]);
            }

            app.Execute([.. args, "--dry-run"]).Should().Be(0);
            executor.Commands.Should().BeEmpty();
            File.ReadAllText(sentinel).Should().Be("not xml");
            Directory.Exists(Path.Combine(root, "coverage/project-0001")).Should().BeFalse();
            string preview = output.ToString();
            preview.Should().Contain("Selected files:").And.Contain("Owning project:")
                .And.Contain("Results directory: coverage/project-0001")
                .And.Contain("Results directory: coverage/project-0002");
            string[][] commands = [.. preview.Split('\n')
                .Where(line => line.StartsWith("Command: ", StringComparison.Ordinal))
                .Select(line => JsonSerializer.Deserialize<string[]>(line[9..])!)];
            commands.Should().HaveCount(2);
            output.GetStringBuilder().Clear();

            app.Execute([.. args]).Should().Be(0);
            executor.Commands.Should().HaveCount(2);
            executor.Commands[0].Should().Equal(commands[0]);
            executor.Commands[1].Should().Equal(commands[1]);
            executor.Commands.Select(command => command[2]).Should().BeInAscendingOrder(StringComparer.Ordinal);
            File.Exists(sentinel).Should().BeFalse();
            executor.FirstArtifactsSurvived.Should().BeTrue("coverage must be cleared only once");
            error.ToString().Should().BeEmpty();
            output.ToString().Split("CRAP Report", StringSplitOptions.None).Should().HaveCount(2);
            string[] directories = ["coverage/project-0001", "coverage/project-0002"];
            var paths = directories
                .SelectMany(directory => CoverageReportLocator.LocateInResultsDirectory(Path.Combine(root, directory))).ToArray();
            paths.Should().HaveCount(2, "TRX deployment copies are scoped to each project");
            var map = CoberturaCoverageParser.ParseReports(paths);
            map["C#M#C.cs:10"].CoveragePercent.Should().Be(100);
            CrapAnalyzer.AnalyzeCoverage([Path.Combine(root, "src/C.cs")], map)
                .Should().ContainSingle().Which.CrapScore.Should().Be(2);
        });
    }

    [Theory]
    [InlineData("exit")]
    [InlineData("missing-trx")]
    [InlineData("malformed-trx")]
    [InlineData("native-error")]
    [InlineData("filter-warning")]
    [InlineData("missing-report")]
    [InlineData("empty-report")]
    [InlineData("malformed-report")]
    [InlineData("multiple-reports")]
    public void SecondProjectFailureNeverProducesPartialNormalReport(string failure)
    {
        WithFixture(root =>
        {
            using StringWriter output = new();
            using StringWriter error = new();
            BatchExecutor executor = new(failure);
            CliApplication app = new(root, output, error, new CoverageRunner(executor));
            Func<int> execute = () => app.Execute(["src/C.cs", "--test-project", "tests/**/*.csproj"]);
            if (failure is "missing-report" or "empty-report" or "multiple-reports")
            {
                execute().Should().Be(1);
            }
            else if (failure == "malformed-report")
            {
                FluentActions.Invoking(() => execute()).Should().Throw<InvalidOperationException>();
            }
            else
            {
                FluentActions.Invoking(() => execute()).Should().Throw<CoverageException>();
            }

            executor.Commands.Should().HaveCount(2);
            output.ToString().Should().NotContain("CRAP Report");
            error.ToString().Should().NotContain("CRAP threshold exceeded");
        });
    }

    [Theory]
    [InlineData("missing", false)]
    [InlineData("missing", true)]
    [InlineData("nonreferencing", false)]
    [InlineData("nonreferencing", true)]
    [InlineData("mtp", false)]
    [InlineData("mtp", true)]
    public void InvalidLastProjectPreflightsEntireSelectionBeforeAnyMutation(string failure, bool dryRun)
    {
        WithFixture(root =>
        {
            if (failure == "nonreferencing")
            {
                Write(root, "tests/B/Same.csproj", "<Project/>");
            }
            else if (failure == "mtp")
            {
                Write(
                    root,
                    "tests/B/Directory.Build.props",
                    "<Project><PropertyGroup><TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport></PropertyGroup></Project>");
            }

            string sentinel = Write(root, "coverage/sentinel", "keep");
            using StringWriter output = new();
            using StringWriter error = new();
            BatchExecutor executor = new("valid");
            CliApplication app = new(root, output, error, new CoverageRunner(executor));
            List<string> args = ["src/C.cs", "--test-project", "tests/A/Same.csproj",
                "--test-project", failure == "missing" ? "tests/missing.csproj" : "tests/B/Same.csproj"];
            if (dryRun)
            {
                args.Add("--dry-run");
            }

            app.Execute([.. args]).Should().Be(1);
            executor.Commands.Should().BeEmpty();
            File.ReadAllText(sentinel).Should().Be("keep");
            output.ToString().Should().NotContain("CRAP Report").And.NotContain("Command:");
        });
    }

    private static void WithFixture(Action<string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Write(root, "src/C.csproj", "<Project/>");
            Write(root, "src/C.cs", "class C { int M(bool x) { if (x) return 1; return 0; } }");
            const string project = "<Project><ItemGroup><ProjectReference Include='../../src/C.csproj'/></ItemGroup></Project>";
            Write(root, "tests/A/Same.csproj", project);
            Write(root, "tests/B/Same.csproj", project);
            action(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Write(string root, string relative, string content)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class BatchExecutor(string failure) : ICommandExecutor
    {
        public List<IReadOnlyList<string>> Commands { get; } = [];

        public bool FirstArtifactsSurvived { get; private set; }

        public int Run(IReadOnlyList<string> command, string workingDirectory)
        {
            Commands.Add(command);
            bool second = Commands.Count == 2;
            if (second)
            {
                FirstArtifactsSurvived = File.Exists(Path.Combine(workingDirectory, "coverage/project-0001/canonical/coverage.cobertura.xml"));
                if (failure == "exit")
                {
                    return 1;
                }
            }

            string directory = Path.Combine(workingDirectory, command[command.ToList().IndexOf("--results-directory") + 1]);
            string deployment = second ? "suite-two" : "suite-one";
            string runInfo = second ? failure switch
            {
                "native-error" => "<RunInfos><RunInfo outcome='Error'><Text>native error</Text></RunInfo></RunInfos>",
                "filter-warning" => "<RunInfos><RunInfo outcome='Warning'><Text>Exception filtering tests</Text></RunInfo></RunInfos>",
                _ => string.Empty,
            } : string.Empty;
            string trx = $"""
                <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                  <TestSettings><Deployment runDeploymentRoot="{deployment}" /></TestSettings>
                  <ResultSummary>{runInfo}</ResultSummary>
                </TestRun>
                """;
            if (!second || failure != "missing-trx")
            {
                Write(directory, "crap4csharp-native.trx", second && failure == "malformed-trx" ? "not xml" : trx);
            }

            if (second && failure == "missing-report")
            {
                return 0;
            }

            string xml = $"""
                <coverage><class name="C" filename="C.cs"><methods><method name="M" signature="(System.Boolean)">
                  <lines><line number="10" hits="{(second ? 0 : 1)}"/><line number="20" hits="{(second ? 1 : 0)}"/></lines>
                </method></methods></class></coverage>
                """;
            if (second && failure == "empty-report")
            {
                xml = "<coverage/>";
            }
            else if (second && failure == "malformed-report")
            {
                xml = "not xml";
            }

            Write(directory, (second ? "suite-one" : "canonical") + "/coverage.cobertura.xml", xml);
            Write(directory, deployment + "/copy/coverage.cobertura.xml", xml);
            if (second && failure == "multiple-reports")
            {
                Write(directory, "another-tfm/coverage.cobertura.xml", xml);
            }

            return 0;
        }
    }
}

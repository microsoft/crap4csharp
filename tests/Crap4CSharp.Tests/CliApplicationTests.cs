namespace Microsoft.Crap4CSharp.Tests;

// Ports crap4java's CliApplicationTest + MainTest into the T14 composition layer, adapted for the two
// ratified departures that reshape these tests: #1 (fail-fast) and #7 (resolve-once). CliApplication is
// exercised directly (no Program/T15), injecting a fake ICommandExecutor via CoverageRunner -- exactly the
// Java `CoverageRunner((command, directory) -> ...)` seam -- so no real `dotnet test` is ever launched.
// Java dispositions (see the T14 contract §6): parseErrors/noFiles/threshold/help/maxCrap ports; the
// coverage-path tests (doesNotWarnWhenJacocoXmlExists, explicitFileArgsAreAnalyzed,
// directoryArg..., and the replaced explicitFileUsesOwningModule...) now require a WRITTEN report to reach
// exit 0 because the Java "warn + N/A + exit 0" no-report branch is now fail-fast; two dedicated fail-fast
// tests (#10, #11) plus C#-specific exit-2 (#12) and trigger-3 propagation (#13) tests are added. Fixtures
// are modeled on CrapAnalyzerTests.Alpha75Xml; temp trees are always removed in a finally; test names are
// PascalCase (C2/CA1707) while fixture member names inside string literals keep crap4java's lowercase.
public class CliApplicationTests
{
    private const string AlphaSampleSource = """
        namespace demo;
        class Sample
        {
            int alpha(bool a)
            {
                if (a)
                {
                    return 1;
                }
                return 0;
            }
        }
        """;

    // alpha declared at line 4; covered lines 6,7,8 (hits=1) + missed line 10 (hits=0) -> 75%, resolved
    // from alpha's StartLine 4 by nearest line to key demo.Sample#alpha:6 -> CRAP 2.0625 (<= 8.0, exit 0).
    private const string Alpha75Xml = """
        <?xml version="1.0"?>
        <coverage>
          <packages>
            <package name="demo">
              <classes>
                <class name="demo.Sample" filename="Sample.cs">
                  <methods>
                    <method name="alpha" signature="(System.Boolean)">
                      <lines>
                        <line number="6" hits="1" />
                        <line number="7" hits="1" />
                        <line number="8" hits="1" />
                        <line number="10" hits="0" />
                      </lines>
                    </method>
                  </methods>
                </class>
              </classes>
            </package>
          </packages>
        </coverage>
        """;

    // 1 <- CliApplicationTest.parseErrorsReturnUsageAndExitOne. --changed combined with a file arg throws
    // ArgumentException (W6), caught -> message to stderr, usage to stdout, exit 1.
    [Fact]
    public void ParseErrorsReturnUsageAndExitOne()
    {
        WithTempRoot(root =>
        {
            using StringWriter output = new();
            using StringWriter error = new();
            CliApplication app = new(root, output, error, new CoverageRunner(new FakeExecutor(0, null)));

            int exit = app.Execute(["--changed", "src/Sample.cs"]);

            exit.Should().Be(1);
            output.ToString().Should().Contain("Usage:");
            error.ToString().Should().Contain("--changed cannot be combined with file arguments");
        });
    }

    // 2 <- CliApplicationTest.returnsZeroWhenNoFilesAreFound (message adapted to C#). No src/ -> no files ->
    // exit 0 BEFORE any coverage run.
    [Fact]
    public void ReturnsZeroWhenNoFilesAreFound()
    {
        WithTempRoot(root =>
        {
            using StringWriter output = new();
            using StringWriter error = new();
            FakeExecutor fake = new(0, null);
            CliApplication app = new(root, output, error, new CoverageRunner(fake));

            int exit = app.Execute([]);

            exit.Should().Be(0);
            output.ToString().Should().Contain("No C# files to analyze.");
            fake.Directories.Should().BeEmpty();
        });
    }

    // 3 <- CliApplicationTest.doesNotWarnWhenJacocoXmlExists (adapted). A written cobertura report reaches
    // exit 0; stdout has the class, stderr has NEITHER fail-fast anchor.
    [Fact]
    public void AnalyzesAndReportsWhenCoverageExists()
    {
        WithTempRoot(root =>
        {
            WriteFile(root, "Sample.cs", AlphaSampleSource);
            using StringWriter output = new();
            using StringWriter error = new();
            CliApplication app = new(root, output, error, new CoverageRunner(new FakeExecutor(0, Alpha75Xml)));

            int exit = app.Execute(["Sample.cs"]);

            exit.Should().Be(0);
            output.ToString().Should().Contain("Sample");
            error.ToString().Should().NotContain("No coverage report was produced");
            error.ToString().Should().NotContain("contained no coverage data");
        });
    }

    // 4 <- MainTest.helpWritesUsageToStdout (via Execute directly). --help -> usage to stdout, exit 0, no run.
    [Fact]
    public void HelpWritesUsageToStdout()
    {
        WithTempRoot(root =>
        {
            using StringWriter output = new();
            using StringWriter error = new();
            FakeExecutor fake = new(0, null);
            CliApplication app = new(root, output, error, new CoverageRunner(fake));

            int exit = app.Execute(["--help"]);

            exit.Should().Be(0);
            output.ToString().Should().Contain("Usage:");
            fake.Directories.Should().BeEmpty();
        });
    }

    // 5 <- CliApplicationTest.thresholdExceededUsesStrictlyGreaterThanEight. Pins the strict '>' boundary.
    [Fact]
    public void ThresholdExceededUsesStrictlyGreaterThanEight()
    {
        CliApplication.ThresholdExceeded(8.0).Should().BeFalse();
        CliApplication.ThresholdExceeded(8.1).Should().BeTrue();
    }

    // 6 <- MainTest.maxCrapReturnsLargestNonNullScore (now CliApplication.MaxCrap).
    [Fact]
    public void MaxCrapReturnsLargestNonNullScore()
    {
        IReadOnlyList<MethodMetrics> metrics =
        [
            new MethodMetrics("alpha", "demo.Sample", 1, null, null),
            new MethodMetrics("beta", "demo.Sample", 1, 75.0, 4.5),
            new MethodMetrics("gamma", "demo.Sample", 1, 85.0, 7.0),
        ];

        CliApplication.MaxCrap(metrics).Should().Be(7.0);
    }

    // 7 <- MainTest.explicitFileArgsAreAnalyzed (adapted: a written report is now required for exit 0).
    [Fact]
    public void ExplicitFileArgsAreAnalyzed()
    {
        WithTempRoot(root =>
        {
            string relative = Path.Combine("src", "demo", "Sample.cs");
            WriteFile(root, relative, AlphaSampleSource);
            using StringWriter output = new();
            using StringWriter error = new();
            CliApplication app = new(root, output, error, new CoverageRunner(new FakeExecutor(0, Alpha75Xml)));

            int exit = app.Execute([relative]);

            exit.Should().Be(0);
            output.ToString().Should().Contain("Sample");
            output.ToString().Should().Contain("alpha");
        });
    }

    // 8 <- MainTest.directoryArgAnalyzesJavaFilesUnderThatDirectorySrc (adapted). A directory arg expands via
    // SourceFileFinder under <dir>/src (W12); a written report reaches exit 0.
    [Fact]
    public void DirectoryArgAnalyzesCSharpFilesUnderThatDirectorySrc()
    {
        WithTempRoot(root =>
        {
            WriteFile(root, Path.Combine("module-a", "src", "demo", "Sample.cs"), AlphaSampleSource);
            using StringWriter output = new();
            using StringWriter error = new();
            CliApplication app = new(root, output, error, new CoverageRunner(new FakeExecutor(0, Alpha75Xml)));

            int exit = app.Execute(["module-a"]);

            exit.Should().Be(0);
            output.ToString().Should().Contain("Sample");
            output.ToString().Should().Contain("alpha");
        });
    }

    // 9 -- replaces CliApplicationTest.explicitFileUsesOwningModuleForCoverageAndJacocoXml (departure #7).
    // Coverage runs EXACTLY ONCE at ModuleRootResolver.Resolve(projectRoot) with the locked token list.
    [Fact]
    public void RunsCoverageOnceAtResolvedModuleRoot()
    {
        WithTempRoot(root =>
        {
            WriteFile(root, "Sample.cs", AlphaSampleSource);
            using StringWriter output = new();
            using StringWriter error = new();
            FakeExecutor fake = new(0, Alpha75Xml);
            CliApplication app = new(root, output, error, new CoverageRunner(fake));

            int exit = app.Execute(["Sample.cs"]);

            exit.Should().Be(0);
            fake.Directories.Should().ContainSingle();
            fake.Directories[0].Should().Be(ModuleRootResolver.Resolve(root));
            fake.Commands[0].Should().Equal(
                "dotnet", "test", "--collect:XPlat Code Coverage", "--results-directory", "coverage");
        });
    }

    // 10 -- FAIL-FAST TRIGGER 1: the runner succeeds but emits no report -> exit 1, stderr anchor.
    [Fact]
    public void FailsFastWhenNoCoverageReportProduced()
    {
        WithTempRoot(root =>
        {
            WriteFile(root, "Sample.cs", AlphaSampleSource);
            using StringWriter output = new();
            using StringWriter error = new();
            CliApplication app = new(root, output, error, new CoverageRunner(new FakeExecutor(0, null)));

            int exit = app.Execute(["Sample.cs"]);

            exit.Should().Be(1);
            error.ToString().Should().Contain("No coverage report was produced");
        });
    }

    // 11 -- FAIL-FAST TRIGGER 2: a report is produced but parses to an empty map -> exit 1, stderr anchor.
    [Fact]
    public void FailsFastWhenCoverageReportIsEmpty()
    {
        WithTempRoot(root =>
        {
            WriteFile(root, "Sample.cs", AlphaSampleSource);
            using StringWriter output = new();
            using StringWriter error = new();
            const string emptyReport = "<?xml version=\"1.0\"?><coverage><packages></packages></coverage>";
            CliApplication app = new(root, output, error, new CoverageRunner(new FakeExecutor(0, emptyReport)));

            int exit = app.Execute(["Sample.cs"]);

            exit.Should().Be(1);
            error.ToString().Should().Contain("contained no coverage data");
        });
    }

    // 12 -- C#-specific: a CC=3 method (two `if`s) with resolved 0% coverage yields CRAP 12 > 8 -> exit 2.
    [Fact]
    public void ReturnsTwoWhenCrapThresholdExceeded()
    {
        const string riskySource = """
            namespace demo;
            class Sample
            {
                int risky(bool a, bool b)
                {
                    if (a)
                    {
                        return 1;
                    }
                    if (b)
                    {
                        return 2;
                    }
                    return 0;
                }
            }
            """;

        // risky declared at line 4; all lines hits=0 -> 0% coverage, resolved from StartLine 4 to the nearest
        // key demo.Sample#risky:6. CRAP = 3^2 * (1-0)^3 + 3 = 12.
        const string zeroCoverageXml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="demo.Sample" filename="Sample.cs">
                      <methods>
                        <method name="risky" signature="(System.Boolean,System.Boolean)">
                          <lines>
                            <line number="6" hits="0" />
                            <line number="8" hits="0" />
                            <line number="10" hits="0" />
                            <line number="12" hits="0" />
                            <line number="14" hits="0" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempRoot(root =>
        {
            WriteFile(root, "Sample.cs", riskySource);
            using StringWriter output = new();
            using StringWriter error = new();
            CliApplication app = new(root, output, error, new CoverageRunner(new FakeExecutor(0, zeroCoverageXml)));

            int exit = app.Execute(["Sample.cs"]);

            exit.Should().Be(2);
            error.ToString().Should().Contain("CRAP threshold exceeded");
        });
    }

    // 13 -- C#-specific trigger-3 boundary: a non-zero coverage exit throws InvalidOperationException, which
    // Execute PROPAGATES (T15 converts it to exit 1).
    [Fact]
    public void PropagatesWhenCoverageCommandFails()
    {
        WithTempRoot(root =>
        {
            WriteFile(root, "Sample.cs", AlphaSampleSource);
            using StringWriter output = new();
            using StringWriter error = new();
            CliApplication app = new(root, output, error, new CoverageRunner(new FakeExecutor(2, null)));

            Action act = () => app.Execute(["Sample.cs"]);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("Coverage command failed with exit 2");
        });
    }

    // 14 -- CA1062 ctor guard.
    [Fact]
    public void ConstructorThrowsOnNullProjectRoot()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        CoverageRunner runner = new(new FakeExecutor(0, null));

        Action act = () => _ = new CliApplication(null!, output, error, runner);

        act.Should().Throw<ArgumentNullException>().WithParameterName("projectRoot");
    }

    // 15 -- CA1062 ctor guard.
    [Fact]
    public void ConstructorThrowsOnNullOutput()
    {
        using StringWriter error = new();
        CoverageRunner runner = new(new FakeExecutor(0, null));

        Action act = () => _ = new CliApplication("root", null!, error, runner);

        act.Should().Throw<ArgumentNullException>().WithParameterName("output");
    }

    // 16 -- CA1062 ctor guard.
    [Fact]
    public void ConstructorThrowsOnNullError()
    {
        using StringWriter output = new();
        CoverageRunner runner = new(new FakeExecutor(0, null));

        Action act = () => _ = new CliApplication("root", output, null!, runner);

        act.Should().Throw<ArgumentNullException>().WithParameterName("error");
    }

    // 17 -- CA1062 ctor guard.
    [Fact]
    public void ConstructorThrowsOnNullCoverageRunner()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        Action act = () => _ = new CliApplication("root", output, error, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("coverageRunner");
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

    private static string WriteFile(string root, string relativePath, string content)
    {
        string path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    // The one shared fake seam (contract §7): records each command + workingDirectory and, when configured
    // with report XML, writes it under <workingDirectory>/coverage/<guid>/coverage.cobertura.xml (the
    // D-T12e reciprocal path CoverageReportLocator reads), then returns the configured exit code. No real
    // process is ever launched.
    private sealed class FakeExecutor : ICommandExecutor
    {
        private readonly int _exitCode;
        private readonly string? _reportXml;

        public FakeExecutor(int exitCode, string? reportXml)
        {
            _exitCode = exitCode;
            _reportXml = reportXml;
        }

        public List<IReadOnlyList<string>> Commands { get; } = [];

        public List<string> Directories { get; } = [];

        public int Run(IReadOnlyList<string> command, string workingDirectory)
        {
            Commands.Add(command);
            Directories.Add(workingDirectory);
            if (_reportXml is not null)
            {
                string reportDirectory =
                    Path.Combine(workingDirectory, "coverage", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(reportDirectory);
                File.WriteAllText(Path.Combine(reportDirectory, "coverage.cobertura.xml"), _reportXml);
            }

            return _exitCode;
        }
    }
}

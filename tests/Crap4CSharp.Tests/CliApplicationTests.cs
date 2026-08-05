namespace Microsoft.Crap4CSharp.Tests;

// Ports crap4java's CliApplicationTest + MainTest into the T14 composition layer, reshaped for Model B
// (departures #9/#10) on top of the earlier #1 (fail-fast) and #7 (resolve-once). CliApplication is exercised
// directly (no Program/T15), injecting a fake ICommandExecutor via CoverageRunner -- exactly the Java
// `CoverageRunner((command, directory) -> ...)` seam -- so no real `dotnet test` is ever launched. Every test
// that must reach the coverage run now scaffolds an on-disk owning project (src/Foo/Foo.csproj), a test project
// (tests/Foo.Tests/Foo.Tests.csproj transitively referencing it), and the analyzed .cs under src/Foo/, so
// OwningProjectResolver + TestProjectResolver resolve. Three new fail-fast tests pin the Model B absence/span
// exits (no owning project, no test project, files span multiple projects), each asserting the runner was NOT
// invoked (fake.Directories empty). Fixtures are modeled on CrapAnalyzerTests.Alpha75Xml; temp trees are always
// removed in a finally; test names are PascalCase (C2/CA1707) while fixture member names in string literals keep
// crap4java's lowercase.
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
    // ArgumentException (W6), caught -> message to stderr, usage to stdout, exit 1 (before resolution).
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

    // 2 <- CliApplicationTest.returnsZeroWhenNoFilesAreFound. No src/ -> no files -> exit 0 BEFORE resolution.
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
            string source = ScaffoldModule(root, "Foo", "Sample.cs", AlphaSampleSource);
            using StringWriter output = new();
            using StringWriter error = new();
            CliApplication app = new(root, output, error, new CoverageRunner(new FakeExecutor(0, Alpha75Xml)));

            int exit = app.Execute([source]);

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

    // 7 <- MainTest.explicitFileArgsAreAnalyzed (adapted: owning+test scaffolding + a written report).
    [Fact]
    public void ExplicitFileArgsAreAnalyzed()
    {
        WithTempRoot(root =>
        {
            string source = ScaffoldModule(root, "Foo", "Sample.cs", AlphaSampleSource);
            using StringWriter output = new();
            using StringWriter error = new();
            CliApplication app = new(root, output, error, new CoverageRunner(new FakeExecutor(0, Alpha75Xml)));

            int exit = app.Execute([source]);

            exit.Should().Be(0);
            output.ToString().Should().Contain("Sample");
            output.ToString().Should().Contain("alpha");
        });
    }

    // 8 <- MainTest.directoryArgAnalyzesJavaFilesUnderThatDirectorySrc (adapted). A directory arg expands via
    // SourceFileFinder under <dir>/src (W12); owning+test are scaffolded under the module-a subtree.
    [Fact]
    public void DirectoryArgAnalyzesCSharpFilesUnderThatDirectorySrc()
    {
        WithTempRoot(root =>
        {
            ScaffoldModule(Path.Combine(root, "module-a"), "Foo", "Sample.cs", AlphaSampleSource);
            using StringWriter output = new();
            using StringWriter error = new();
            CliApplication app = new(root, output, error, new CoverageRunner(new FakeExecutor(0, Alpha75Xml)));

            int exit = app.Execute(["module-a"]);

            exit.Should().Be(0);
            output.ToString().Should().Contain("Sample");
            output.ToString().Should().Contain("alpha");
        });
    }

    // 9 -- replaces RunsCoverageOnceAtResolvedModuleRoot (Model B). Coverage runs EXACTLY ONCE, at the
    // invocation root as coverage base, against the RESOLVED test project, with the locked unit-only token list.
    [Fact]
    public void RunsCoverageOnceAtResolvedTestProject()
    {
        WithTempRoot(root =>
        {
            string source = ScaffoldModule(root, "Foo", "Sample.cs", AlphaSampleSource);
            using StringWriter output = new();
            using StringWriter error = new();
            FakeExecutor fake = new(0, Alpha75Xml);
            CliApplication app = new(root, output, error, new CoverageRunner(fake));

            int exit = app.Execute([source]);

            exit.Should().Be(0);

            string? owning = OwningProjectResolver.ResolveOwningProject(source, root);
            owning.Should().NotBeNull();
            string? expectedTestProject = TestProjectResolver.ResolveTestProject(owning!, root);
            expectedTestProject.Should().NotBeNull();

            fake.Directories.Should().ContainSingle();
            fake.Directories[0].Should().Be(root);
            fake.Commands[0].Should().Equal(
                "dotnet", "test", expectedTestProject!, "--collect:XPlat Code Coverage", "--filter", "type!=IntegrationTests", "--results-directory", "coverage");
        });
    }

    // 10 -- FAIL-FAST TRIGGER 1: resolution passes but the runner emits no report -> exit 1, stderr anchor.
    [Fact]
    public void FailsFastWhenNoCoverageReportProduced()
    {
        WithTempRoot(root =>
        {
            string source = ScaffoldModule(root, "Foo", "Sample.cs", AlphaSampleSource);
            using StringWriter output = new();
            using StringWriter error = new();
            CliApplication app = new(root, output, error, new CoverageRunner(new FakeExecutor(0, null)));

            int exit = app.Execute([source]);

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
            string source = ScaffoldModule(root, "Foo", "Sample.cs", AlphaSampleSource);
            using StringWriter output = new();
            using StringWriter error = new();
            const string emptyReport = "<?xml version=\"1.0\"?><coverage><packages></packages></coverage>";
            CliApplication app = new(root, output, error, new CoverageRunner(new FakeExecutor(0, emptyReport)));

            int exit = app.Execute([source]);

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
            string source = ScaffoldModule(root, "Foo", "Sample.cs", riskySource);
            using StringWriter output = new();
            using StringWriter error = new();
            CliApplication app = new(root, output, error, new CoverageRunner(new FakeExecutor(0, zeroCoverageXml)));

            int exit = app.Execute([source]);

            exit.Should().Be(2);
            error.ToString().Should().Contain("CRAP threshold exceeded");
        });
    }

    // 13 -- C#-specific trigger-3 boundary: a non-zero coverage exit throws CoverageException, which
    // Execute PROPAGATES (T15 converts it to exit 1).
    [Fact]
    public void PropagatesWhenCoverageCommandFails()
    {
        WithTempRoot(root =>
        {
            string source = ScaffoldModule(root, "Foo", "Sample.cs", AlphaSampleSource);
            using StringWriter output = new();
            using StringWriter error = new();
            CliApplication app = new(root, output, error, new CoverageRunner(new FakeExecutor(2, null)));

            Action act = () => app.Execute([source]);

            act.Should().Throw<CoverageException>()
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

    // 18 -- NEW (Model B fail-fast): files present but NO owning .csproj under the root -> exit 1, stderr
    // anchor; the runner is NEVER invoked (resolution fail-fasts before coverage).
    [Fact]
    public void FailsFastWhenNoOwningProject()
    {
        WithTempRoot(root =>
        {
            WriteFile(root, Path.Combine("src", "demo", "Sample.cs"), AlphaSampleSource);
            using StringWriter output = new();
            using StringWriter error = new();
            FakeExecutor fake = new(0, Alpha75Xml);
            CliApplication app = new(root, output, error, new CoverageRunner(fake));

            int exit = app.Execute([]);

            exit.Should().Be(1);
            error.ToString().Should().Contain("has no owning project");
            fake.Directories.Should().BeEmpty();
        });
    }

    // 18b -- NEW (finding #2 regression guard): a MIXED set -- one owned source plus a loose orphan with no
    // owning .csproj -- previously dropped the orphan yet analyzed against the surviving project's coverage
    // (silent N/A, exit 0). Now the unowned file trips the fail-fast -> exit 1, stderr names the orphan; the
    // runner is NEVER invoked (resolution fail-fasts before coverage).
    [Fact]
    public void FailsFastWhenSomeFilesHaveNoOwningProject()
    {
        WithTempRoot(root =>
        {
            string ownedSource = ScaffoldModule(root, "Foo", "A.cs", AlphaSampleSource);
            string orphanPath = WriteFile(root, Path.Combine("loose", "Orphan.cs"), AlphaSampleSource);
            using StringWriter output = new();
            using StringWriter error = new();
            FakeExecutor fake = new(0, Alpha75Xml);
            CliApplication app = new(root, output, error, new CoverageRunner(fake));

            int exit = app.Execute([ownedSource, orphanPath]);

            exit.Should().Be(1);
            error.ToString().Should().Contain("has no owning project");
            error.ToString().Should().Contain("Orphan.cs");
            fake.Directories.Should().BeEmpty();
        });
    }

    // 19 -- NEW (Model B fail-fast): an owning .csproj exists but no .Tests/.UnitTests references it -> exit 1,
    // stderr anchor; the runner is NEVER invoked.
    [Fact]
    public void FailsFastWhenNoTestProject()
    {
        WithTempRoot(root =>
        {
            string srcDir = Path.Combine(root, "src", "Foo");
            Directory.CreateDirectory(srcDir);
            File.WriteAllText(Path.Combine(srcDir, "Foo.csproj"), MinimalProject());
            string source = Path.Combine(srcDir, "Sample.cs");
            File.WriteAllText(source, AlphaSampleSource);
            using StringWriter output = new();
            using StringWriter error = new();
            FakeExecutor fake = new(0, Alpha75Xml);
            CliApplication app = new(root, output, error, new CoverageRunner(fake));

            int exit = app.Execute([source]);

            exit.Should().Be(1);
            error.ToString().Should().Contain("No test project");
            fake.Directories.Should().BeEmpty();
        });
    }

    // 20 -- NEW (Model B fail-fast, FLAG-1 default): analyzed files resolve to two distinct owning projects ->
    // exit 1, stderr anchor; the runner is NEVER invoked.
    [Fact]
    public void FailsFastWhenFilesSpanMultipleProjects()
    {
        WithTempRoot(root =>
        {
            string sourceA = ScaffoldModule(root, "Foo", "A.cs", AlphaSampleSource);
            string sourceB = ScaffoldModule(root, "Bar", "B.cs", AlphaSampleSource);
            using StringWriter output = new();
            using StringWriter error = new();
            FakeExecutor fake = new(0, Alpha75Xml);
            CliApplication app = new(root, output, error, new CoverageRunner(fake));

            int exit = app.Execute([sourceA, sourceB]);

            exit.Should().Be(1);
            error.ToString().Should().Contain("span multiple projects");
            fake.Directories.Should().BeEmpty();
        });
    }

    // 21 -- NEW (T22 fail-fast): a multi-targeted test project makes coverlet emit two coverage.cobertura.xml
    // (one per TFM); LocateAll surfaces both and CliApplication refuses deterministically -> exit 1, stderr
    // anchor. Pinned to fail at the multiplicity gate, NOT the empty gate.
    [Fact]
    public void FailsFastWhenMultipleCoverageReportsProduced()
    {
        WithTempRoot(root =>
        {
            string source = ScaffoldModule(root, "Foo", "Sample.cs", AlphaSampleSource);
            using StringWriter output = new();
            using StringWriter error = new();
            CliApplication app = new(root, output, error, new CoverageRunner(new FakeExecutor(0, Alpha75Xml, 2)));

            int exit = app.Execute([source]);

            exit.Should().Be(1);
            error.ToString().Should().Contain("multiple coverage reports");
            error.ToString().Should().NotContain("contained no coverage data");
        });
    }

    // T30 (docs/decisions.md departure #15 / D-T30d) -- end-to-end gate smokes for the coverlet-blind
    // 0%-promotion, one per empirically-confirmed escape shape plus a regression + a no-spurious-trip
    // guard. Each simulates coverlet #507 by OMITTING the dropped same-line second accessor from the
    // report (exactly what coverlet does), keeping the surviving first accessor present so the
    // type-present guard is satisfied. FakeExecutor supplies the report -- no real `dotnet test`.

    // Escape closed (property, block setter): an uncalled same-line block setter (CC 4) is dropped by
    // #507 -> promoted to 0% -> CRAP 20 -> exit 2. Previously it rendered N/A and escaped the gate.
    [Fact]
    public void SameLineBlockSetterEscapeClosedFiresGate()
    {
        const string source = """
            namespace demo;
            class Sample
            {
                int _v;
                int Danger { get { return _v; } set { if (value > 0) { if (value > 1) { if (value > 2) { _v = value; } } } } }
            }
            """;

        // coverlet #507 keeps only get_Danger (the first accessor on line 5) and drops set_Danger.
        const string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="demo.Sample" filename="Sample.cs">
                      <methods>
                        <method name="get_Danger" signature="()">
                          <lines>
                            <line number="5" hits="1" />
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
            string src = ScaffoldModule(root, "Foo", "Sample.cs", source);
            using StringWriter output = new();
            using StringWriter error = new();
            CliApplication app = new(root, output, error, new CoverageRunner(new FakeExecutor(0, xml)));

            int exit = app.Execute([src]);

            exit.Should().Be(2);
            error.ToString().Should().Contain("CRAP threshold exceeded");
        });
    }

    // Escape closed (property, expression setter): the expression-bodied same-line setter (three ?: ->
    // CC 4) is likewise dropped by #507 -> promoted -> exit 2.
    [Fact]
    public void SameLineExpressionSetterEscapeClosedFiresGate()
    {
        const string source = """
            namespace demo;
            class Sample
            {
                int _v;
                int Danger { get => _v; set => _v = value > 0 ? value > 1 ? value > 2 ? value : 0 : 0 : 0; }
            }
            """;

        const string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="demo.Sample" filename="Sample.cs">
                      <methods>
                        <method name="get_Danger" signature="()">
                          <lines>
                            <line number="5" hits="1" />
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
            string src = ScaffoldModule(root, "Foo", "Sample.cs", source);
            using StringWriter output = new();
            using StringWriter error = new();
            CliApplication app = new(root, output, error, new CoverageRunner(new FakeExecutor(0, xml)));

            int exit = app.Execute([src]);

            exit.Should().Be(2);
            error.ToString().Should().Contain("CRAP threshold exceeded");
        });
    }

    // Escape closed (indexer): an uncalled same-line indexer setter (CC 4) dropped by #507 -> exit 2.
    [Fact]
    public void SameLineIndexerSetterEscapeClosedFiresGate()
    {
        const string source = """
            namespace demo;
            class Sample
            {
                int[] _data;
                int this[int i] { get { return _data[i]; } set { if (i > 0) { if (i > 1) { if (i > 2) { _data[i] = value; } } } } }
            }
            """;

        const string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="demo.Sample" filename="Sample.cs">
                      <methods>
                        <method name="get_Item" signature="(System.Int32)">
                          <lines>
                            <line number="5" hits="1" />
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
            string src = ScaffoldModule(root, "Foo", "Sample.cs", source);
            using StringWriter output = new();
            using StringWriter error = new();
            CliApplication app = new(root, output, error, new CoverageRunner(new FakeExecutor(0, xml)));

            int exit = app.Execute([src]);

            exit.Should().Be(2);
            error.ToString().Should().Contain("CRAP threshold exceeded");
        });
    }

    // Escape closed (custom event): an uncalled same-line remove accessor (CC 4) dropped by #507 -> exit 2.
    [Fact]
    public void SameLineEventRemoveEscapeClosedFiresGate()
    {
        const string source = """
            namespace demo;
            class Bus
            {
                EventHandler? _h;
                event EventHandler Changed { add { _h += value; } remove { if (_h != null) { if (value != null) { if (_h == value) { _h -= value; } } } } }
            }
            """;

        const string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="demo.Bus" filename="Bus.cs">
                      <methods>
                        <method name="add_Changed" signature="(System.EventHandler)">
                          <lines>
                            <line number="5" hits="1" />
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
            string src = ScaffoldModule(root, "Foo", "Bus.cs", source);
            using StringWriter output = new();
            using StringWriter error = new();
            CliApplication app = new(root, output, error, new CoverageRunner(new FakeExecutor(0, xml)));

            int exit = app.Execute([src]);

            exit.Should().Be(2);
            error.ToString().Should().Contain("CRAP threshold exceeded");
        });
    }

    // Regression: a MULTI-LINE setter (get and set on separate lines) is NOT blind, so coverlet emits it
    // normally at real 0% and it is caught the unchanged way (CRAP 20 -> exit 2); promotion never fires.
    [Fact]
    public void MultiLineSetterStillCaughtAtRealZeroCoverage()
    {
        const string source = """
            namespace demo;
            class Sample
            {
                int _v;
                int Danger
                {
                    get => _v;
                    set { if (value > 0) { if (value > 1) { if (value > 2) { _v = value; } } } }
                }
            }
            """;

        // The multi-line setter is present in the report (coverlet does NOT collapse it), all-0%.
        const string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="demo.Sample" filename="Sample.cs">
                      <methods>
                        <method name="set_Danger" signature="(System.Int32)">
                          <lines>
                            <line number="8" hits="0" />
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
            string src = ScaffoldModule(root, "Foo", "Sample.cs", source);
            using StringWriter output = new();
            using StringWriter error = new();
            CliApplication app = new(root, output, error, new CoverageRunner(new FakeExecutor(0, xml)));

            int exit = app.Execute([src]);

            exit.Should().Be(2);
            error.ToString().Should().Contain("CRAP threshold exceeded");
        });
    }

    // No spurious trip: a LOW-complexity same-line second accessor (CC 2) is promoted to 0% but CRAP =
    // 2^2*(1-0)^3 + 2 = 6 <= 8, so the gate does NOT fire -> exit 0. Promotion enriches inputs; it does
    // not manufacture a threshold breach.
    [Fact]
    public void LowComplexitySameLineSecondAccessorDoesNotTripGate()
    {
        const string source = """
            namespace demo;
            class Sample
            {
                int _v;
                int Danger { get => _v; set { if (value > 0) { _v = value; } } }
            }
            """;

        const string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="demo.Sample" filename="Sample.cs">
                      <methods>
                        <method name="get_Danger" signature="()">
                          <lines>
                            <line number="5" hits="1" />
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
            string src = ScaffoldModule(root, "Foo", "Sample.cs", source);
            using StringWriter output = new();
            using StringWriter error = new();
            CliApplication app = new(root, output, error, new CoverageRunner(new FakeExecutor(0, xml)));

            int exit = app.Execute([src]);

            exit.Should().Be(0);
            error.ToString().Should().NotContain("CRAP threshold exceeded");
        });
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

    // Scaffolds a Model B target under moduleDir: src/<project>/<project>.csproj (owning), the analyzed source
    // src/<project>/<sourceFileName>, and tests/<project>.Tests/<project>.Tests.csproj transitively referencing
    // the owning project. Returns the absolute analyzed-source path (usable verbatim as an explicit file arg,
    // since ExplicitFiles combines it with the rooted projectRoot and an absolute arg wins).
    private static string ScaffoldModule(string moduleDir, string project, string sourceFileName, string content)
    {
        string srcDir = Path.Combine(moduleDir, "src", project);
        string testDir = Path.Combine(moduleDir, "tests", project + ".Tests");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(testDir);
        File.WriteAllText(Path.Combine(srcDir, project + ".csproj"), MinimalProject());
        File.WriteAllText(
            Path.Combine(testDir, project + ".Tests.csproj"),
            MinimalProject($@"..\..\src\{project}\{project}.csproj"));
        string sourcePath = Path.Combine(srcDir, sourceFileName);
        File.WriteAllText(sourcePath, content);
        return sourcePath;
    }

    // Minimal SDK-style .csproj text with a <ProjectReference> per relative Include path.
    private static string MinimalProject(params string[] referenceRelPaths)
    {
        string references = string.Concat(
            referenceRelPaths.Select(rel => $"<ProjectReference Include=\"{rel}\" />"));
        return $"<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup>{references}</ItemGroup></Project>";
    }

    // The one shared fake seam: records each command + workingDirectory and, when configured with report XML,
    // writes reportCount copies (default 1), each under its own <workingDirectory>/coverage/<guid>/
    // coverage.cobertura.xml (the D-T12e reciprocal path CoverageReportLocator reads); reportCount > 1
    // simulates a multi-targeted (<TargetFrameworks>) test project coverlet emits one report per TFM for.
    // Returns the configured exit code. No real process is ever launched.
    private sealed class FakeExecutor : ICommandExecutor
    {
        private readonly int _exitCode;
        private readonly string? _reportXml;
        private readonly int _reportCount;

        public FakeExecutor(int exitCode, string? reportXml, int reportCount = 1)
        {
            _exitCode = exitCode;
            _reportXml = reportXml;
            _reportCount = reportCount;
        }

        public List<IReadOnlyList<string>> Commands { get; } = [];

        public List<string> Directories { get; } = [];

        public int Run(IReadOnlyList<string> command, string workingDirectory)
        {
            Commands.Add(command);
            Directories.Add(workingDirectory);
            if (_reportXml is not null)
            {
                for (int i = 0; i < _reportCount; i++)
                {
                    string reportDirectory =
                        Path.Combine(workingDirectory, "coverage", Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(reportDirectory);
                    File.WriteAllText(Path.Combine(reportDirectory, "coverage.cobertura.xml"), _reportXml);
                }
            }

            return _exitCode;
        }
    }
}

namespace Microsoft.Crap4CSharp.Tests;

// Faithful port of crap4java's CrapAnalyzerTest, re-hosted on the Roslyn parser (T9) + Cobertura
// coverage adapter (T10). The nine Java tests are ported 1:1 with identical intent and oracle values
// (2 / 75.0 / 2.0625 for computesScoresForChangedFiles; the exact->nearest / tie-break / parse-trailing
// helper behaviours; the descending-with-nulls-last sort). One Java test is DROPPED at T11:
// usesSimpleClassNameWhenSourceHasNoPackage exercised classNameFromSource, which has no C# analog --
// the per-method TypeName is produced by CSharpMethodParser (D-T9), and the "no namespace -> bare name"
// behaviour is already pinned by CSharpMethodParserTests' global-namespace form. Eight C#-specific tests
// pin behaviour with no Java original: C1 the load-bearing frozen-reciprocal key chain (T9->T10->T11),
// C2 per-method N/A in a populated report (W4: null, never 0.0), C3 an empty report -> all N/A, C4 an
// empty file list, C5 skipping a nonexistent file, C6 overload disambiguation by nearest line, C7 the
// stable equal-CRAP sort, and C8 the null-argument guards. End-to-end fixtures are written to a fresh
// temp directory and always deleted in a finally (the @TempDir analog); test method names are PascalCase
// (CA1707/C2), while the fixtures keep crap4java's lowercase member names (inside string literals, so
// CA1707 does not apply) to maximise oracle fidelity.
public class CrapAnalyzerTests
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

    // alpha declared at line 4; covered lines 6,7,8 (hits=1) + missed line 10 (hits=0) -> 75%,
    // minChildLine 6 -> key demo.Sample#alpha:6, resolved from alpha's StartLine 4 by nearest line.
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

    // Async source method DrainAsync (declared line 5) with Roslyn CC = 2 (base 1 + one `while`). Its
    // lowered state machine emits coverage on demo.Runner/<DrainAsync>d__0's MoveNext (T21 attribution).
    private const string AsyncRunnerSource = """
        using System.Threading.Tasks;
        namespace demo;
        class Runner
        {
            async Task DrainAsync(int n)
            {
                int i = 0;
                while (i < n)
                {
                    i++;
                }
            }
        }
        """;

    // Iterator source method ResolveAbsolutePaths (declared line 5) with Roslyn CC = 3 (base 1 + one
    // `foreach` + one `if`; `yield` adds nothing). Its lowered state machine emits coverage on
    // demo.Paths/<ResolveAbsolutePaths>d__0's MoveNext (T21 attribution).
    private const string IteratorPathsSource = """
        using System.Collections.Generic;
        namespace demo;
        class Paths
        {
            IEnumerable<int> ResolveAbsolutePaths(int[] xs)
            {
                foreach (var x in xs)
                {
                    if (x > 0)
                    {
                        yield return x;
                    }
                }
            }
        }
        """;

    // Both state machines in one Cobertura file. Each MoveNext carries 10 lowered body lines near the
    // source method's declaration (7 hit + 3 unhit -> 70%), keyed on the ENCLOSING type + demangled
    // source-method name (demo.Runner#DrainAsync:7, demo.Paths#ResolveAbsolutePaths:7) by T21.
    private const string AsyncIteratorXml = """
        <?xml version="1.0"?>
        <coverage>
          <packages>
            <package name="demo">
              <classes>
                <class name="demo.Runner/&lt;DrainAsync&gt;d__0" filename="Runner.cs">
                  <methods>
                    <method name="MoveNext" signature="()">
                      <lines>
                        <line number="7" hits="1" />
                        <line number="8" hits="1" />
                        <line number="9" hits="1" />
                        <line number="10" hits="1" />
                        <line number="11" hits="1" />
                        <line number="12" hits="1" />
                        <line number="13" hits="1" />
                        <line number="14" hits="0" />
                        <line number="15" hits="0" />
                        <line number="16" hits="0" />
                      </lines>
                    </method>
                  </methods>
                </class>
                <class name="demo.Paths/&lt;ResolveAbsolutePaths&gt;d__0" filename="Paths.cs">
                  <methods>
                    <method name="MoveNext" signature="()">
                      <lines>
                        <line number="7" hits="1" />
                        <line number="8" hits="1" />
                        <line number="9" hits="1" />
                        <line number="10" hits="1" />
                        <line number="11" hits="1" />
                        <line number="12" hits="1" />
                        <line number="13" hits="1" />
                        <line number="14" hits="0" />
                        <line number="15" hits="0" />
                        <line number="16" hits="0" />
                      </lines>
                    </method>
                  </methods>
                </class>
              </classes>
            </package>
          </packages>
        </coverage>
        """;

    // T21 end-to-end: an async and an iterator source method now receive REAL coverage (not N/A) via the
    // state-machine MoveNext attribution. Async CC = 2 @ 70% -> CRAP 2^2*0.3^3+2 = 2.108; iterator CC = 3
    // @ 70% -> CRAP 3^2*0.3^3+3 = 3.243. The MoveNext key resolves via T11's nearest-line lookup from each
    // method's StartLine (single key per source method -> resolves at any distance).
    [Fact]
    public void ReportsRealCrapForAsyncAndIteratorMethods()
    {
        WithTempDir(dir =>
        {
            string asyncSource = WriteFile(dir, "Runner.cs", AsyncRunnerSource);
            string iteratorSource = WriteFile(dir, "Paths.cs", IteratorPathsSource);
            string cobertura = WriteFile(dir, "coverage.cobertura.xml", AsyncIteratorXml);

            IReadOnlyList<MethodMetrics> result = CrapAnalyzer.Analyze([asyncSource, iteratorSource], cobertura);

            MethodMetrics asyncMetric = result.Single(m => m.MethodName == "DrainAsync");
            asyncMetric.ClassName.Should().Be("demo.Runner");
            asyncMetric.Complexity.Should().Be(2);
            asyncMetric.CoveragePercent.Should().NotBeNull();
            asyncMetric.CoveragePercent!.Value.Should().BeApproximately(70.0, 1e-9);
            asyncMetric.CrapScore.Should().NotBeNull();
            asyncMetric.CrapScore!.Value.Should().BeApproximately(2.108, 1e-9);

            MethodMetrics iteratorMetric = result.Single(m => m.MethodName == "ResolveAbsolutePaths");
            iteratorMetric.ClassName.Should().Be("demo.Paths");
            iteratorMetric.Complexity.Should().Be(3);
            iteratorMetric.CoveragePercent.Should().NotBeNull();
            iteratorMetric.CoveragePercent!.Value.Should().BeApproximately(70.0, 1e-9);
            iteratorMetric.CrapScore.Should().NotBeNull();
            iteratorMetric.CrapScore!.Value.Should().BeApproximately(3.243, 1e-9);
        });
    }

    // P1 <- computesScoresForChangedFiles.
    [Fact]
    public void ComputesScoresForChangedFiles()
    {
        WithTempDir(dir =>
        {
            string source = WriteFile(dir, "Sample.cs", AlphaSampleSource);
            string cobertura = WriteFile(dir, "coverage.cobertura.xml", Alpha75Xml);

            IReadOnlyList<MethodMetrics> result = CrapAnalyzer.Analyze([source], cobertura);

            result.Should().HaveCount(1);
            MethodMetrics metric = result[0];
            metric.MethodName.Should().Be("alpha");
            metric.ClassName.Should().Be("demo.Sample");
            metric.Complexity.Should().Be(2);
            metric.CoveragePercent.Should().BeApproximately(75.0, 0.001);
            metric.CrapScore.Should().BeApproximately(2.0625, 0.00001);
        });
    }

    // P2 <- lookupCoveragePrefersExactLineBeforeNearestMatch.
    [Fact]
    public void LookupCoveragePrefersExactLineOverNearest()
    {
        Dictionary<string, CoverageData> coverageMap = new(StringComparer.Ordinal)
        {
            ["demo.Sample#alpha#Sample.cs:10"] = new CoverageData(1, 3),
            ["demo.Sample#alpha#Sample.cs:12"] = new CoverageData(0, 8),
        };

        double? coverage = CrapAnalyzer.LookupCoverage(coverageMap, "demo.Sample", "alpha", "Sample.cs", 10);

        coverage.Should().BeApproximately(75.0, 0.001);
    }

    // P3 <- lookupCoverageFallsBackToNearestLineWithinMethod.
    [Fact]
    public void LookupCoverageFallsBackToNearestLineWithinMethod()
    {
        Dictionary<string, CoverageData> coverageMap = new(StringComparer.Ordinal)
        {
            ["demo.Sample#alpha#Sample.cs:10"] = new CoverageData(1, 3),
            ["demo.Sample#alpha#Sample.cs:15"] = new CoverageData(0, 8),
        };

        double? coverage = CrapAnalyzer.LookupCoverage(coverageMap, "demo.Sample", "alpha", "Sample.cs", 13);

        coverage.Should().BeApproximately(100.0, 0.001);
    }

    // P4 <- nearestCoverageKeepsFirstEntryWhenDistancesTie. Insertion order (:10 then :14) is the
    // LinkedHashMap analog; strict '<' keeps the first-inserted entry when both are distance 2.
    [Fact]
    public void NearestCoverageKeepsFirstEntryWhenDistancesTie()
    {
        Dictionary<string, CoverageData> coverageMap = new(StringComparer.Ordinal)
        {
            ["demo.Sample#alpha#Sample.cs:10"] = new CoverageData(1, 3),
            ["demo.Sample#alpha#Sample.cs:14"] = new CoverageData(0, 8),
        };

        CoverageData? nearest = CrapAnalyzer.NearestCoverage(coverageMap, "demo.Sample", "alpha", "Sample.cs", 12);

        nearest.Should().NotBeNull();
        nearest!.CoveragePercent.Should().BeApproximately(75.0, 0.001);
    }

    // P5 <- lookupCoverageReturnsNullWhenMethodHasNoCoverageEntries.
    [Fact]
    public void LookupCoverageReturnsNullWhenNoCoverageEntries()
    {
        Dictionary<string, CoverageData> empty = new(StringComparer.Ordinal);

        double? coverage = CrapAnalyzer.LookupCoverage(empty, "demo.Sample", "alpha", "Sample.cs", 10);

        coverage.Should().BeNull();
    }

    // P6 <- parseTrailingLineReturnsMaxValueForMalformedKeys.
    [Fact]
    public void ParseTrailingLineReturnsMaxValueForMalformedKeys()
    {
        CrapAnalyzer.ParseTrailingLine("demo.Sample#alpha").Should().Be(int.MaxValue);
        CrapAnalyzer.ParseTrailingLine("demo.Sample#alpha:").Should().Be(int.MaxValue);
        CrapAnalyzer.ParseTrailingLine("demo.Sample#alpha:oops").Should().Be(int.MaxValue);
    }

    // P7 <- parseTrailingLineReturnsParsedLineNumberForValidKey.
    [Fact]
    public void ParseTrailingLineReturnsParsedLineNumberForValidKey()
    {
        CrapAnalyzer.ParseTrailingLine("demo.Sample#alpha:10").Should().Be(10);

        // T24: the key gains a #basename segment before ':line'; the last-':' split still finds the line.
        CrapAnalyzer.ParseTrailingLine("demo.Sample#alpha#Sample.cs:10").Should().Be(10);
    }

    // P8 <- parseTrailingLineAcceptsLeadingSeparator.
    [Fact]
    public void ParseTrailingLineAcceptsLeadingSeparator()
    {
        CrapAnalyzer.ParseTrailingLine(":10").Should().Be(10);
    }

    // P9 <- sortsMetricsByScoreDescendingWithNullsLast. alpha (0% -> crap 6.0), beta (100% -> crap 1.0),
    // gamma absent (N/A -> crap null) -> sorted alpha, beta, gamma.
    [Fact]
    public void SortsMetricsByScoreDescendingWithNullsLast()
    {
        const string source = """
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

                int beta()
                {
                    return 1;
                }

                int gamma()
                {
                    return 2;
                }
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
                        <method name="alpha" signature="(System.Boolean)">
                          <lines>
                            <line number="7" hits="0" />
                            <line number="9" hits="0" />
                            <line number="11" hits="0" />
                          </lines>
                        </method>
                        <method name="beta" signature="()">
                          <lines>
                            <line number="16" hits="1" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempDir(dir =>
        {
            string sourcePath = WriteFile(dir, "Sample.cs", source);
            string cobertura = WriteFile(dir, "coverage.cobertura.xml", xml);

            IReadOnlyList<MethodMetrics> result = CrapAnalyzer.Analyze([sourcePath], cobertura);

            result.Select(m => m.MethodName).Should().Equal("alpha", "beta", "gamma");
        });
    }

    // C1 -- the load-bearing consumer test: a nested generic type flows byte-for-byte through
    // T9 (TypeNameOf -> Demo.Outer`1.Inner`2) and T10 (NormalizeTypeName of the '/'-separated coverlet
    // name) so T11's key matches. A resolved 100% (not N/A) proves the frozen reciprocal held.
    [Fact]
    public void ResolvesCoverageThroughFrozenReciprocalKeyEndToEnd()
    {
        const string source = """
            namespace Demo;
            class Outer<T>
            {
                class Inner<U, V>
                {
                    int Wrap(T t, U u, V v)
                    {
                        if (t is null)
                        {
                            return 0;
                        }
                        return 1;
                    }
                }
            }
            """;

        const string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="Demo">
                  <classes>
                    <class name="Demo.Outer`1/Inner`2" filename="Outer.cs">
                      <methods>
                        <method name="Wrap" signature="(T,U,V)">
                          <lines>
                            <line number="10" hits="1" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempDir(dir =>
        {
            string sourcePath = WriteFile(dir, "Outer.cs", source);
            string cobertura = WriteFile(dir, "coverage.cobertura.xml", xml);

            IReadOnlyList<MethodMetrics> result = CrapAnalyzer.Analyze([sourcePath], cobertura);

            result.Should().HaveCount(1);
            MethodMetrics metric = result[0];
            metric.ClassName.Should().Be("Demo.Outer`1.Inner`2");
            metric.MethodName.Should().Be("Wrap");
            metric.Complexity.Should().Be(2);
            metric.CoveragePercent.Should().NotBeNull();
            metric.CoveragePercent.Should().BeApproximately(100.0, 0.001);
            metric.CrapScore.Should().BeApproximately(2.0, 0.00001);
        });
    }

    // C2 -- W4: a method present in the source but ABSENT from a populated report yields null coverage
    // and null CRAP (never 0.0), while the present method keeps its real 75% measurement.
    [Fact]
    public void AnalyzeReportsNaForMethodAbsentFromPopulatedReport()
    {
        const string source = """
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

                int beta()
                {
                    return 1;
                }
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
                        <method name="alpha" signature="(System.Boolean)">
                          <lines>
                            <line number="7" hits="1" />
                            <line number="8" hits="0" />
                            <line number="9" hits="1" />
                            <line number="11" hits="1" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempDir(dir =>
        {
            string sourcePath = WriteFile(dir, "Sample.cs", source);
            string cobertura = WriteFile(dir, "coverage.cobertura.xml", xml);

            IReadOnlyList<MethodMetrics> result = CrapAnalyzer.Analyze([sourcePath], cobertura);

            result.Should().HaveCount(2);

            MethodMetrics present = result[0];
            present.MethodName.Should().Be("alpha");
            present.Complexity.Should().Be(2);
            present.CoveragePercent.Should().BeApproximately(75.0, 0.001);
            present.CrapScore.Should().BeApproximately(2.0625, 0.00001);

            MethodMetrics absent = result[1];
            absent.MethodName.Should().Be("beta");
            absent.Complexity.Should().Be(1);
            absent.CoveragePercent.Should().BeNull();
            absent.CrapScore.Should().BeNull();
        });
    }

    // C3 -- a missing report (nonexistent path -> empty map from T10) makes every method N/A.
    [Fact]
    public void AnalyzeTreatsMissingReportAsAllNa()
    {
        WithTempDir(dir =>
        {
            string source = WriteFile(dir, "Sample.cs", AlphaSampleSource);
            string missingReport = Path.Combine(dir, "does-not-exist.cobertura.xml");

            IReadOnlyList<MethodMetrics> result = CrapAnalyzer.Analyze([source], missingReport);

            result.Should().HaveCount(1);
            MethodMetrics metric = result[0];
            metric.MethodName.Should().Be("alpha");
            metric.Complexity.Should().Be(2);
            metric.CoveragePercent.Should().BeNull();
            metric.CrapScore.Should().BeNull();
        });
    }

    // C4 -- an empty changed-file list returns no metrics (loop body never runs).
    [Fact]
    public void AnalyzeReturnsEmptyForEmptyFileList()
    {
        string anyReport = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cobertura.xml");

        IReadOnlyList<MethodMetrics> result = CrapAnalyzer.Analyze([], anyReport);

        result.Should().BeEmpty();
    }

    // C5 -- a nonexistent path in the list is skipped (File.Exists false) with no throw.
    [Fact]
    public void AnalyzeSkipsNonexistentFiles()
    {
        WithTempDir(dir =>
        {
            string source = WriteFile(dir, "Sample.cs", AlphaSampleSource);
            string cobertura = WriteFile(dir, "coverage.cobertura.xml", Alpha75Xml);
            string missing = Path.Combine(dir, "Missing.cs");

            IReadOnlyList<MethodMetrics> result = CrapAnalyzer.Analyze([missing, source], cobertura);

            result.Should().HaveCount(1);
            result[0].MethodName.Should().Be("alpha");
            result[0].CoveragePercent.Should().BeApproximately(75.0, 0.001);
        });
    }

    // C6 -- two overloads share the demo.Calc#Add: prefix; each StartLine resolves to its OWN body's
    // nearest coverage entry (Add(int)@5 -> :7 100%; Add(string)@10 -> :12 0%).
    [Fact]
    public void DisambiguatesOverloadsByNearestLine()
    {
        const string source = """
            namespace Demo;

            class Calc
            {
                int Add(int x)
                {
                    return x + 1;
                }

                int Add(string s)
                {
                    if (s.Length > 0)
                    {
                        return 1;
                    }
                    return 0;
                }
            }
            """;

        const string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="Demo">
                  <classes>
                    <class name="Demo.Calc" filename="Calc.cs">
                      <methods>
                        <method name="Add" signature="(System.Int32)">
                          <lines>
                            <line number="7" hits="1" />
                          </lines>
                        </method>
                        <method name="Add" signature="(System.String)">
                          <lines>
                            <line number="12" hits="0" />
                            <line number="14" hits="0" />
                            <line number="16" hits="0" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempDir(dir =>
        {
            string sourcePath = WriteFile(dir, "Calc.cs", source);
            string cobertura = WriteFile(dir, "coverage.cobertura.xml", xml);

            IReadOnlyList<MethodMetrics> result = CrapAnalyzer.Analyze([sourcePath], cobertura);

            result.Should().HaveCount(2);

            MethodMetrics higher = result[0];
            higher.MethodName.Should().Be("Add");
            higher.ClassName.Should().Be("Demo.Calc");
            higher.Complexity.Should().Be(2);
            higher.CoveragePercent.Should().BeApproximately(0.0, 0.001);
            higher.CrapScore.Should().BeApproximately(6.0, 0.00001);

            MethodMetrics lower = result[1];
            lower.MethodName.Should().Be("Add");
            lower.Complexity.Should().Be(1);
            lower.CoveragePercent.Should().BeApproximately(100.0, 0.001);
            lower.CrapScore.Should().BeApproximately(1.0, 0.00001);
        });
    }

    // T24 collision proof: a partial class Widget split across A.cs + B.cs declares an overload of Render
    // in EACH file, both at StartLine 4 with an overlapping coverage min-line (:6). Coverlet emits one
    // <class name="Demo.Widget"> per file, so the pre-T24 Type#method:line key collided and BOTH overloads
    // borrowed whichever entry the second parse left in the map. T24's #<basename> binds each overload to
    // ITS OWN file's coverage: Render(int)/A.cs -> 100%, Render(string)/B.cs -> 0%. Complexity (1 vs 2)
    // pins file -> metric unambiguously since both methods are named Render.
    [Fact]
    public void SplitPartialClassOverloadsResolveToOwnFileCoverage()
    {
        const string aSource = """
            namespace Demo;
            partial class Widget
            {
                int Render(int x)
                {
                    return x + 1;
                }
            }
            """;

        const string bSource = """
            namespace Demo;
            partial class Widget
            {
                int Render(string s)
                {
                    if (s.Length > 0)
                    {
                        return 1;
                    }
                    return 0;
                }
            }
            """;

        // A.cs: Render(int)'s body line 6 hit -> 100%. B.cs: Render(string)'s body line 6 unhit -> 0%.
        // Both overloads have StartLine 4, so each resolves its own file's :6 entry at nearest distance 2.
        const string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="Demo">
                  <classes>
                    <class name="Demo.Widget" filename="A.cs">
                      <methods>
                        <method name="Render" signature="(System.Int32)">
                          <lines>
                            <line number="6" hits="1" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                    <class name="Demo.Widget" filename="B.cs">
                      <methods>
                        <method name="Render" signature="(System.String)">
                          <lines>
                            <line number="6" hits="0" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempDir(dir =>
        {
            string aPath = WriteFile(dir, "A.cs", aSource);
            string bPath = WriteFile(dir, "B.cs", bSource);
            string cobertura = WriteFile(dir, "coverage.cobertura.xml", xml);

            IReadOnlyList<MethodMetrics> result = CrapAnalyzer.Analyze([aPath, bPath], cobertura);

            result.Should().HaveCount(2);

            // Render(int) -- A.cs, CC 1, its OWN file's 100% (NOT borrowed).
            MethodMetrics fromA = result.Single(m => m.Complexity == 1);
            fromA.MethodName.Should().Be("Render");
            fromA.ClassName.Should().Be("Demo.Widget");
            fromA.CoveragePercent.Should().BeApproximately(100.0, 0.001);
            fromA.CrapScore.Should().BeApproximately(1.0, 0.00001);

            // Render(string) -- B.cs, CC 2, its OWN file's 0% (CRAP 6.0). B.cs's <class> is parsed LAST,
            // so its 0% entry is the one that SURVIVES the pre-T24 collision -- fromB stays 0% either way.
            // It is therefore the Render(int)/A.cs 100% assertion ABOVE (fromA), NOT this one, that fails
            // loudly if the borrowing bug regresses: A.cs's Render(int) would borrow B.cs's surviving 0%.
            MethodMetrics fromB = result.Single(m => m.Complexity == 2);
            fromB.MethodName.Should().Be("Render");
            fromB.ClassName.Should().Be("Demo.Widget");
            fromB.CoveragePercent.Should().BeApproximately(0.0, 0.001);
            fromB.CrapScore.Should().BeApproximately(6.0, 0.00001);

            // Residual (accepted, T24 / departure #12): the key carries the basename, NOT the directory,
            // so two partial-class files sharing the SAME basename in DIFFERENT directories (e.g.
            // Foo/Widget.cs + Bar/Widget.cs) still collide. Rare; documented-not-fixed. See
            // docs/decisions.md T24 register.
        });
    }

    // C7 -- two equal-CRAP methods (both 100% -> crap 1.0) keep document order under the stable sort
    // (the OrderBy/ThenByDescending guard against an unstable List.Sort).
    [Fact]
    public void SortIsStableForEqualCrapScores()
    {
        const string source = """
            namespace demo;

            class Sample
            {
                int first()
                {
                    return 1;
                }

                int second()
                {
                    return 2;
                }
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
                        <method name="first" signature="()">
                          <lines>
                            <line number="7" hits="1" />
                          </lines>
                        </method>
                        <method name="second" signature="()">
                          <lines>
                            <line number="12" hits="1" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempDir(dir =>
        {
            string sourcePath = WriteFile(dir, "Sample.cs", source);
            string cobertura = WriteFile(dir, "coverage.cobertura.xml", xml);

            IReadOnlyList<MethodMetrics> result = CrapAnalyzer.Analyze([sourcePath], cobertura);

            result.Select(m => m.MethodName).Should().Equal("first", "second");
            result.Should().OnlyContain(m => m.CrapScore != null);
            result[0].CrapScore.Should().BeApproximately(1.0, 0.00001);
            result[1].CrapScore.Should().BeApproximately(1.0, 0.00001);
        });
    }

    // C8 -- house-style null guards (CA1062): both public Analyze params are validated before any work,
    // so even an empty file list still throws on a null path.
    [Fact]
    public void AnalyzeThrowsOnNullArguments()
    {
        Action nullFiles = () => CrapAnalyzer.Analyze(null!, "any.cobertura.xml");
        nullFiles.Should().Throw<ArgumentNullException>();

        Action nullPath = () => CrapAnalyzer.Analyze([], null!);
        nullPath.Should().Throw<ArgumentNullException>();
    }

    // T23 (6.9) -- FINDING #3, end-to-end. A method Compute whose only lowered coverage is a never-invoked
    // local function <Compute>g__Validate|0_0 (all unhit) now reports REAL 0% coverage instead of the
    // pre-T23 N/A (skip). CC folds the local function so Compute is CC 1; CRAP = 1^2*(1-0)^3+1 = 2.0. The
    // point: NOT N/A, NOT 100%/PASS -- the method now participates in the exit-2 gate.
    [Fact]
    public void AttributesNeverInvokedLocalFunctionSoCrapReflectsZeroCoverage()
    {
        const string source = """
            namespace Demo;
            class Calc
            {
                int Compute(int x)
                {
                    int Validate(int y) => y;
                    return x;
                }
            }
            """;

        const string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="Demo">
                  <classes>
                    <class name="Demo.Calc" filename="Calc.cs">
                      <methods>
                        <method name="&lt;Compute&gt;g__Validate|0_0" signature="(System.Int32)">
                          <lines>
                            <line number="6" hits="0" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempDir(dir =>
        {
            string sourcePath = WriteFile(dir, "Calc.cs", source);
            string cobertura = WriteFile(dir, "coverage.cobertura.xml", xml);

            IReadOnlyList<MethodMetrics> result = CrapAnalyzer.Analyze([sourcePath], cobertura);

            MethodMetrics metric = result.Single(m => m.MethodName == "Compute");
            metric.ClassName.Should().Be("Demo.Calc");
            metric.Complexity.Should().Be(1);
            metric.CoveragePercent.Should().NotBeNull();
            metric.CoveragePercent!.Value.Should().BeApproximately(0.0, 1e-9);
            metric.CrapScore.Should().NotBeNull();
            metric.CrapScore!.Value.Should().BeApproximately(2.0, 1e-9);
        });
    }

    // T23 (6.10) -- a covered capturing lambda's display-class member <Run>b__0 UNIONs onto its enclosing
    // method Run, so Run resolves to REAL coverage. CC 2 (the `if`) @ 100% -> CRAP = 2^2*(1-1)^3+2 = 2.0.
    [Fact]
    public void AttributesLambdaCoverageToEnclosingMethod()
    {
        const string source = """
            using System;
            namespace Demo;
            class Worker
            {
                int Run(int x)
                {
                    Func<int, int> f = n => n + x;
                    if (x > 0)
                    {
                        return f(x);
                    }
                    return 0;
                }
            }
            """;

        const string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="Demo">
                  <classes>
                    <class name="Demo.Worker" filename="Worker.cs">
                      <methods>
                        <method name="Run" signature="(System.Int32)">
                          <lines>
                            <line number="8" hits="1" />
                            <line number="10" hits="1" />
                            <line number="12" hits="1" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                    <class name="Demo.Worker/&lt;&gt;c__DisplayClass0_0" filename="Worker.cs">
                      <methods>
                        <method name="&lt;Run&gt;b__0" signature="(System.Int32)">
                          <lines>
                            <line number="7" hits="1" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempDir(dir =>
        {
            string sourcePath = WriteFile(dir, "Worker.cs", source);
            string cobertura = WriteFile(dir, "coverage.cobertura.xml", xml);

            IReadOnlyList<MethodMetrics> result = CrapAnalyzer.Analyze([sourcePath], cobertura);

            MethodMetrics metric = result.Single(m => m.MethodName == "Run");
            metric.ClassName.Should().Be("Demo.Worker");
            metric.Complexity.Should().Be(2);
            metric.CoveragePercent.Should().NotBeNull();
            metric.CoveragePercent!.Value.Should().BeApproximately(100.0, 1e-9);
            metric.CrapScore.Should().NotBeNull();
            metric.CrapScore!.Value.Should().BeApproximately(2.0, 1e-9);
        });
    }

    // T23 (6.11) -- SAFETY PROPERTY (D-T24d family). A display class whose filename ("Other.cs") differs
    // from the enclosing method's source file ("Calc.cs") produces an unmatchable key, so the covered
    // lambda CANNOT attribute to Compute: the method stays N/A (null coverage + null CRAP) -- a SAFE
    // under-report, NEVER a false pass, even though the synthetic member is fully covered.
    [Fact]
    public void DisplayClassWithMismatchedFilenameYieldsNAnotFalsePass()
    {
        const string source = """
            using System;
            namespace Demo;
            class Calc
            {
                int Compute(int x)
                {
                    Func<int, int> f = n => n + x;
                    return f(x);
                }
            }
            """;

        const string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="Demo">
                  <classes>
                    <class name="Demo.Calc/&lt;&gt;c__DisplayClass0_0" filename="Other.cs">
                      <methods>
                        <method name="&lt;Compute&gt;b__0" signature="(System.Int32)">
                          <lines>
                            <line number="7" hits="1" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempDir(dir =>
        {
            string sourcePath = WriteFile(dir, "Calc.cs", source);
            string cobertura = WriteFile(dir, "coverage.cobertura.xml", xml);

            IReadOnlyList<MethodMetrics> result = CrapAnalyzer.Analyze([sourcePath], cobertura);

            MethodMetrics metric = result.Single(m => m.MethodName == "Compute");
            metric.CoveragePercent.Should().BeNull();
            metric.CrapScore.Should().BeNull();
        });
    }

    // T26 (departure #14): executable-member decomposition, end-to-end. Accessors and operators now
    // round-trip through the frozen reciprocal key under their CLR names (get_/op_), and a renamed
    // indexer degrades to a safe N/A -- never a false pass.
    //
    // A1 -- the real get_/op_ round-trip. get_Value resolves to its OWN 100% entry (exact line -> CRAP
    // 1.0); op_Addition resolves by nearest line to its OWN 0% entry (CRAP = 1^2*(1-0)^3+1 = 2.0).
    [Fact]
    public void MapsAccessorAndOperatorCoverageToTheirDescriptors()
    {
        const string source = """
            namespace Demo;
            class Widget
            {
                int _v;
                int Value
                {
                    get { return _v; }
                }
                public static Widget operator +(Widget a, Widget b)
                {
                    return new Widget();
                }
            }
            """;

        const string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="Demo">
                  <classes>
                    <class name="Demo.Widget" filename="Widget.cs">
                      <methods>
                        <method name="get_Value" signature="()">
                          <lines>
                            <line number="7" hits="1" />
                          </lines>
                        </method>
                        <method name="op_Addition" signature="(Demo.Widget,Demo.Widget)">
                          <lines>
                            <line number="11" hits="0" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempDir(dir =>
        {
            string sourcePath = WriteFile(dir, "Widget.cs", source);
            string cobertura = WriteFile(dir, "coverage.cobertura.xml", xml);

            IReadOnlyList<MethodMetrics> result = CrapAnalyzer.Analyze([sourcePath], cobertura);

            MethodMetrics getter = result.Single(m => m.MethodName == "get_Value");
            getter.ClassName.Should().Be("Demo.Widget");
            getter.Complexity.Should().Be(1);
            getter.CoveragePercent.Should().NotBeNull();
            getter.CoveragePercent!.Value.Should().BeApproximately(100.0, 1e-9);
            getter.CrapScore!.Value.Should().BeApproximately(1.0, 1e-9);

            MethodMetrics op = result.Single(m => m.MethodName == "op_Addition");
            op.ClassName.Should().Be("Demo.Widget");
            op.Complexity.Should().Be(1);
            op.CoveragePercent.Should().NotBeNull();
            op.CoveragePercent!.Value.Should().BeApproximately(0.0, 1e-9);
            op.CrapScore!.Value.Should().BeApproximately(2.0, 1e-9);
        });
    }

    // A2 -- two well-separated implicit conversions collide by CLR name (op_Implicit) but resolve
    // INDEPENDENTLY: each StartLine's nearest coverage entry is its OWN body. conv#1 (CC 1) -> 100% ->
    // CRAP 1.0; conv#2 (CC 2, the `if`) -> 0% -> CRAP = 2^2*(1-0)^3+2 = 6.0. Complexity pins file->metric.
    [Fact]
    public void ResolvesSeparatedSameNameImplicitConversionsIndependently()
    {
        const string source = """
            namespace Demo;
            struct Money
            {
                long _v;
                public static implicit operator int(Money m)
                {
                    return (int)m._v;
                }

                public static implicit operator long(Money m)
                {
                    if (m._v > 0)
                    {
                        return m._v;
                    }
                    return 0;
                }
            }
            """;

        // conv#1 body min-line 7 hit -> 100%; conv#2 body min-line 12 unhit -> 0%. StartLines 5 and 10
        // resolve to the nearest (7 and 12 respectively), so neither borrows the other's entry.
        const string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="Demo">
                  <classes>
                    <class name="Demo.Money" filename="Money.cs">
                      <methods>
                        <method name="op_Implicit" signature="(Demo.Money)">
                          <lines>
                            <line number="7" hits="1" />
                          </lines>
                        </method>
                        <method name="op_Implicit" signature="(Demo.Money)">
                          <lines>
                            <line number="12" hits="0" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempDir(dir =>
        {
            string sourcePath = WriteFile(dir, "Money.cs", source);
            string cobertura = WriteFile(dir, "coverage.cobertura.xml", xml);

            IReadOnlyList<MethodMetrics> result = CrapAnalyzer.Analyze([sourcePath], cobertura);

            result.Should().HaveCount(2);

            MethodMetrics first = result.Single(m => m.Complexity == 1);
            first.MethodName.Should().Be("op_Implicit");
            first.CoveragePercent!.Value.Should().BeApproximately(100.0, 1e-9);
            first.CrapScore!.Value.Should().BeApproximately(1.0, 1e-9);

            MethodMetrics second = result.Single(m => m.Complexity == 2);
            second.MethodName.Should().Be("op_Implicit");
            second.CoveragePercent!.Value.Should().BeApproximately(0.0, 1e-9);
            second.CrapScore!.Value.Should().BeApproximately(6.0, 1e-9);
        });
    }

    // A3 -- a [IndexerName]-renamed indexer: the parser emits get_Item (the default CLR indexer name),
    // but coverlet emits get_Element (the renamed CLR name). The keys never align, so the metric
    // degrades to a deterministic SAFE N/A (null coverage + null CRAP) -- never a false pass.
    [Fact]
    public void RenamedIndexerDegradesToSafeNA()
    {
        const string source = """
            namespace Demo;
            class Bag
            {
                int[] _data;
                [System.Runtime.CompilerServices.IndexerName("Element")]
                public int this[int i]
                {
                    get { return _data[i]; }
                }
            }
            """;

        const string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="Demo">
                  <classes>
                    <class name="Demo.Bag" filename="Bag.cs">
                      <methods>
                        <method name="get_Element" signature="(System.Int32)">
                          <lines>
                            <line number="8" hits="1" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempDir(dir =>
        {
            string sourcePath = WriteFile(dir, "Bag.cs", source);
            string cobertura = WriteFile(dir, "coverage.cobertura.xml", xml);

            IReadOnlyList<MethodMetrics> result = CrapAnalyzer.Analyze([sourcePath], cobertura);

            MethodMetrics metric = result.Single(m => m.MethodName == "get_Item");
            metric.ClassName.Should().Be("Demo.Bag");
            metric.CoveragePercent.Should().BeNull();
            metric.CrapScore.Should().BeNull();
        });
    }

    private static void WithTempDir(Action<string> test)
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            test(dir);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private static string WriteFile(string dir, string name, string content)
    {
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }
}

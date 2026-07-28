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
            ["demo.Sample#alpha:10"] = new CoverageData(1, 3),
            ["demo.Sample#alpha:12"] = new CoverageData(0, 8),
        };

        double? coverage = CrapAnalyzer.LookupCoverage(coverageMap, "demo.Sample", "alpha", 10);

        coverage.Should().BeApproximately(75.0, 0.001);
    }

    // P3 <- lookupCoverageFallsBackToNearestLineWithinMethod.
    [Fact]
    public void LookupCoverageFallsBackToNearestLineWithinMethod()
    {
        Dictionary<string, CoverageData> coverageMap = new(StringComparer.Ordinal)
        {
            ["demo.Sample#alpha:10"] = new CoverageData(1, 3),
            ["demo.Sample#alpha:15"] = new CoverageData(0, 8),
        };

        double? coverage = CrapAnalyzer.LookupCoverage(coverageMap, "demo.Sample", "alpha", 13);

        coverage.Should().BeApproximately(100.0, 0.001);
    }

    // P4 <- nearestCoverageKeepsFirstEntryWhenDistancesTie. Insertion order (:10 then :14) is the
    // LinkedHashMap analog; strict '<' keeps the first-inserted entry when both are distance 2.
    [Fact]
    public void NearestCoverageKeepsFirstEntryWhenDistancesTie()
    {
        Dictionary<string, CoverageData> coverageMap = new(StringComparer.Ordinal)
        {
            ["demo.Sample#alpha:10"] = new CoverageData(1, 3),
            ["demo.Sample#alpha:14"] = new CoverageData(0, 8),
        };

        CoverageData? nearest = CrapAnalyzer.NearestCoverage(coverageMap, "demo.Sample", "alpha", 12);

        nearest.Should().NotBeNull();
        nearest!.CoveragePercent.Should().BeApproximately(75.0, 0.001);
    }

    // P5 <- lookupCoverageReturnsNullWhenMethodHasNoCoverageEntries.
    [Fact]
    public void LookupCoverageReturnsNullWhenNoCoverageEntries()
    {
        Dictionary<string, CoverageData> empty = new(StringComparer.Ordinal);

        double? coverage = CrapAnalyzer.LookupCoverage(empty, "demo.Sample", "alpha", 10);

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

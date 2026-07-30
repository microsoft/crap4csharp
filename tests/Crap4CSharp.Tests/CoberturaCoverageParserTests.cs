namespace Microsoft.Crap4CSharp.Tests;

using System.Xml;

// Faithful port of crap4java's JacocoCoverageParserTest, re-hosted on the Coverlet -> Cobertura format.
// The three Java tests are ported with identical intent and oracle values (90.0 / 0.0 percentages, the
// ":0" invalid-line key shape); the JaCoCo <counter type="INSTRUCTION"> mechanism is adapted to
// Cobertura per-method <line> counters (departure #4). Java's configuresSecureFactoryFeatures (which
// inspected DocumentBuilderFactory flags) is replaced by the behavioral XXE test DoesNotResolveExternal-
// Entities (D-T10a). The remaining tests pin the C#-only surface: the empty/null/missing/malformed edge
// cases, the compiler-generated/accessor/state-machine skip predicate (W-T9b/W-T10c), the frozen
// reciprocal NormalizeTypeName forms (D-table), and -- against REAL coverlet output regenerated outside
// this repo -- the nested/generic/async class-name normalization plus synthetic/accessor skipping (R3).
// Each test writes its fixture to a fresh temp file and always deletes it in a finally (mirrors the
// @TempDir pattern used by ChangedFileDetectorTests). Test method names are PascalCase (CA1707/C2).
public class CoberturaCoverageParserTests
{
    [Fact]
    public void ParsesCoverageByClassAndMethod()
    {
        // Alpha: 9 covered (lines 10-18) + 1 missed (line 19) -> 90%. Beta: 1 missed (line 20) -> 0%.
        // The class-level <lines> aggregate duplicates line 10 to prove it is never double-counted.
        string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="Demo.Sample" filename="Sample.cs">
                      <methods>
                        <method name="Alpha" signature="()">
                          <lines>
                            <line number="10" hits="1" />
                            <line number="11" hits="1" />
                            <line number="12" hits="1" />
                            <line number="13" hits="1" />
                            <line number="14" hits="1" />
                            <line number="15" hits="1" />
                            <line number="16" hits="1" />
                            <line number="17" hits="1" />
                            <line number="18" hits="1" />
                            <line number="19" hits="0" />
                          </lines>
                        </method>
                        <method name="Beta" signature="()">
                          <lines>
                            <line number="20" hits="0" />
                          </lines>
                        </method>
                      </methods>
                      <lines>
                        <line number="10" hits="1" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempFile(xml, path =>
        {
            IReadOnlyDictionary<string, CoverageData> result = CoberturaCoverageParser.Parse(path);

            result["Demo.Sample#Alpha#Sample.cs:10"].CoveragePercent.Should().BeApproximately(90.0, 0.001);
            result["Demo.Sample#Beta#Sample.cs:20"].CoveragePercent.Should().BeApproximately(0.0, 0.001);
        });
    }

    [Fact]
    public void ParsesXmlWithDoctypeWithoutRequiringLocalDtdFile()
    {
        // A DOCTYPE naming an external DTD that does not exist on disk. DtdProcessing.Ignore tolerates
        // the DOCTYPE and XmlResolver = null never fetches coverage.dtd, so the body still parses.
        string xml = """
            <?xml version="1.0"?>
            <!DOCTYPE coverage SYSTEM "coverage.dtd">
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="Demo.Sample" filename="Sample.cs">
                      <methods>
                        <method name="Alpha" signature="()">
                          <lines>
                            <line number="10" hits="1" />
                            <line number="11" hits="1" />
                            <line number="12" hits="1" />
                            <line number="13" hits="1" />
                            <line number="14" hits="1" />
                            <line number="15" hits="1" />
                            <line number="16" hits="1" />
                            <line number="17" hits="1" />
                            <line number="18" hits="1" />
                            <line number="19" hits="0" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempFile(xml, path =>
        {
            IReadOnlyDictionary<string, CoverageData> result = CoberturaCoverageParser.Parse(path);

            result["Demo.Sample#Alpha#Sample.cs:10"].CoveragePercent.Should().BeApproximately(90.0, 0.001);
        });
    }

    [Fact]
    public void ParsesInvalidLineNumbersAsZero()
    {
        // The lowest line's number is non-numeric ("oops") and degrades to 0, becoming minChildLine ->
        // the key ends in ":0". Coverage is still 9 covered (oops + 11-18) + 1 missed (19) = 90%.
        string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="Demo.Sample" filename="Sample.cs">
                      <methods>
                        <method name="Alpha" signature="()">
                          <lines>
                            <line number="oops" hits="1" />
                            <line number="11" hits="1" />
                            <line number="12" hits="1" />
                            <line number="13" hits="1" />
                            <line number="14" hits="1" />
                            <line number="15" hits="1" />
                            <line number="16" hits="1" />
                            <line number="17" hits="1" />
                            <line number="18" hits="1" />
                            <line number="19" hits="0" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempFile(xml, path =>
        {
            IReadOnlyDictionary<string, CoverageData> result = CoberturaCoverageParser.Parse(path);

            result["Demo.Sample#Alpha#Sample.cs:0"].CoveragePercent.Should().BeApproximately(90.0, 0.001);
        });
    }

    [Fact]
    public void ReturnsEmptyMapForEmptyReport()
    {
        string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages />
            </coverage>
            """;

        WithTempFile(xml, path =>
        {
            IReadOnlyDictionary<string, CoverageData> result = CoberturaCoverageParser.Parse(path);

            result.Should().BeEmpty();
        });
    }

    [Fact]
    public void ReturnsEmptyMapForNullPath()
    {
        IReadOnlyDictionary<string, CoverageData> result = CoberturaCoverageParser.Parse(null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ReturnsEmptyMapForMissingFile()
    {
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cobertura.xml");

        IReadOnlyDictionary<string, CoverageData> result = CoberturaCoverageParser.Parse(missing);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ThrowsInvalidOperationExceptionForMalformedXml()
    {
        WithTempFile("<coverage><packages><class", path =>
        {
            Action act = () => CoberturaCoverageParser.Parse(path);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Cobertura*")
                .WithInnerException<XmlException>();
        });
    }

    [Fact]
    public void EmitsAccessorsAndSkipsConstructors()
    {
        // T26 (departure #14): the Rule-3 accessor skip is gone, so get_Value/set_Value now emit like
        // ordinary methods; only the .ctor constructor (Rule 3, formerly Rule 4) is still skipped. Real
        // and both accessors survive. (T23: an angle-bracket <M>b__/<M>g__ member is no longer a "skip";
        // it ATTRIBUTES to its source method and is proven by the attribution tests.)
        string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="Demo.Sample" filename="Sample.cs">
                      <methods>
                        <method name=".ctor" signature="()">
                          <lines><line number="5" hits="1" /></lines>
                        </method>
                        <method name="get_Value" signature="()">
                          <lines><line number="6" hits="1" /></lines>
                        </method>
                        <method name="set_Value" signature="()">
                          <lines><line number="7" hits="1" /></lines>
                        </method>
                        <method name="Real" signature="()">
                          <lines><line number="9" hits="1" /></lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempFile(xml, path =>
        {
            IReadOnlyDictionary<string, CoverageData> result = CoberturaCoverageParser.Parse(path);

            result.Should().HaveCount(3);
            result.Should().ContainKey("Demo.Sample#get_Value#Sample.cs:6")
                .WhoseValue.Should().Be(new CoverageData(0, 1));
            result.Should().ContainKey("Demo.Sample#set_Value#Sample.cs:7")
                .WhoseValue.Should().Be(new CoverageData(0, 1));
            result.Should().ContainKey("Demo.Sample#Real#Sample.cs:9")
                .WhoseValue.Should().Be(new CoverageData(0, 1));
            result.Keys.Should().NotContain(k => k.Contains(".ctor", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void EmitsAccessorOperatorFinalizerAndEventEntriesButSkipsConstructors()
    {
        // T26 (departure #14): with Rule 3 inverted, the accessor entries (get_/set_/add_/remove_) emit;
        // op_* and Finalize always flowed through (they matched no skip rule); only .ctor is still skipped.
        // Every method carries one covered line -> 100%, so all eight non-constructor entries appear.
        string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="Demo.Sample" filename="Sample.cs">
                      <methods>
                        <method name=".ctor" signature="()">
                          <lines><line number="5" hits="1" /></lines>
                        </method>
                        <method name="get_Value" signature="()">
                          <lines><line number="6" hits="1" /></lines>
                        </method>
                        <method name="set_Value" signature="()">
                          <lines><line number="7" hits="1" /></lines>
                        </method>
                        <method name="op_Addition" signature="()">
                          <lines><line number="8" hits="1" /></lines>
                        </method>
                        <method name="op_Implicit" signature="()">
                          <lines><line number="9" hits="1" /></lines>
                        </method>
                        <method name="Finalize" signature="()">
                          <lines><line number="10" hits="1" /></lines>
                        </method>
                        <method name="add_Changed" signature="()">
                          <lines><line number="11" hits="1" /></lines>
                        </method>
                        <method name="remove_Changed" signature="()">
                          <lines><line number="12" hits="1" /></lines>
                        </method>
                        <method name="Real" signature="()">
                          <lines><line number="13" hits="1" /></lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempFile(xml, path =>
        {
            IReadOnlyDictionary<string, CoverageData> result = CoberturaCoverageParser.Parse(path);

            result.Should().HaveCount(8);
            result.Should().ContainKey("Demo.Sample#get_Value#Sample.cs:6")
                .WhoseValue.Should().Be(new CoverageData(0, 1));
            result.Should().ContainKey("Demo.Sample#set_Value#Sample.cs:7")
                .WhoseValue.Should().Be(new CoverageData(0, 1));
            result.Should().ContainKey("Demo.Sample#op_Addition#Sample.cs:8")
                .WhoseValue.Should().Be(new CoverageData(0, 1));
            result.Should().ContainKey("Demo.Sample#op_Implicit#Sample.cs:9")
                .WhoseValue.Should().Be(new CoverageData(0, 1));
            result.Should().ContainKey("Demo.Sample#Finalize#Sample.cs:10")
                .WhoseValue.Should().Be(new CoverageData(0, 1));
            result.Should().ContainKey("Demo.Sample#add_Changed#Sample.cs:11")
                .WhoseValue.Should().Be(new CoverageData(0, 1));
            result.Should().ContainKey("Demo.Sample#remove_Changed#Sample.cs:12")
                .WhoseValue.Should().Be(new CoverageData(0, 1));
            result.Should().ContainKey("Demo.Sample#Real#Sample.cs:13")
                .WhoseValue.Should().Be(new CoverageData(0, 1));
            result.Keys.Should().NotContain(k => k.Contains(".ctor", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void AttributesStateMachineMoveNextToSourceMethodAndKeepsRealMoveNext()
    {
        // The synthetic state-machine class Demo.C+<DoAsync>d__0 is intercepted at class level (R3/T21):
        // its MoveNext body-coverage is RE-ATTRIBUTED to the source method DoAsync, while the synthetic
        // SetStateMachine sibling is dropped (D-T21a: MoveNext only). A user-authored MoveNext on the real
        // class Demo.Enumerator is still kept verbatim (W-T10c: no blanket MoveNext name-skip).
        string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="Demo.C+&lt;DoAsync&gt;d__0" filename="C.cs">
                      <methods>
                        <method name="MoveNext" signature="()">
                          <lines><line number="12" hits="1" /></lines>
                        </method>
                        <method name="SetStateMachine" signature="()">
                          <lines><line number="13" hits="1" /></lines>
                        </method>
                      </methods>
                    </class>
                    <class name="Demo.Enumerator" filename="Enumerator.cs">
                      <methods>
                        <method name="MoveNext" signature="()">
                          <lines><line number="20" hits="1" /></lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempFile(xml, path =>
        {
            IReadOnlyDictionary<string, CoverageData> result = CoberturaCoverageParser.Parse(path);

            result.Should().ContainKey("Demo.C#DoAsync#C.cs:12").WhoseValue.Should().Be(new CoverageData(0, 1));
            result.Should().ContainKey("Demo.Enumerator#MoveNext#Enumerator.cs:20");
            result.Should().HaveCount(2);
            result.Keys.Should().NotContain(k =>
                k.Contains("SetStateMachine", StringComparison.Ordinal)
                || k.Contains("d__0", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void MatchesRealCoverletSampleClassNames()
    {
        // VERBATIM class @name / method @name / <line> strings emitted by a real `dotnet test
        // --collect:"XPlat Code Coverage"` run (regenerated outside this repo, see the test-file note):
        //   * CrapScore.Calculate  -- the on-disk dev sample anchor (5 covered lines) -> 100%.
        //   * Sample.Outer/Inner            -> Sample.Outer.Inner              (nested '/' -> '.')
        //   * Sample.Outer`1/Inner`2        -> Sample.Outer`1.Inner`2          (generic arity kept)
        //   * Sample.Container`1            -- get_Item accessor emitted (T26), Count kept
        //   * Sample.Worker                -- get_Prop accessor emitted (T26), UseLambda (captured lambda
        //                                     inlined) kept as 10 covered lines
        //   * Sample.Worker/<DoAsync>d__4  -- async state machine; its MoveNext body-coverage is
        //                                     RE-ATTRIBUTED to the source method DoAsync (R3/T21).
        // This pins the frozen reciprocal AND the skip predicate against genuine coverlet output (R3).
        string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <coverage>
              <packages>
                <package name="Real">
                  <classes>
                    <class name="Microsoft.Crap4CSharp.CrapScore" filename="CrapScore.cs" line-rate="1" branch-rate="1" complexity="2"><methods><method name="Calculate" signature="(System.Int32,System.Nullable`1&lt;System.Double&gt;)" line-rate="1" branch-rate="1" complexity="2"><lines><line number="11" hits="4" branch="True" condition-coverage="100% (2/2)"><conditions><condition number="7" type="jump" coverage="100%" /></conditions></line><line number="13" hits="1" branch="False" /><line number="16" hits="3" branch="False" /><line number="17" hits="3" branch="False" /><line number="18" hits="3" branch="False" /></lines></method></methods><lines><line number="11" hits="4" branch="True" condition-coverage="100% (2/2)"><conditions><condition number="7" type="jump" coverage="100%" /></conditions></line><line number="13" hits="1" branch="False" /><line number="16" hits="3" branch="False" /><line number="17" hits="3" branch="False" /><line number="18" hits="3" branch="False" /></lines></class>
                    <class name="Sample.Outer/Inner" filename="Types.cs" line-rate="1" branch-rate="1" complexity="1">
                      <methods>
                        <method name="Value" signature="()" line-rate="1" branch-rate="1" complexity="1">
                          <lines>
                            <line number="9" hits="1" branch="False" />
                          </lines>
                        </method>
                      </methods>
                      <lines>
                        <line number="9" hits="1" branch="False" />
                      </lines>
                    </class>
                    <class name="Sample.Outer`1/Inner`2" filename="Types.cs" line-rate="1" branch-rate="1" complexity="1">
                      <methods>
                        <method name="Combine" signature="(U,V)" line-rate="1" branch-rate="1" complexity="1">
                          <lines>
                            <line number="17" hits="1" branch="False" />
                          </lines>
                        </method>
                      </methods>
                      <lines>
                        <line number="17" hits="1" branch="False" />
                      </lines>
                    </class>
                    <class name="Sample.Container`1" filename="Types.cs" line-rate="1" branch-rate="1" complexity="2">
                      <methods>
                        <method name="get_Item" signature="()" line-rate="1" branch-rate="1" complexity="1">
                          <lines>
                            <line number="23" hits="2" branch="False" />
                          </lines>
                        </method>
                        <method name="Count" signature="(T[])" line-rate="1" branch-rate="1" complexity="1">
                          <lines>
                            <line number="25" hits="1" branch="False" />
                          </lines>
                        </method>
                      </methods>
                      <lines>
                        <line number="23" hits="2" branch="False" />
                        <line number="25" hits="1" branch="False" />
                      </lines>
                    </class>
                    <class name="Sample.Worker" filename="Types.cs" line-rate="1" branch-rate="1" complexity="3">
                      <methods>
                        <method name="get_Prop" signature="()" line-rate="1" branch-rate="1" complexity="1">
                          <lines>
                            <line number="30" hits="2" branch="False" />
                          </lines>
                        </method>
                        <method name="UseLambda" signature="(System.Int32[])" line-rate="1" branch-rate="1" complexity="2">
                          <lines>
                            <line number="39" hits="1" branch="False" />
                            <line number="40" hits="1" branch="False" />
                            <line number="41" hits="4" branch="False" />
                            <line number="42" hits="1" branch="False" />
                            <line number="43" hits="9" branch="True" condition-coverage="100% (2/2)">
                              <conditions>
                                <condition number="67" type="jump" coverage="100%" />
                              </conditions>
                            </line>
                            <line number="44" hits="3" branch="False" />
                            <line number="45" hits="3" branch="False" />
                            <line number="46" hits="3" branch="False" />
                            <line number="48" hits="1" branch="False" />
                            <line number="49" hits="1" branch="False" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                    <class name="Sample.Worker/&lt;DoAsync&gt;d__4" filename="Types.cs" line-rate="1" branch-rate="1" complexity="2">
                      <methods>
                        <method name="MoveNext" signature="()" line-rate="1" branch-rate="1" complexity="2">
                          <lines>
                            <line number="33" hits="1" branch="False" />
                            <line number="34" hits="1" branch="False" />
                            <line number="35" hits="1" branch="False" />
                            <line number="36" hits="1" branch="False" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempFile(xml, path =>
        {
            IReadOnlyDictionary<string, CoverageData> result = CoberturaCoverageParser.Parse(path);

            // Plain-FQN anchor: 5 covered lines, aggregate + <conditions> ignored -> 100%.
            result.Should().ContainKey("Microsoft.Crap4CSharp.CrapScore#Calculate#CrapScore.cs:11")
                .WhoseValue.Should().Be(new CoverageData(0, 5));

            // Frozen reciprocal against REAL coverlet '/'-separated + backtick-arity names.
            result.Should().ContainKey("Sample.Outer.Inner#Value#Types.cs:9")
                .WhoseValue.Should().Be(new CoverageData(0, 1));
            result.Should().ContainKey("Sample.Outer`1.Inner`2#Combine#Types.cs:17")
                .WhoseValue.Should().Be(new CoverageData(0, 1));
            result.Should().ContainKey("Sample.Container`1#Count#Types.cs:25")
                .WhoseValue.Should().Be(new CoverageData(0, 1));
            result.Should().ContainKey("Sample.Worker#UseLambda#Types.cs:39")
                .WhoseValue.Should().Be(new CoverageData(0, 10));

            // T26 (departure #14): the Rule-3 accessor skip is gone, so the get_Item/get_Prop accessors
            // now emit like ordinary methods (each a single covered line -> 100%).
            result.Should().ContainKey("Sample.Container`1#get_Item#Types.cs:23")
                .WhoseValue.Should().Be(new CoverageData(0, 1));
            result.Should().ContainKey("Sample.Worker#get_Prop#Types.cs:30")
                .WhoseValue.Should().Be(new CoverageData(0, 1));

            // R3/T21: the async state machine's MoveNext (lines 33-36, all hit) is attributed to DoAsync.
            result.Should().ContainKey("Sample.Worker#DoAsync#Types.cs:33")
                .WhoseValue.Should().Be(new CoverageData(0, 4));

            // The synthetic state-machine class and the constructor are still absent (T26 removed only
            // the accessor skip; the <...> state-machine/display-class routing and .ctor skip stand).
            result.Keys.Should().NotContain(k =>
                k.Contains('<')
                || k.Contains('>')
                || k.Contains("d__", StringComparison.Ordinal)
                || k.Contains("MoveNext", StringComparison.Ordinal)
                || k.Contains(".ctor", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void AttributesAsyncStateMachineCoverageToSourceMethod()
    {
        // An async state machine Sample.Runner/<DrainAsync>d__5: MoveNext carries the lowered user body
        // (lines 12-21), 7 hit + 3 unhit -> attributed to DrainAsync at min-line 12 as 70% coverage.
        string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="Sample.Runner/&lt;DrainAsync&gt;d__5" filename="Runner.cs">
                      <methods>
                        <method name="MoveNext" signature="()">
                          <lines>
                            <line number="12" hits="1" />
                            <line number="13" hits="1" />
                            <line number="14" hits="1" />
                            <line number="15" hits="1" />
                            <line number="16" hits="1" />
                            <line number="17" hits="1" />
                            <line number="18" hits="1" />
                            <line number="19" hits="0" />
                            <line number="20" hits="0" />
                            <line number="21" hits="0" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempFile(xml, path =>
        {
            IReadOnlyDictionary<string, CoverageData> result = CoberturaCoverageParser.Parse(path);

            result.Should().ContainKey("Sample.Runner#DrainAsync#Runner.cs:12")
                .WhoseValue.Should().Be(new CoverageData(3, 7));
        });
    }

    [Fact]
    public void AttributesIteratorStateMachineCoverageToSourceMethod()
    {
        // An iterator state machine Sample.Paths/<ResolveAbsolutePaths>d__5: MoveNext carries the lowered
        // user body (lines 40-49), 7 hit + 3 unhit -> attributed to ResolveAbsolutePaths at min-line 40.
        string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="Sample.Paths/&lt;ResolveAbsolutePaths&gt;d__5" filename="Paths.cs">
                      <methods>
                        <method name="MoveNext" signature="()">
                          <lines>
                            <line number="40" hits="1" />
                            <line number="41" hits="1" />
                            <line number="42" hits="1" />
                            <line number="43" hits="1" />
                            <line number="44" hits="1" />
                            <line number="45" hits="1" />
                            <line number="46" hits="1" />
                            <line number="47" hits="0" />
                            <line number="48" hits="0" />
                            <line number="49" hits="0" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempFile(xml, path =>
        {
            IReadOnlyDictionary<string, CoverageData> result = CoberturaCoverageParser.Parse(path);

            result.Should().ContainKey("Sample.Paths#ResolveAbsolutePaths#Paths.cs:40")
                .WhoseValue.Should().Be(new CoverageData(3, 7));
        });
    }

    [Fact]
    public void SplitPartialClassOverloadsGetDistinctPerFileKeys()
    {
        // A C# partial class split across A.cs + B.cs, each declaring an overload of Render whose lowered
        // <line> min-number OVERLAPS (both :6). Pre-T24 both mapped to "Demo.Widget#Render:6" and the
        // second-parsed entry overwrote the first; T24's #<basename> segregates them so BOTH survive.
        //   A.cs -> Render lines 6,7,8 all hit   -> CoverageData(0 missed, 3 covered)
        //   B.cs -> Render lines 6,8,10 all unhit -> CoverageData(3 missed, 0 covered)
        string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="Demo.Widget" filename="A.cs">
                      <methods>
                        <method name="Render" signature="(System.Int32)">
                          <lines>
                            <line number="6" hits="1" />
                            <line number="7" hits="1" />
                            <line number="8" hits="1" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                    <class name="Demo.Widget" filename="B.cs">
                      <methods>
                        <method name="Render" signature="(System.String)">
                          <lines>
                            <line number="6" hits="0" />
                            <line number="8" hits="0" />
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

        WithTempFile(xml, path =>
        {
            IReadOnlyDictionary<string, CoverageData> result = CoberturaCoverageParser.Parse(path);

            result.Should().HaveCount(2);
            result.Should().ContainKey("Demo.Widget#Render#A.cs:6").WhoseValue.Should().Be(new CoverageData(0, 3));
            result.Should().ContainKey("Demo.Widget#Render#B.cs:6").WhoseValue.Should().Be(new CoverageData(3, 0));
        });
    }

    [Fact]
    public void AttributesLambdaDisplayClassToSourceMethod()
    {
        // T23 (finding #3 INVERTED, sec.5): a capturing-lambda display class Sample.Worker/<>c__Display-
        // Class3_0 is routed to the B collector. Its <UseLambda>b__0_0 member (line 41, hit) demangles to
        // the source method UseLambda and, with no body/other synthetic to anchor to, CREATES the entry
        // Sample.Worker#UseLambda#Worker.cs:41. No angle-bracket / display-class / b__ name may leak.
        string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="Sample.Worker/&lt;&gt;c__DisplayClass3_0" filename="Worker.cs">
                      <methods>
                        <method name="&lt;UseLambda&gt;b__0_0" signature="(System.Int32)">
                          <lines>
                            <line number="41" hits="4" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempFile(xml, path =>
        {
            IReadOnlyDictionary<string, CoverageData> result = CoberturaCoverageParser.Parse(path);

            result.Should().HaveCount(1);
            result.Should().ContainKey("Sample.Worker#UseLambda#Worker.cs:41")
                .WhoseValue.Should().Be(new CoverageData(0, 1));
            result.Keys.Should().NotContain(k =>
                k.Contains('<')
                || k.Contains('>')
                || k.Contains("b__", StringComparison.Ordinal)
                || k.Contains("DisplayClass", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void AttributesStaticLambdaDisplayClassToSourceMethod()
    {
        // T23 (B, static-cache variant): a non-capturing lambda is emitted on the shared cache class
        // Sample.Worker/<>c (no DisplayClass suffix). Its <UseLambda>b__1_0 member (line 41, hit) still
        // demangles to UseLambda and CREATES Sample.Worker#UseLambda#Worker.cs:41.
        string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="Sample.Worker/&lt;&gt;c" filename="Worker.cs">
                      <methods>
                        <method name="&lt;UseLambda&gt;b__1_0" signature="(System.Int32)">
                          <lines>
                            <line number="41" hits="4" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempFile(xml, path =>
        {
            IReadOnlyDictionary<string, CoverageData> result = CoberturaCoverageParser.Parse(path);

            result.Should().HaveCount(1);
            result.Should().ContainKey("Sample.Worker#UseLambda#Worker.cs:41")
                .WhoseValue.Should().Be(new CoverageData(0, 1));
            result.Keys.Should().NotContain(k => k.Contains('<') || k.Contains('>'));
        });
    }

    [Fact]
    public void AttributesRealClassHostedLocalFunctionToSourceMethod()
    {
        // T23 (sec.1.C, INVOKED): a non-capturing local function is emitted DIRECTLY on the real class
        // Demo.Calc as <Compute>g__Validate|0_0. Its lines (8,9 hit) demangle to Compute and UNION with
        // Compute's own body (5,6 hit) into a single entry Demo.Calc#Compute#Calc.cs:5 == (0,4). No g__
        // key leaks.
        string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="Demo.Calc" filename="Calc.cs">
                      <methods>
                        <method name="Compute" signature="(System.Int32)">
                          <lines>
                            <line number="5" hits="1" />
                            <line number="6" hits="1" />
                          </lines>
                        </method>
                        <method name="&lt;Compute&gt;g__Validate|0_0" signature="(System.Int32)">
                          <lines>
                            <line number="8" hits="1" />
                            <line number="9" hits="1" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempFile(xml, path =>
        {
            IReadOnlyDictionary<string, CoverageData> result = CoberturaCoverageParser.Parse(path);

            result.Should().HaveCount(1);
            result.Should().ContainKey("Demo.Calc#Compute#Calc.cs:5")
                .WhoseValue.Should().Be(new CoverageData(0, 4));
            result.Keys.Should().NotContain(k =>
                k.Contains("g__", StringComparison.Ordinal) || k.Contains('<') || k.Contains('>'));
        });
    }

    [Fact]
    public void AttributesNeverInvokedLocalFunctionAsZeroCoverage()
    {
        // T23 (finding #3, parser). A never-invoked local function <Compute>g__Validate|0_0 (lines 8,9,10
        // ALL unhit) with NO Compute body CREATES Demo.Calc#Compute#Calc.cs:8 == (3,0) -> a real 0% entry
        // (pre-T23 this class was skipped wholesale -> empty map / N/A -> the false pass).
        string zeroXml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="Demo.Calc" filename="Calc.cs">
                      <methods>
                        <method name="&lt;Compute&gt;g__Validate|0_0" signature="(System.Int32)">
                          <lines>
                            <line number="8" hits="0" />
                            <line number="9" hits="0" />
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

        WithTempFile(zeroXml, path =>
        {
            IReadOnlyDictionary<string, CoverageData> result = CoberturaCoverageParser.Parse(path);

            result.Should().HaveCount(1);
            result.Should().ContainKey("Demo.Calc#Compute#Calc.cs:8")
                .WhoseValue.Should().Be(new CoverageData(3, 0));
        });

        // Variant (body + dead local fn union): a covered body (line 5) unioned with the dead local fn
        // (lines 8,9 unhit) -> Demo.Calc#Compute#Calc.cs:5 == (2,1) -> 33.3% (was 100% pre-T23).
        string unionXml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="Demo.Calc" filename="Calc.cs">
                      <methods>
                        <method name="Compute" signature="(System.Int32)">
                          <lines>
                            <line number="5" hits="1" />
                          </lines>
                        </method>
                        <method name="&lt;Compute&gt;g__Validate|0_0" signature="(System.Int32)">
                          <lines>
                            <line number="8" hits="0" />
                            <line number="9" hits="0" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempFile(unionXml, path =>
        {
            IReadOnlyDictionary<string, CoverageData> result = CoberturaCoverageParser.Parse(path);

            result.Should().HaveCount(1);
            CoverageData merged = result["Demo.Calc#Compute#Calc.cs:5"];
            merged.Should().Be(new CoverageData(2, 1));
            merged.CoveragePercent.Should().BeApproximately(33.333, 0.001);
        });
    }

    [Fact]
    public void AttributesAsyncLocalFunctionToSourceMethod()
    {
        // T23 (A1): an async local function lowers to a d__ machine nested UNDER its source method's
        // demangled name -- Sample.Runner/<<Run>g__Drain|0_0>d__1. Its MoveNext (lines 15,16 hit; 17,18,19
        // unhit -> covered 2, missed 3) attributes to the OUTER method Run and UNIONs with Run's own body
        // (lines 10,11 hit) -> Sample.Runner#Run#Runner.cs:10 == (3,4). No d__/g__ key leaks.
        string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="Sample.Runner" filename="Runner.cs">
                      <methods>
                        <method name="Run" signature="()">
                          <lines>
                            <line number="10" hits="1" />
                            <line number="11" hits="1" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                    <class name="Sample.Runner/&lt;&lt;Run&gt;g__Drain|0_0&gt;d__1" filename="Runner.cs">
                      <methods>
                        <method name="MoveNext" signature="()">
                          <lines>
                            <line number="15" hits="1" />
                            <line number="16" hits="1" />
                            <line number="17" hits="0" />
                            <line number="18" hits="0" />
                            <line number="19" hits="0" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempFile(xml, path =>
        {
            IReadOnlyDictionary<string, CoverageData> result = CoberturaCoverageParser.Parse(path);

            result.Should().HaveCount(1);
            result.Should().ContainKey("Sample.Runner#Run#Runner.cs:10")
                .WhoseValue.Should().Be(new CoverageData(3, 4));
            result.Keys.Should().NotContain(k =>
                k.Contains("d__", StringComparison.Ordinal)
                || k.Contains("g__", StringComparison.Ordinal)
                || k.Contains('<') || k.Contains('>'));
        });
    }

    [Fact]
    public void MergesMultipleSyntheticMembersIntoOneSourceMethodEntry()
    {
        // T23 (union/nested): a method Foo with a body (line 5 hit), a real-class-hosted local function
        // <Foo>g__Local|0_0 (line 8 hit) AND a display-class lambda Demo.Box/<>c #<Foo>b__0_1 (line 9 hit)
        // -- three DISJOINT-line coverlet members -- all merge into ONE entry Demo.Box#Foo#Box.cs:5 ==
        // (0,3). Proves the faithful disjoint-line union across all three synthetic shapes.
        string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="Demo.Box" filename="Box.cs">
                      <methods>
                        <method name="Foo" signature="()">
                          <lines>
                            <line number="5" hits="1" />
                          </lines>
                        </method>
                        <method name="&lt;Foo&gt;g__Local|0_0" signature="()">
                          <lines>
                            <line number="8" hits="1" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                    <class name="Demo.Box/&lt;&gt;c" filename="Box.cs">
                      <methods>
                        <method name="&lt;Foo&gt;b__0_1" signature="()">
                          <lines>
                            <line number="9" hits="1" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;

        WithTempFile(xml, path =>
        {
            IReadOnlyDictionary<string, CoverageData> result = CoberturaCoverageParser.Parse(path);

            result.Should().HaveCount(1);
            result.Should().ContainKey("Demo.Box#Foo#Box.cs:5")
                .WhoseValue.Should().Be(new CoverageData(0, 3));
            result.Keys.Should().NotContain(k =>
                k.Contains("g__", StringComparison.Ordinal)
                || k.Contains("b__", StringComparison.Ordinal)
                || k.Contains('<') || k.Contains('>'));
        });
    }

    [Fact]
    public void MergesSyntheticIntoNearestOverloadNotSibling()
    {
        // T23 (overload safety, C6 family): two same-file overloads of M (bodies @10 and @30, each hit) and
        // one uncovered lambda Demo.Calc/<>c__DisplayClass #<M>b__ (line 12, unhit). The nearest-body merge
        // folds the lambda into the NEAREST overload (:10 -> (1,1)); the far overload :30 stays UNCHANGED
        // (0,1). A global union would blend both and could over-report -> this proves it does not.
        string xml = """
            <?xml version="1.0"?>
            <coverage>
              <packages>
                <package name="demo">
                  <classes>
                    <class name="Demo.Calc" filename="Calc.cs">
                      <methods>
                        <method name="M" signature="(System.Int32)">
                          <lines>
                            <line number="10" hits="1" />
                          </lines>
                        </method>
                        <method name="M" signature="(System.String)">
                          <lines>
                            <line number="30" hits="1" />
                          </lines>
                        </method>
                      </methods>
                    </class>
                    <class name="Demo.Calc/&lt;&gt;c__DisplayClass0_0" filename="Calc.cs">
                      <methods>
                        <method name="&lt;M&gt;b__0" signature="()">
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

        WithTempFile(xml, path =>
        {
            IReadOnlyDictionary<string, CoverageData> result = CoberturaCoverageParser.Parse(path);

            result.Should().HaveCount(2);
            result.Should().ContainKey("Demo.Calc#M#Calc.cs:10")
                .WhoseValue.Should().Be(new CoverageData(1, 1));
            result.Should().ContainKey("Demo.Calc#M#Calc.cs:30")
                .WhoseValue.Should().Be(new CoverageData(0, 1));
        });
    }

    [Fact]
    public void NormalizeTypeNameProducesFrozenReciprocalForm()
    {
        CoberturaCoverageParser.NormalizeTypeName("Demo.Sample").Should().Be("Demo.Sample");
        CoberturaCoverageParser.NormalizeTypeName("Demo.Outer+Inner").Should().Be("Demo.Outer.Inner");
        CoberturaCoverageParser.NormalizeTypeName("Demo.Outer/Inner").Should().Be("Demo.Outer.Inner");
        CoberturaCoverageParser.NormalizeTypeName("Demo.Container`1").Should().Be("Demo.Container`1");
        CoberturaCoverageParser.NormalizeTypeName("Demo.Outer`1+Inner`2").Should().Be("Demo.Outer`1.Inner`2");
        CoberturaCoverageParser.NormalizeTypeName("Sample").Should().Be("Sample");
    }

    [Fact]
    public void DoesNotResolveExternalEntities()
    {
        // Behavioral XXE proof (D-T10a, replaces Java's configuresSecureFactoryFeatures). A canary file
        // is referenced through an internal-subset external entity via file://; the parse must fail
        // WITHOUT ever reading the canary into the document.
        string canaryPath = Path.Combine(Path.GetTempPath(), "canary-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(canaryPath, "SECRET-CANARY-" + Guid.NewGuid().ToString("N"));
        string canaryUri = new Uri(canaryPath).AbsoluteUri;
        string maliciousPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cobertura.xml");

        string xml = $"""
            <?xml version="1.0"?>
            <!DOCTYPE coverage [ <!ENTITY xxe SYSTEM "{canaryUri}"> ]>
            <coverage><packages><package name="p"><classes>
              <class name="Demo.Leak&xxe;" filename="x.cs"><methods>
                <method name="M" signature="()"><lines><line number="1" hits="1" /></lines></method>
              </methods></class>
            </classes></package></packages></coverage>
            """;
        File.WriteAllText(maliciousPath, xml);

        try
        {
            Action act = () => CoberturaCoverageParser.Parse(maliciousPath);

            act.Should().Throw<InvalidOperationException>()
                .Which.ToString().Should().NotContain("SECRET-CANARY");
        }
        finally
        {
            if (File.Exists(canaryPath))
            {
                File.Delete(canaryPath);
            }

            if (File.Exists(maliciousPath))
            {
                File.Delete(maliciousPath);
            }
        }
    }

    private static void WithTempFile(string content, Action<string> test)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cobertura.xml");
        File.WriteAllText(path, content);
        try
        {
            test(path);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

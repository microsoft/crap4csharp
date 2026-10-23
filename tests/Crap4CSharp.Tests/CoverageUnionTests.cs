namespace Microsoft.Crap4CSharp.Tests;

using System.Globalization;
using System.Xml.Linq;

public class CoverageUnionTests
{
    [Fact]
    public void ComplementaryHitsAreLineUnionNotAverageAndDuplicatesAreIdempotent()
    {
        string first = Report(Member("Demo.C", "C.cs", "M", "()", (10, 1), (20, 0)));
        string second = Report(Member("Demo.C", "C.cs", "M", "()", (10, 0), (20, 1)));
        WithReports(
        paths =>
        {
            CoberturaCoverageParser.Parse(paths[0]).Values.Single().CoveragePercent.Should().Be(50);
            CoberturaCoverageParser.Parse(paths[1]).Values.Single().CoveragePercent.Should().Be(50);
            var combined = CoberturaCoverageParser.ParseReports(paths);
            combined.Should().ContainSingle().Which.Value.Should().Be(new CoverageData(0, 2));
            CoberturaCoverageParser.ParseReports([paths[1], paths[0], paths[1]])
                .Should().BeEquivalentTo(combined);
        },
        first,
        second);
    }

    [Fact]
    public void DifferentObservedDenominatorsRetainLinesAndCanonicalMinimum()
    {
        WithReports(
        paths =>
        {
            var forward = CoberturaCoverageParser.ParseReports(paths);
            forward.Should().ContainSingle().Which.Should().Be(
                new KeyValuePair<string, CoverageData>("Demo.C#M#C.cs:10", new(1, 2)));
            CoberturaCoverageParser.ParseReports([paths[1], paths[0]]).Should().BeEquivalentTo(forward);
        },
        Report(Member("Demo.C", "C.cs", "M", "()", (20, 0), (30, 0))),
        Report(Member("Demo.C", "C.cs", "M", "()", (10, 1), (20, 1))));
    }

    [Theory]
    [InlineData("Demo.C", "<M>g__Local|0_0")]
    [InlineData("Demo.C+<>c", "<M>b__0_0")]
    [InlineData("Demo.C+<>c__DisplayClass0_0", "<M>b__0")]
    [InlineData("Demo.C+<<M>g__Local|0_0>d__1", "MoveNext")]
    [InlineData("Demo.C+<<M>b__0_0>d__1", "MoveNext")]
    public void SyntheticOnlyFirstReportAnchorsToLaterBodyBeforeFolding(string type, string method)
    {
        WithReports(
        paths =>
        {
            var combined = CoberturaCoverageParser.ParseReports(paths);
            combined.Should().ContainSingle().Which.Should().Be(
                new KeyValuePair<string, CoverageData>("Demo.C#M#C.cs:10", new(0, 2)));
            CoberturaCoverageParser.ParseReports([paths[1], paths[0], paths[0]])
                .Should().BeEquivalentTo(combined);
        },
        Report(Member(type, "C.cs", method, "()", (20, 1))),
        Report(Member("Demo.C", "C.cs", "M", "()", (10, 1), (20, 0))));
    }

    [Fact]
    public void SyntheticOnlyReportsUseStableMinimumAndUnionSharedLines()
    {
        WithReports(
        paths =>
        {
            var forward = CoberturaCoverageParser.ParseReports(paths);
            forward.Should().ContainSingle().Which.Should().Be(
                new KeyValuePair<string, CoverageData>("Demo.C#M#C.cs:20", new(0, 2)));
            CoberturaCoverageParser.ParseReports([paths[1], paths[0]]).Should().BeEquivalentTo(forward);
        },
        Report(Member("Demo.C", "C.cs", "<M>g__L|0_0", "()", (30, 1))),
        Report(Member("Demo.C+<>c", "C.cs", "<M>b__0_0", "()", (20, 1), (30, 0))));
    }

    [Fact]
    public void OverloadsPartialsAndNestedGenericsStaySegregated()
    {
        WithReports(
        paths =>
        {
            var result = CoberturaCoverageParser.ParseReports(paths);
            result.Should().HaveCount(3);
            result["Demo.Outer`1.Inner`1#M#One.cs:10"].Should().Be(new CoverageData(0, 2));
            result["Demo.Outer`1.Inner`1#M#One.cs:100"].Should().Be(new CoverageData(2, 0));
            result["Demo.Outer`1.Inner`1#M#Two.cs:10"].Should().Be(new CoverageData(1, 0));
            CoberturaCoverageParser.ParseReports([paths[1], paths[0]]).Should().BeEquivalentTo(result);
        },
        Report(
            Member("Demo.Outer`1/Inner`1", "One.cs", "<M>g__Local|0_0", "()", (20, 1)),
            Member("Demo.Outer`1/Inner`1", "One.cs", "<M>g__Other|1_0", "()", (110, 0)),
            Member("Demo.Outer`1/Inner`1", "Two.cs", "M", "()", (10, 0))),
        Report(
            Member("Demo.Outer`1+Inner`1", "One.cs", "M", "()", (10, 1)),
            Member("Demo.Outer`1+Inner`1", "One.cs", "M", "(System.Int32)", (100, 0))));
    }

    [Theory]
    [InlineData("Demo.C+<M>d__1")]
    [InlineData("Demo.C/<M>d__1")]
    public void PlainAsyncAndIteratorMoveNextLinesUnionWithoutHelperNoise(string secondType)
    {
        WithReports(
        paths =>
        {
            var result = CoberturaCoverageParser.ParseReports(paths);
            result.Should().ContainSingle().Which.Should().Be(
                new KeyValuePair<string, CoverageData>("Demo.C#M#C.cs:10", new(0, 2)));
        },
        Report(Member("Demo.C+<M>d__1", "C.cs", "MoveNext", "()", (10, 0), (20, 1))),
        Report(
            Member(secondType, "C.cs", "MoveNext", "()", (10, 1), (20, 0)),
            Member("Demo.C+<M>d__1", "C.cs", "Dispose", "()", (30, 0))));
    }

    [Fact]
    public void MoveNextNormalizedIdentityAlignsDifferentMinimumLines()
    {
        WithReports(
        paths =>
        {
            var result = CoberturaCoverageParser.ParseReports(paths);
            result.Should().ContainSingle().Which.Should().Be(
                new KeyValuePair<string, CoverageData>("Demo.C#M#C.cs:10", new(0, 3)));
            CoberturaCoverageParser.ParseReports([paths[1], paths[0]]).Should().BeEquivalentTo(result);
        },
        Report(Member("Demo.C+<M>d__1", "C.cs", "MoveNext", "()", (20, 0), (30, 1))),
        Report(Member("Demo.C/<M>d__1", "C.cs", "MoveNext", "()", (10, 1), (20, 1))));
    }

    [Fact]
    public void ZeroCoverageAndAbsentMembersAreNotErasedOrInvented()
    {
        WithReports(
        paths =>
        {
            var map = CoberturaCoverageParser.ParseReports(paths);
            map["Demo.C#M#C.cs:10"].Should().Be(new CoverageData(1, 0));
            CrapAnalyzer.LookupCoverage(map, "Demo.C", "Missing", "C.cs", 1).Should().BeNull();
            map.Should().HaveCount(2);
        },
        Report(Member("Demo.C", "C.cs", "M", "()", (10, 0))),
        Report(Member("Other.C", "Other.cs", "N", "()", (10, 1))));
    }

    [Theory]
    [InlineData("<coverage/>")]
    [InlineData("<coverage><class><lines><line number='1' hits='1'/></lines></class></coverage>")]
    public void GoodReportCannotMaskAnUnusableReport(string empty)
    {
        WithReports(
        paths =>
        {
            CoberturaCoverageParser.ParseReports(paths).Should().BeEmpty();
            CoberturaCoverageParser.ParseReports([paths[1], paths[0]]).Should().BeEmpty();
        },
        Report(Member("Demo.C", "C.cs", "M", "()", (10, 1))),
        empty);
    }

    [Fact]
    public void MissingOrMalformedReportCannotBeMasked()
    {
        WithReports(
        paths =>
        {
            FluentActions.Invoking(() => CoberturaCoverageParser.ParseReports(paths))
                .Should().Throw<InvalidOperationException>();
            File.Delete(paths[1]);
            FluentActions.Invoking(() => CoberturaCoverageParser.ParseReports(paths))
                .Should().Throw<InvalidOperationException>();
        },
        Report(Member("Demo.C", "C.cs", "M", "()", (10, 1))),
        "not xml");
    }

    [Fact]
    public void UnreadableReportCannotBeMasked()
    {
        string xml = Report(Member("Demo.C", "C.cs", "M", "()", (10, 1)));
        WithReports(
        paths =>
        {
            using FileStream locked = new(paths[1], FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            FluentActions.Invoking(() => CoberturaCoverageParser.ParseReports(paths))
                .Should().Throw<InvalidOperationException>();
        },
        xml,
        xml);
    }

    [Fact]
    public void RealAccessorCoverageWinsBeforeBlindZeroPromotion()
    {
        WithReports(
        paths =>
        {
            string source = Path.Combine(Path.GetDirectoryName(paths[0])!, "C.cs");
            File.WriteAllText(source, "namespace Demo; class C { int _v; int P { get => _v; set { if(value > 0) _v = value; } } }");
            var map = CoberturaCoverageParser.ParseReports(paths);
            var metrics = CrapAnalyzer.AnalyzeCoverage([source], map);
            metrics.Single(metric => metric.MethodName == "set_P").CoveragePercent.Should().Be(100);
            var missing = CrapAnalyzer.AnalyzeCoverage([source], CoberturaCoverageParser.Parse(paths[0]));
            missing.Single(metric => metric.MethodName == "set_P").CoveragePercent.Should().Be(0);
        },
        Report(Member("Demo.C", "C.cs", "get_P", "()", (1, 1))),
        Report(Member("Demo.C", "C.cs", "set_P", "(System.Int32)", (1, 1))));
    }

    private static string Report(params string[] classes) => "<coverage>" + string.Concat(classes) + "</coverage>";

    private static string Member(string type, string file, string method, string signature, params (int Number, int Hits)[] lines) =>
        new XElement(
            "class",
            new XAttribute("name", type),
            new XAttribute("filename", file),
            new XElement(
                "methods",
                new XElement(
                    "method",
                    new XAttribute("name", method),
                    new XAttribute("signature", signature),
                    new XElement("lines", lines.Select(line =>
                        new XElement(
                            "line",
                            new XAttribute("number", line.Number.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("hits", line.Hits.ToString(CultureInfo.InvariantCulture)))))))).ToString();

    private static void WithReports(Action<string[]> action, params string[] xml)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string[] paths = [.. xml.Select((content, index) =>
            {
                string path = Path.Combine(root, index.ToString(CultureInfo.InvariantCulture) + ".xml");
                File.WriteAllText(path, content);
                return path;
            })];
            action(paths);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

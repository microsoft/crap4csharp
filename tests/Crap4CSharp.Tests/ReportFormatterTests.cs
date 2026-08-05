namespace Microsoft.Crap4CSharp.Tests;

using System.Globalization;

public class ReportFormatterTests
{
    [Fact]
    public void FormatsExactReportWithScoresAndNaValues()
    {
        var scored = new MethodMetrics("foo", "demo.Sample", 3, 85.0, 4.5);
        var unknown = new MethodMetrics("bar", "demo.Sample", 2, null, null);

        string report = ReportFormatter.Format([scored, unknown]);

        // Build the expected report exactly as crap4java's ReportFormatterTest does: derive the header
        // and separator via the same format string, then assemble the six lines. Every terminator is an
        // explicit '\n' (the approved determinism departure) so this must NOT be a raw/verbatim string
        // literal -- this repo is CRLF, which would embed '\r\n' and mismatch the '\n' output.
        string header = string.Format(
            CultureInfo.InvariantCulture,
            "{0,-30} {1,-35} {2,4} {3,7} {4,8}",
            "Method",
            "Class",
            "CC",
            "Cov%",
            "CRAP");
        string separator = new string('-', header.Length);
        string expected =
            "CRAP Report\n" +
            "===========\n" +
            header + "\n" +
            separator + "\n" +
            string.Format(
                CultureInfo.InvariantCulture,
                "{0,-30} {1,-35} {2,4} {3,7} {4,8}\n",
                "foo",
                "demo.Sample",
                3,
                "85.0%",
                "4.5") +
            string.Format(
                CultureInfo.InvariantCulture,
                "{0,-30} {1,-35} {2,4} {3,7} {4,8}\n",
                "bar",
                "demo.Sample",
                2,
                "  N/A ",
                "     N/A");

        report.Should().Be(expected);
    }

    [Fact]
    public void SortsScoredEntriesAheadOfNaEntriesAndHigherScoresFirst()
    {
        var lowerScore = new MethodMetrics("low", "demo.Sample", 2, 100.0, 2.0);
        var unknown = new MethodMetrics("unknown", "demo.Sample", 2, null, null);
        var higherScore = new MethodMetrics("high", "demo.Sample", 5, 10.0, 9.0);

        string report = ReportFormatter.Format([lowerScore, unknown, higherScore]);

        report.IndexOf("high", StringComparison.Ordinal)
            .Should().BeLessThan(report.IndexOf("low", StringComparison.Ordinal));
        report.IndexOf("low", StringComparison.Ordinal)
            .Should().BeLessThan(report.IndexOf("unknown", StringComparison.Ordinal));
    }

    [Fact]
    public void PreservesInputOrderAmongEqualKeyedEntries()
    {
        // Beyond-parity guard (guardrail #8): pins the STABLE sort -- equal-keyed entries must keep
        // their input order. NOTE: .NET's List<T>.Sort uses a *stable* insertion sort for <=16 items,
        // so a handful of tied entries would NOT expose an unstable regression; List<T>.Sort only
        // diverges once its introspective quicksort path engages (>16 items). We therefore use 17 tied
        // N/A entries (verified to reorder under List<T>.Sort) in a deliberately non-alphabetical input
        // order, so this guard actually fails if the LINQ OrderBy/ThenBy is regressed to List<T>.Sort.

        // Same-CRAP-score tie: both keys equal (CC=5 fully covered => CRAP=CC=5.0). Input "ttt","kkk".
        var scoredFirst = new MethodMetrics("ttt", "demo.Sample", 5, 100.0, 5.0);
        var scoredSecond = new MethodMetrics("kkk", "demo.Sample", 5, 100.0, 5.0);

        // Full tie: every entry is N/A (CrapScore null => primary key true, secondary 0.0). Scrambled.
        string[] naOrder =
        [
            "z07", "z00", "z14", "z03", "z11", "z05", "z16", "z01", "z09",
            "z13", "z02", "z15", "z06", "z12", "z04", "z10", "z08",
        ];

        MethodMetrics[] entries =
        [
            scoredFirst,
            scoredSecond,
            .. naOrder.Select(name => new MethodMetrics(name, "demo.Sample", 3, null, null)),
        ];

        string report = ReportFormatter.Format(entries);

        // Same-score tie: "ttt" must stay ahead of "kkk" (not reordered, not alphabetized).
        report.IndexOf("ttt", StringComparison.Ordinal)
            .Should().BeLessThan(report.IndexOf("kkk", StringComparison.Ordinal));

        // Full N/A tie: the report must list the entries in exactly the (non-alphabetical) input order,
        // i.e. each IndexOf strictly increases. An unstable sort would break this monotonic chain.
        int previousIndex = -1;
        foreach (string name in naOrder)
        {
            int index = report.IndexOf(name, StringComparison.Ordinal);
            index.Should().BeGreaterThan(previousIndex);
            previousIndex = index;
        }
    }
}

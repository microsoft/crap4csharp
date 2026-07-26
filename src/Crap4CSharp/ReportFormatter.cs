namespace Microsoft.Crap4CSharp;

using System.Globalization;
using System.Text;

// Faithful port of crap4java's ReportFormatter. Java's non-instantiable `final class` with a private
// ctor maps to the idiomatic C# `static class`. Renders the CRAP report: a title, a header row, a
// separator, then one row per method, sorted so scored methods (highest CRAP first) precede the N/A
// ones. Approved departures: every line terminator is an explicit '\n' and all formatting uses
// InvariantCulture, so the report is byte-for-byte deterministic across platforms and locales.
public static class ReportFormatter
{
    // Ports format(List<MethodMetrics>) exactly.
    public static string Format(IReadOnlyList<MethodMetrics> entries)
    {
        // Java's List.sort(Comparator.comparing(...).thenComparing(...)) is a STABLE sort; C#'s
        // List<T>.Sort is NOT, so use LINQ OrderBy/ThenBy (stable) to preserve input order among ties.
        // Scored entries (CrapScore not null -> false) sort before N/A (null -> true); among scored,
        // negating the score puts the highest CRAP first.
        List<MethodMetrics> sorted =
        [
            .. entries
                .OrderBy(e => e.CrapScore is null)
                .ThenBy(e => e.CrapScore is null ? 0.0 : -e.CrapScore.Value),
        ];

        string header = string.Format(
            CultureInfo.InvariantCulture,
            "{0,-30} {1,-35} {2,4} {3,7} {4,8}",
            "Method",
            "Class",
            "CC",
            "Cov%",
            "CRAP");
        string separator = new string('-', header.Length);

        StringBuilder builder = new();
        builder.Append("CRAP Report\n");
        builder.Append("===========\n");
        builder.Append(header).Append('\n');
        builder.Append(separator).Append('\n');

        foreach (MethodMetrics entry in sorted)
        {
            builder.Append(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-30} {1,-35} {2,4} {3,7} {4,8}\n",
                entry.MethodName,
                entry.ClassName,
                entry.Complexity,
                FormatCoverage(entry.CoveragePercent),
                FormatCrap(entry.CrapScore)));
        }

        return builder.ToString();
    }

    private static string FormatCoverage(double? coverage)
    {
        if (coverage is null)
        {
            return "  N/A ";
        }

        return string.Format(CultureInfo.InvariantCulture, "{0,5:F1}%", coverage.Value);
    }

    private static string FormatCrap(double? score)
    {
        if (score is null)
        {
            return "     N/A";
        }

        return string.Format(CultureInfo.InvariantCulture, "{0,8:F1}", score.Value);
    }
}

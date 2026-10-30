namespace Microsoft.Crap4CSharp;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

public static class SarifReportFormatter
{
    private const string RuleId = "CRAP001";
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Format(IReadOnlyList<MethodMetrics> metrics, string projectRoot)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(projectRoot);

        string root = Path.GetFullPath(projectRoot);
        var results = metrics
            .Where(metric => metric.CrapScore is double score && CliApplication.ThresholdExceeded(score))
            .Select(metric => new
            {
                Metric = metric,
                File = metric.File is null ? null : Path.GetRelativePath(root, Path.GetFullPath(metric.File, root))
                    .Replace(Path.DirectorySeparatorChar, '/'),
            })
            .OrderByDescending(member => member.Metric.CrapScore)
            .ThenBy(member => member.File, StringComparer.Ordinal)
            .ThenBy(member => member.Metric.StartLine)
            .ThenBy(member => member.Metric.EndLine)
            .ThenBy(member => member.Metric.ClassName + "." + member.Metric.MethodName, StringComparer.Ordinal)
            .ThenBy(member => member.Metric.Complexity)
            .ThenBy(member => member.Metric.CoveragePercent)
            .Select(member => Result(member.Metric, member.File));

        var run = new
        {
            tool = new
            {
                driver = new
                {
                    name = "crap4csharp",
                    informationUri = "https://github.com/microsoft/crap4csharp",
                    rules = new[]
                    {
                        new
                        {
                            id = RuleId,
                            shortDescription = new { text = "CRAP score exceeds 8.0" },
                            defaultConfiguration = new { level = "warning" },
                        },
                    },
                },
            },
            results,
        };
        Dictionary<string, object> document = new(StringComparer.Ordinal)
        {
            ["$schema"] = "https://docs.oasis-open.org/sarif/sarif/v2.1.0/os/schemas/sarif-schema-2.1.0.json",
            ["version"] = "2.1.0",
            ["runs"] = new[] { run },
        };
        return JsonSerializer.Serialize(document, Options) + "\n";
    }

    private static object Result(MethodMetrics metric, string? file)
    {
        string name = metric.ClassName + "." + metric.MethodName;
        return new
        {
            ruleId = RuleId,
            ruleIndex = 0,
            level = "warning",
            message = new
            {
                text = string.Format(CultureInfo.InvariantCulture, "{0} has CRAP score {1:G17}, exceeding 8.0.", name, metric.CrapScore),
            },
            locations = new[]
            {
                new
                {
                    physicalLocation = PhysicalLocation(metric, file),
                    logicalLocations = new[] { new { fullyQualifiedName = name, kind = "member" } },
                },
            },
            properties = new
            {
                complexity = metric.Complexity,
                coveragePercent = metric.CoveragePercent,
                crap = metric.CrapScore,
            },
        };
    }

    private static object? PhysicalLocation(MethodMetrics metric, string? file)
    {
        if (file is null)
        {
            return null;
        }

        bool absolute = Path.IsPathRooted(file);
        string uri = absolute
            ? new Uri(file).AbsoluteUri
            : string.Join("/", file.Split('/').Select(Uri.EscapeDataString));
        return new
        {
            artifactLocation = new { uri, uriBaseId = absolute ? null : "%SRCROOT%" },
            region = metric.StartLine is > 0
                ? new { startLine = metric.StartLine, endLine = metric.EndLine >= metric.StartLine ? metric.EndLine : null }
                : null,
        };
    }
}

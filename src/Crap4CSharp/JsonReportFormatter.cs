namespace Microsoft.Crap4CSharp;

using System.Text.Json;

public static class JsonReportFormatter
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Format(IReadOnlyList<MethodMetrics> metrics, string projectRoot)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(projectRoot);

        string root = Path.GetFullPath(projectRoot);
        var members = metrics.Select(metric => new
        {
            name = metric.ClassName + "." + metric.MethodName,
            file = metric.File is null ? null : Path.GetRelativePath(root, Path.GetFullPath(metric.File, root))
                .Replace(Path.DirectorySeparatorChar, '/'),
            startLine = metric.StartLine,
            endLine = metric.EndLine,
            complexity = metric.Complexity,
            coveragePercent = metric.CoveragePercent,
            crap = metric.CrapScore,
        })
        .OrderByDescending(member => member.crap)
        .ThenBy(member => member.file, StringComparer.Ordinal)
        .ThenBy(member => member.startLine)
        .ThenBy(member => member.endLine)
        .ThenBy(member => member.name, StringComparer.Ordinal)
        .ThenBy(member => member.complexity)
        .ThenBy(member => member.coveragePercent);

        return JsonSerializer.Serialize(new { members }, Options) + "\n";
    }
}

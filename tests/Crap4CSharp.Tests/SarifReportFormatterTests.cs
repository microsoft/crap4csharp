namespace Microsoft.Crap4CSharp.Tests;

using System.Globalization;
using System.Text.Json;

public class SarifReportFormatterTests
{
    [Fact]
    public void ReportsOnlyScoresAboveTheExistingThresholdWithRuleAndSourceLocation()
    {
        string root = Path.GetTempPath();
        MethodMetrics risky = new("Risky", "Demo.Sample", 3, 0.0, 12.0)
        {
            File = Path.Combine(root, "src", "a # % café.cs"),
            StartLine = 4,
            EndLine = 12,
        };
        using JsonDocument document = JsonDocument.Parse(SarifReportFormatter.Format(
            [new("Boundary", "Demo.Sample", 8, 100.0, 8.0), risky, new("Unknown", "Demo.Sample", 2, null, null)], root));

        document.RootElement.GetProperty("version").GetString().Should().Be("2.1.0");
        document.RootElement.GetProperty("$schema").GetString().Should().Be(
            "https://docs.oasis-open.org/sarif/sarif/v2.1.0/os/schemas/sarif-schema-2.1.0.json");
        JsonElement run = document.RootElement.GetProperty("runs")[0];
        JsonElement driver = run.GetProperty("tool").GetProperty("driver");
        driver.GetProperty("name").GetString().Should().Be("crap4csharp");
        run.GetProperty("results").GetArrayLength().Should().Be(1);
        JsonElement result = run.GetProperty("results")[0];
        result.GetProperty("ruleId").GetString().Should().Be("CRAP001");
        driver.GetProperty("rules")[result.GetProperty("ruleIndex").GetInt32()]
            .GetProperty("id").GetString().Should().Be(result.GetProperty("ruleId").GetString());
        result.GetProperty("level").GetString().Should().Be("warning");
        result.GetProperty("message").GetProperty("text").GetString().Should().Contain("Demo.Sample.Risky");
        JsonElement properties = result.GetProperty("properties");
        properties.GetProperty("crap").GetDouble().Should().Be(12.0);
        properties.GetProperty("complexity").GetInt32().Should().Be(3);
        properties.GetProperty("coveragePercent").GetDouble().Should().Be(0.0);
        JsonElement location = result.GetProperty("locations")[0];
        location.GetProperty("logicalLocations")[0].GetProperty("fullyQualifiedName")
            .GetString().Should().Be("Demo.Sample.Risky");
        JsonElement physical = location.GetProperty("physicalLocation");
        physical.GetProperty("artifactLocation").GetProperty("uri").GetString()
            .Should().Be("src/a%20%23%20%25%20caf%C3%A9.cs");
        physical.GetProperty("artifactLocation").GetProperty("uriBaseId").GetString().Should().Be("%SRCROOT%");
        physical.GetProperty("region").GetProperty("startLine").GetInt32().Should().Be(4);
        physical.GetProperty("region").GetProperty("endLine").GetInt32().Should().Be(12);
    }

    [Fact]
    public void OmitsUnavailablePhysicalLocationAndInvalidRegion()
    {
        using JsonDocument document = JsonDocument.Parse(SarifReportFormatter.Format(
            [new("A", "Demo", 3, null, 12.0), new("B", "Demo", 3, 0.0, 12.0) { File = "Sample.cs", StartLine = 0 }],
            Path.GetTempPath()));
        JsonElement results = document.RootElement.GetProperty("runs")[0].GetProperty("results");

        results[0].GetProperty("locations")[0].TryGetProperty("physicalLocation", out _).Should().BeFalse();
        results[1].GetProperty("locations")[0].GetProperty("physicalLocation")
            .TryGetProperty("region", out _).Should().BeFalse();
    }

    [Fact]
    public void OutputIsDeterministicAcrossCulturesAndInputOrder()
    {
        MethodMetrics[] metrics = [new("ä", "Demo", 3, 25.5, 10.5), new("z", "Demo", 3, 25.5, 10.5)];
        string root = Path.GetTempPath();
        string expected = SarifReportFormatter.Format(metrics, root);
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            SarifReportFormatter.Format([.. metrics.Reverse()], root).Should().Be(expected);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void EmptyInputProducesAValidRunWithoutResults()
    {
        using JsonDocument document = JsonDocument.Parse(SarifReportFormatter.Format([], Path.GetTempPath()));

        document.RootElement.GetProperty("runs").GetArrayLength().Should().Be(1);
        document.RootElement.GetProperty("runs")[0].GetProperty("results").GetArrayLength().Should().Be(0);
    }
}

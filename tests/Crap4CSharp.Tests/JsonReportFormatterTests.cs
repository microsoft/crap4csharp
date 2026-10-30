namespace Microsoft.Crap4CSharp.Tests;

using System.Globalization;
using System.Text.Json;

public class JsonReportFormatterTests
{
    [Fact]
    public void SerializesNumericMetricsLocationsAndEscapedNames()
    {
        string root = Path.GetTempPath();
        MethodMetrics metric = new("Read\"Value", "Demo.Container`1", 3, 75.0, 3.140625)
        {
            File = Path.Combine(root, "src", "a # % café.cs"),
            StartLine = 4,
            EndLine = 12,
        };

        using JsonDocument document = JsonDocument.Parse(JsonReportFormatter.Format([metric], root));
        JsonElement member = document.RootElement.GetProperty("members")[0];

        member.GetProperty("name").GetString().Should().Be("Demo.Container`1.Read\"Value");
        member.GetProperty("file").GetString().Should().Be("src/a # % café.cs");
        member.GetProperty("startLine").GetInt32().Should().Be(4);
        member.GetProperty("endLine").GetInt32().Should().Be(12);
        member.GetProperty("complexity").GetInt32().Should().Be(3);
        member.GetProperty("coveragePercent").GetDouble().Should().Be(75.0);
        member.GetProperty("crap").GetDouble().Should().Be(3.140625);
    }

    [Fact]
    public void KeepsUnavailableMetricsAndLocationsNull()
    {
        using JsonDocument document = JsonDocument.Parse(JsonReportFormatter.Format(
            [new MethodMetrics("Missing", "Demo.Sample", 2, null, null)], Path.GetTempPath()));
        JsonElement member = document.RootElement.GetProperty("members")[0];

        foreach (string property in new[] { "file", "startLine", "endLine", "coveragePercent", "crap" })
        {
            member.GetProperty(property).ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    [Fact]
    public void OrdersScoresAndLocationTiesIndependentlyOfCultureAndInputOrder()
    {
        MethodMetrics[] metrics =
        [
            new("ä", "Demo", 3, 25.5, 10.5) { File = "src/B.cs", StartLine = 4, EndLine = 6 },
            new("z", "Demo", 3, 25.5, 10.5) { File = "src/B.cs", StartLine = 4, EndLine = 6 },
            new("A", "Demo", 3, 25.5, 10.5) { File = "src/A.cs", StartLine = 1, EndLine = 2 },
            new("Unknown", "Demo", 1, null, null),
            new("Highest", "Demo", 4, 0.0, 20.0),
        ];
        string root = Path.GetTempPath();
        string expected = JsonReportFormatter.Format(metrics, root);
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            JsonReportFormatter.Format([.. metrics.Reverse()], root).Should().Be(expected);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        using JsonDocument document = JsonDocument.Parse(expected);
        document.RootElement.GetProperty("members").EnumerateArray()
            .Select(member => member.GetProperty("name").GetString())
            .Should().Equal("Demo.Highest", "Demo.A", "Demo.z", "Demo.ä", "Demo.Unknown");
    }

    [Fact]
    public void EmptyInputProducesAnEmptyMembersArray()
    {
        using JsonDocument document = JsonDocument.Parse(JsonReportFormatter.Format([], Path.GetTempPath()));

        document.RootElement.GetProperty("members").GetArrayLength().Should().Be(0);
    }
}

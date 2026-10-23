namespace Microsoft.Crap4CSharp.Tests;

// Faithful port of crap4java's CliArgumentsParserTest. Records do reference (not structural) equality
// on the IReadOnlyList<string> FileArgs member, so Mode and FileArgs are asserted separately rather
// than comparing whole CliArguments values. Java demo/*.java fixtures are adapted to C#-idiomatic
// src/Demo/*.cs paths; the parser is extension-agnostic, so the verified behavior is identical.
public class CliArgumentsParserTests
{
    [Fact]
    public void NoArgsMeansAllSrcFiles()
    {
        var result = CliArgumentsParser.Parse([]);

        result.Mode.Should().Be(CliMode.AllSrc);
    }

    [Fact]
    public void ChangedFlagMeansChangedSrcFiles()
    {
        var result = CliArgumentsParser.Parse(["--changed"]);

        result.Mode.Should().Be(CliMode.ChangedSrc);
    }

    [Fact]
    public void FileNamesMeanExplicitFiles()
    {
        var result = CliArgumentsParser.Parse(["src/Demo/A.cs", "src/Demo/B.cs"]);

        result.Mode.Should().Be(CliMode.ExplicitFiles);
        result.FileArgs.Should().Equal("src/Demo/A.cs", "src/Demo/B.cs");
    }

    [Fact]
    public void UnknownFlagsAreRejected()
    {
        FluentActions.Invoking(() => CliArgumentsParser.Parse(["src/Demo/A.cs", "--bogus", "src/Demo/B.cs"]))
            .Should().Throw<ArgumentException>().WithMessage("*Unknown option*");
    }

    [Fact]
    public void HelpPrintsUsageMode()
    {
        var result = CliArgumentsParser.Parse(["--help"]);

        result.Mode.Should().Be(CliMode.Help);
    }

    [Fact]
    public void ChangedCannotBeCombinedWithFiles()
    {
        FluentActions.Invoking(() => CliArgumentsParser.Parse(["--changed", "src/Demo/A.cs"]))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PlainFilesDoNotTriggerChangedMode()
    {
        var result = CliArgumentsParser.Parse(["src/Demo/A.cs"]);

        result.Mode.Should().Be(CliMode.ExplicitFiles);
        result.FileArgs.Should().Equal("src/Demo/A.cs");
    }

    [Fact]
    public void DefaultsPreserveExistingPolicy()
    {
        var result = CliArgumentsParser.Parse([]);
        result.TestProject.Should().BeNull();
        result.TestFilter.Should().BeNull();
        result.AllTests.Should().BeFalse();
        result.CrapThreshold.Should().Be(8);
        result.Framework.Should().BeNull();
        result.DryRun.Should().BeFalse();
    }

    [Fact]
    public void ParsesOptionsAmongPathsWithoutChangingValues()
    {
        var result = CliArgumentsParser.Parse([
            "src/A.cs", "--test-project", "tests/Custom.csproj", "--test-filter", "FullyQualifiedName~A | type=IntegrationTests",
            "src/B.cs", "--crap-threshold", "2.0625", "--framework", "net8.0", "--dry-run"]);
        result.Mode.Should().Be(CliMode.ExplicitFiles);
        result.FileArgs.Should().Equal("src/A.cs", "src/B.cs");
        result.TestProject.Should().Be("tests/Custom.csproj");
        result.TestFilter.Should().Be("FullyQualifiedName~A | type=IntegrationTests");
        result.CrapThreshold.Should().Be(2.0625);
        result.Framework.Should().Be("net8.0");
        result.DryRun.Should().BeTrue();
    }

    [Theory]
    [InlineData("--dry-run")]
    [InlineData("--all-tests")]
    public void OptionsOnlySelectAllSources(string option)
    {
        CliArgumentsParser.Parse([option]).Mode.Should().Be(CliMode.AllSrc);
        CliArgumentsParser.Parse([option, "--changed"]).Mode.Should().Be(CliMode.ChangedSrc);
    }

    [Theory]
    [InlineData("--test-project", "tests/Custom.csproj")]
    [InlineData("--test-filter", "type=IntegrationTests")]
    [InlineData("--framework", "net8.0-windows")]
    [InlineData("--crap-threshold", "10.5")]
    public void ValueOptionsWorkAloneOrWithChanged(string option, string value)
    {
        CliArgumentsParser.Parse([option, value]).Mode.Should().Be(CliMode.AllSrc);
        CliArgumentsParser.Parse([option, value, "--changed"]).Mode.Should().Be(CliMode.ChangedSrc);
    }

    [Theory]
    [InlineData("--test-project")]
    [InlineData("--test-filter")]
    [InlineData("--crap-threshold")]
    [InlineData("--framework")]
    public void RequiresNonblankValuesAndDoesNotSwallowSwitches(string option)
    {
        foreach (string[] args in new[] { new[] { option }, [option, string.Empty], [option, "  "], [option, "--dry-run"], [option, "-x"] })
        {
            FluentActions.Invoking(() => CliArgumentsParser.Parse(args)).Should().Throw<ArgumentException>();
        }
    }

    [Theory]
    [InlineData("--changed")]
    [InlineData("--dry-run")]
    [InlineData("--all-tests")]
    [InlineData("--test-filter", "Name=A")]
    [InlineData("--framework", "net8.0")]
    [InlineData("--crap-threshold", "8")]
    public void RejectsDuplicateOptions(string option, string? value = null)
    {
        string[] args = value is null ? [option, option] : [option, value, option, value];
        FluentActions.Invoking(() => CliArgumentsParser.Parse(args)).Should().Throw<ArgumentException>()
            .WithMessage("*Duplicate*");
    }

    [Fact]
    public void AcceptsRepeatedTestProjectSelectorsOnly()
    {
        CliArguments options = CliArgumentsParser.Parse(
            ["--test-project", "tests/A.csproj", "--test-project", "tests/**/*.Tests.csproj"]);
        options.TestProjects.Should().Equal("tests/A.csproj", "tests/**/*.Tests.csproj");
        options.TestProject.Should().BeNull();
        options.Mode.Should().Be(CliMode.AllSrc);
        CliArgumentsParser.Parse([]).TestProjects.Should().BeEmpty();
        CliArguments legacy = new(CliMode.AllSrc, []) { TestProject = "tests/Legacy.csproj" };
        legacy.TestProjects.Should().Equal("tests/Legacy.csproj");
        (legacy with { TestProjects = ["A.csproj", "B.csproj"] }).TestProject.Should().BeNull();
        FluentActions.Invoking(() => legacy with { TestProjects = null! }).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => CliArgumentsParser.Parse(
            ["--test-project", "A.csproj", "--test-project", "--dry-run"])).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RejectsConflictsButExactHelpAlwaysWins()
    {
        string[] args = ["--all-tests", "--test-filter", "Name=A"];
        FluentActions.Invoking(() => CliArgumentsParser.Parse(args)).Should().Throw<ArgumentException>();
        CliArgumentsParser.Parse(["--bogus", "--crap-threshold", "NaN", "--changed", "A.cs", "--help"])
            .Mode.Should().Be(CliMode.Help);
        FluentActions.Invoking(() => CliArgumentsParser.Parse(["--help=yes"])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => CliArgumentsParser.Parse(["--"])).Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e999")]
    [InlineData("-0.1")]
    [InlineData("1,000")]
    [InlineData("8,5")]
    public void RejectsInvalidThresholds(string value)
    {
        FluentActions.Invoking(() => CliArgumentsParser.Parse(["--crap-threshold", value]))
            .Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("-0", 0)]
    [InlineData("8.000000000000002", 8.000000000000002)]
    [InlineData("1e2", 100)]
    public void ParsesInvariantThresholdRegardlessOfCulture(string value, double expected)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            CliArgumentsParser.Parse(["--crap-threshold", value]).CrapThreshold.Should().Be(expected);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("net8.0;net9.0")]
    [InlineData("net8.0,net9.0")]
    [InlineData("net8.0 net9.0")]
    [InlineData("-p:Foo=Bar")]
    [InlineData("TargetFramework=net8.0")]
    [InlineData("net8.0/net9.0")]
    public void RejectsFrameworkListsAndAssignments(string value)
    {
        FluentActions.Invoking(() => CliArgumentsParser.Parse(["--framework", value]))
            .Should().Throw<ArgumentException>();
    }
}

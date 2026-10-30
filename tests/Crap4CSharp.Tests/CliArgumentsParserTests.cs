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
        result.Format.Should().Be(OutputFormat.Console);
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
    public void UnknownFlagsAreIgnoredWhenCollectingExplicitFiles()
    {
        var result = CliArgumentsParser.Parse(["src/Demo/A.cs", "--bogus", "src/Demo/B.cs"]);

        result.Mode.Should().Be(CliMode.ExplicitFiles);
        result.FileArgs.Should().Equal("src/Demo/A.cs", "src/Demo/B.cs");
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

    [Theory]
    [InlineData("console", OutputFormat.Console)]
    [InlineData("json", OutputFormat.Json)]
    [InlineData("sarif", OutputFormat.Sarif)]
    public void FormatAloneSelectsAllSourceFiles(string value, OutputFormat expected)
    {
        CliArguments result = CliArgumentsParser.Parse(["--format", value]);

        result.Mode.Should().Be(CliMode.AllSrc);
        result.Format.Should().Be(expected);
        result.FileArgs.Should().BeEmpty();
    }

    [Fact]
    public void FormatCanAppearBeforeBetweenOrAfterFileArguments()
    {
        foreach (string[] args in new[]
        {
            new[] { "--format", "json", "A.cs", "B.cs" },
            new[] { "A.cs", "--format", "json", "B.cs" },
            new[] { "A.cs", "B.cs", "--format", "json" },
        })
        {
            CliArguments result = CliArgumentsParser.Parse(args);
            result.Mode.Should().Be(CliMode.ExplicitFiles);
            result.Format.Should().Be(OutputFormat.Json);
            result.FileArgs.Should().Equal("A.cs", "B.cs");
        }
    }

    [Fact]
    public void FormatPreservesChangedModeAndItsFileArgumentRejection()
    {
        CliArguments result = CliArgumentsParser.Parse(["--format", "sarif", "--changed"]);

        result.Mode.Should().Be(CliMode.ChangedSrc);
        result.Format.Should().Be(OutputFormat.Sarif);
        FluentActions.Invoking(() => CliArgumentsParser.Parse(["--changed", "A.cs", "--format", "json"]))
            .Should().Throw<ArgumentException>().WithMessage("--changed cannot be combined with file arguments");
    }

    [Theory]
    [InlineData("xml")]
    [InlineData("JSON")]
    [InlineData("")]
    public void RejectsInvalidFormatValues(string value)
    {
        FluentActions.Invoking(() => CliArgumentsParser.Parse(["--format", value]))
            .Should().Throw<ArgumentException>().WithMessage("Invalid --format value*");
    }

    [Fact]
    public void RejectsMissingAndDuplicateFormatValues()
    {
        FluentActions.Invoking(() => CliArgumentsParser.Parse(["--format"]))
            .Should().Throw<ArgumentException>().WithMessage("--format requires a value*");
        FluentActions.Invoking(() => CliArgumentsParser.Parse(["--format", "--changed"]))
            .Should().Throw<ArgumentException>().WithMessage("--format requires a value*");
        FluentActions.Invoking(() => CliArgumentsParser.Parse(["--format", "json", "--format", "json"]))
            .Should().Throw<ArgumentException>().WithMessage("--format cannot be specified more than once");
    }

    [Fact]
    public void HelpStillWinsOverInvalidFormatAndOtherArguments()
    {
        CliArgumentsParser.Parse(["--format", "xml", "--changed", "A.cs", "--help"])
            .Mode.Should().Be(CliMode.Help);
        CliArgumentsParser.Parse(["--format", "--help"]).Mode.Should().Be(CliMode.Help);
    }

    [Fact]
    public void UnknownFlagAloneKeepsTheLegacyEmptyExplicitFileList()
    {
        CliArguments result = CliArgumentsParser.Parse(["--bogus"]);

        result.Mode.Should().Be(CliMode.ExplicitFiles);
        result.FileArgs.Should().BeEmpty();
        result.Format.Should().Be(OutputFormat.Console);
    }
}

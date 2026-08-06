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
}

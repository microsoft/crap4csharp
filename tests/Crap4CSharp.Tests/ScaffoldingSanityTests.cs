namespace Microsoft.Crap4CSharp.Tests;

/// <summary>
/// Throwaway scaffolding test proving the xUnit + FluentAssertions harness discovers and runs.
/// A later task replaces this with real behavioral tests.
/// </summary>
public class ScaffoldingSanityTests
{
    [Fact]
    public void HarnessDiscoversAndRuns()
    {
        true.Should().BeTrue();
    }
}

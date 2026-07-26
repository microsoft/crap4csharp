namespace Microsoft.Crap4CSharp.Tests;

public class CoverageDataTests
{
    [Fact]
    public void CoveragePercentReturnsCoveredShareOfTotal()
    {
        var coverage = new CoverageData(MissedInstructions: 1, CoveredInstructions: 3);

        coverage.CoveragePercent.Should().Be(75.0);
    }

    [Fact]
    public void CoveragePercentReturnsZeroWhenNothingToCover()
    {
        var coverage = new CoverageData(MissedInstructions: 0, CoveredInstructions: 0);

        coverage.CoveragePercent.Should().Be(0.0);
    }
}

namespace Microsoft.Crap4CSharp.Tests;

public class CrapScoreTests
{
    [Fact]
    public void ReturnsComplexityWhenFullyCovered()
    {
        CrapScore.Calculate(5, 100.0).Should().BeApproximately(5.0, 0.0001);
    }

    [Fact]
    public void ReturnsCcSquaredPlusCcWhenUncovered()
    {
        CrapScore.Calculate(5, 0.0).Should().BeApproximately(30.0, 0.0001);
    }

    [Fact]
    public void ComputesPartialCoverage()
    {
        CrapScore.Calculate(8, 45.0).Should().BeApproximately(18.648, 0.01);
    }

    [Fact]
    public void ReturnsNullForUnknownCoverage()
    {
        CrapScore.Calculate(3, null).Should().BeNull();
    }
}

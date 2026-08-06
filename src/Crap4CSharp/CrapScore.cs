namespace Microsoft.Crap4CSharp;

// Faithful port of crap4java's CrapScore utility. Java's non-instantiable `final class` with a
// private ctor maps to the idiomatic C# `static class`. CRAP = CC² · (1 − coverage)³ + CC, where
// coverage = coveragePercent / 100. Returns null when coverage is unknown (Java boxed `Double`).
public static class CrapScore
{
    // Ports calculate(int, Double) exactly.
    public static double? Calculate(int complexity, double? coveragePercent)
    {
        if (coveragePercent is null)
        {
            return null;
        }

        double cc = complexity;
        double uncovered = 1.0 - (coveragePercent.Value / 100.0);
        return (cc * cc * uncovered * uncovered * uncovered) + cc;
    }
}

namespace Microsoft.Crap4CSharp;

// Faithful port of crap4java's CoverageData record, with the counter fields renamed to
// MissedLines/CoveredLines (previously JaCoCo INSTRUCTION-named): this port's Cobertura adapter
// counts LINES, not JaCoCo INSTRUCTIONs (deliberate departure per docs/decisions.md; risk R1).
// Algorithm is identical.
public record CoverageData(int MissedLines, int CoveredLines)
{
    // Ports coveragePercent() exactly: 0.0 when there is nothing to cover, else the covered share.
    public double CoveragePercent =>
        MissedLines + CoveredLines == 0
            ? 0.0
            : (CoveredLines * 100.0) / (MissedLines + CoveredLines);
}

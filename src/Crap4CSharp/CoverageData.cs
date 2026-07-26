namespace Microsoft.Crap4CSharp;

// Faithful port of crap4java's CoverageData record. The Java field names MissedInstructions/
// CoveredInstructions are kept verbatim for structural parity. NOTE (risk R1): under Cobertura
// (this port) these carry LINE counters, not JaCoCo INSTRUCTION counters — the algorithm is
// identical but the absolute numbers differ.
public record CoverageData(int MissedInstructions, int CoveredInstructions)
{
    // Ports coveragePercent() exactly: 0.0 when there is nothing to cover, else the covered share.
    public double CoveragePercent =>
        MissedInstructions + CoveredInstructions == 0
            ? 0.0
            : (CoveredInstructions * 100.0) / (MissedInstructions + CoveredInstructions);
}

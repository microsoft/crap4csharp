namespace Microsoft.Crap4CSharp;

// Faithful port of crap4java's MethodDescriptor record. Name/StartLine/EndLine/Complexity are the
// original Java fields. TypeName is the one approved departure (appended last): the enclosing
// type's namespace-qualified FQN, which C# needs as the per-method coverage-lookup key because a
// C# file may declare many types (see docs/decisions.md "coverage key = enclosing-type FQN").
// CoverletBlind (T30, departure #15) is the 2nd C#-only appended-last field: set by the parser for
// an emitted accessor that SHARES its declaration start line with an earlier accessor of the same
// member, because coverlet #507 records only the FIRST accessor on a shared physical line and drops
// the rest from the cobertura XML. It is a parser->analyzer signal, consumed by CrapAnalyzer (which
// promotes such a structurally-absent accessor to a real 0% instead of N/A); defaulting to false
// keeps every existing construction/consumer compiling untouched (same pattern as TypeName).
public record MethodDescriptor(string Name, int StartLine, int EndLine, int Complexity, string TypeName, bool CoverletBlind = false);

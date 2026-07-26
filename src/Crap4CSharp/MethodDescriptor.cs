namespace Microsoft.Crap4CSharp;

// Faithful port of crap4java's MethodDescriptor record. Name/StartLine/EndLine/Complexity are the
// original Java fields. TypeName is the one approved departure (appended last): the enclosing
// type's namespace-qualified FQN, which C# needs as the per-method coverage-lookup key because a
// C# file may declare many types (see docs/decisions.md "coverage key = enclosing-type FQN").
// Populated later (T9/T11); defined here for downstream tasks.
public record MethodDescriptor(string Name, int StartLine, int EndLine, int Complexity, string TypeName);

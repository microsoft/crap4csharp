namespace Microsoft.Crap4CSharp;

// Faithful port of crap4java's MethodMetrics record. Java's boxed Double coveragePercent/crapScore
// (nullable for the N/A cases) become double?; int complexity stays non-null.
public record MethodMetrics(
    string MethodName,
    string ClassName,
    int Complexity,
    double? CoveragePercent,
    double? CrapScore);

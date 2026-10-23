namespace Microsoft.Crap4CSharp;

public sealed record CoverageRun(string TestProject, string ResultsDirectory, IReadOnlyList<string> Command);

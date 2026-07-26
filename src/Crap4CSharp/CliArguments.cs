namespace Microsoft.Crap4CSharp;

// Faithful port of crap4java's CliArguments record. Java's List<String> fileArgs becomes
// IReadOnlyList<string> per the ratified "IReadOnlyList returns" idiom.
public record CliArguments(CliMode Mode, IReadOnlyList<string> FileArgs);

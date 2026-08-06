namespace Microsoft.Crap4CSharp;

// Faithful port of crap4java's CliMode enum. Java's SCREAMING_SNAKE_CASE constants
// (HELP/ALL_SRC/CHANGED_SRC/EXPLICIT_FILES) are renamed to idiomatic PascalCase (CA1707 forbids
// underscores) — same behavior, C# spelling.
public enum CliMode
{
    Help,
    AllSrc,
    ChangedSrc,
    ExplicitFiles,
}

namespace Microsoft.Crap4CSharp;

// Faithful port of crap4java's CliArgumentsParser. Java's non-instantiable `final class` with a
// private ctor maps to the idiomatic C# `static class`. Precedence is preserved exactly: no args →
// AllSrc; --help wins over everything; --changed → ChangedSrc (rejecting file args); otherwise the
// non-flag args become an ExplicitFiles list. String comparisons are ordinal to match Java's
// String.equals / String.startsWith.
public static class CliArgumentsParser
{
    // Preserve legacy parsing while removing the optional format flag and its value from file args.
    public static CliArguments Parse(string[] args)
    {
        // Java implicitly throws NullPointerException on `args.length` when args is null; the
        // idiomatic C# equivalent (and CA1062's required guard, since suppressions are banned) is a
        // fail-fast ArgumentNullException. Behavior is otherwise identical to crap4java's parse.
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            return new CliArguments(CliMode.AllSrc, []);
        }

        if (ContainsFlag(args, "--help"))
        {
            return new CliArguments(CliMode.Help, []);
        }

        bool changed = ContainsFlag(args, "--changed");
        (OutputFormat format, bool hasFormat, List<string> values) = ParseValues(args);
        EnsureChangedIsNotCombined(changed, values);
        if (changed)
        {
            return new CliArguments(CliMode.ChangedSrc, []) { Format = format };
        }

        CliMode mode = hasFormat && values.Count == 0 ? CliMode.AllSrc : CliMode.ExplicitFiles;
        return new CliArguments(mode, [.. values]) { Format = format };
    }

    private static bool ContainsFlag(string[] args, string flag)
    {
        foreach (string arg in args)
        {
            if (string.Equals(arg, flag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static (OutputFormat Format, bool HasFormat, List<string> Values) ParseValues(string[] args)
    {
        OutputFormat format = OutputFormat.Console;
        bool hasFormat = false;
        List<string> values = [];
        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];
            if (string.Equals(arg, "--format", StringComparison.Ordinal))
            {
                if (hasFormat)
                {
                    throw new ArgumentException("--format cannot be specified more than once");
                }

                hasFormat = true;
                if (++index == args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException("--format requires a value: console, json, or sarif");
                }

                format = args[index] switch
                {
                    "console" => OutputFormat.Console,
                    "json" => OutputFormat.Json,
                    "sarif" => OutputFormat.Sarif,
                    _ => throw new ArgumentException($"Invalid --format value '{args[index]}': expected console, json, or sarif"),
                };
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            values.Add(arg);
        }

        return (format, hasFormat, values);
    }

    private static void EnsureChangedIsNotCombined(bool changed, List<string> values)
    {
        if (changed && values.Count > 0)
        {
            throw new ArgumentException("--changed cannot be combined with file arguments");
        }
    }
}

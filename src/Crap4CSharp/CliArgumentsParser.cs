namespace Microsoft.Crap4CSharp;

// Faithful port of crap4java's CliArgumentsParser. Java's non-instantiable `final class` with a
// private ctor maps to the idiomatic C# `static class`. Precedence is preserved exactly: no args →
// AllSrc; --help wins over everything; --changed → ChangedSrc (rejecting file args); otherwise the
// non-flag args become an ExplicitFiles list. String comparisons are ordinal to match Java's
// String.equals / String.startsWith.
public static class CliArgumentsParser
{
    // Ports parse(String[]) exactly.
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
        IReadOnlyList<string> values = NonFlagArgs(args);
        EnsureChangedIsNotCombined(changed, values);
        if (changed)
        {
            return new CliArguments(CliMode.ChangedSrc, []);
        }

        return new CliArguments(CliMode.ExplicitFiles, [.. values]);
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

    private static List<string> NonFlagArgs(string[] args)
    {
        List<string> values = [];
        foreach (string arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            values.Add(arg);
        }

        return values;
    }

    private static void EnsureChangedIsNotCombined(bool changed, IReadOnlyList<string> values)
    {
        if (changed && values.Count > 0)
        {
            throw new ArgumentException("--changed cannot be combined with file arguments");
        }
    }
}

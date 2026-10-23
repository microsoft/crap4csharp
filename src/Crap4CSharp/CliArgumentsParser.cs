namespace Microsoft.Crap4CSharp;

using System.Globalization;

public static class CliArgumentsParser
{
    public static CliArguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Contains("--help", StringComparer.Ordinal))
        {
            return new(CliMode.Help, []);
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        List<string> files = [];
        List<string> selectors = [];
        CliArguments options = new(CliMode.AllSrc, files) { TestProjects = selectors };
        bool changed = false;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith('-'))
            {
                files.Add(arg);
                continue;
            }

            if (!seen.Add(arg) && arg != "--test-project")
            {
                throw new ArgumentException($"Duplicate option: {arg}");
            }

            switch (arg)
            {
                case "--changed": changed = true; break;
                case "--all-tests": options = options with { AllTests = true }; break;
                case "--dry-run": options = options with { DryRun = true }; break;
                case "--test-project":
                    selectors.Add(Value(args, ref i));
                    break;
                case "--test-filter": options = options with { TestFilter = Value(args, ref i) }; break;
                case "--framework":
                    string framework = Value(args, ref i);
                    if (!framework.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-')
                        || !char.IsAsciiLetterOrDigit(framework[0]))
                    {
                        throw new ArgumentException("--framework requires one TFM, not a list or property assignment.");
                    }

                    options = options with { Framework = framework };
                    break;
                case "--crap-threshold":
                    string value = Value(args, ref i);
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double threshold)
                        || !double.IsFinite(threshold) || threshold < 0)
                    {
                        throw new ArgumentException("--crap-threshold requires a finite nonnegative invariant number.");
                    }

                    options = options with { CrapThreshold = threshold };
                    break;
                default: throw new ArgumentException($"Unknown option: {arg}");
            }
        }

        if (changed && files.Count > 0)
        {
            throw new ArgumentException("--changed cannot be combined with file arguments");
        }

        if (options.AllTests && options.TestFilter is not null)
        {
            throw new ArgumentException("--all-tests cannot be combined with --test-filter");
        }

        return options with { Mode = changed ? CliMode.ChangedSrc : files.Count > 0 ? CliMode.ExplicitFiles : CliMode.AllSrc };
    }

    private static string Value(string[] args, ref int index)
    {
        string option = args[index];
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])
            || (args[index + 1].StartsWith('-')
                && !(option == "--crap-threshold" && double.TryParse(
                    args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out _))))
        {
            throw new ArgumentException($"{option} requires a nonblank value.");
        }

        return args[++index];
    }
}

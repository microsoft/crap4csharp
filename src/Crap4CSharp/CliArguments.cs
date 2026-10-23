namespace Microsoft.Crap4CSharp;

// Faithful port of crap4java's CliArguments record. Java's List<String> fileArgs becomes
// IReadOnlyList<string> per the ratified "IReadOnlyList returns" idiom.
public record CliArguments(CliMode Mode, IReadOnlyList<string> FileArgs)
{
    private IReadOnlyList<string> _testProjects = [];

    // Legacy singleton API is a view of the same selection, never a conflicting second source of truth.
    public string? TestProject
    {
        get => _testProjects.Count == 1 ? _testProjects[0] : null;
        init => _testProjects = value is null ? [] : [value];
    }

    public IReadOnlyList<string> TestProjects
    {
        get => _testProjects;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _testProjects = value;
        }
    }

    public string? TestFilter { get; init; }

    public bool AllTests { get; init; }

    public double CrapThreshold { get; init; } = 8.0;

    public string? Framework { get; init; }

    public bool DryRun { get; init; }
}

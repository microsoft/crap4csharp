namespace Microsoft.Crap4CSharp;

/// <summary>
/// Domain failure raised when the coverage COMMAND itself fails (a non-zero exit from the coverage
/// run in <see cref="CoverageRunner.GenerateCoverage"/>). <see cref="Program.Main"/> catches this type
/// specifically to convert an escaping coverage-command failure into process exit 1 (the D-T14d
/// realization), keeping the entry-point catch least-privilege / CA1031-clean without any suppression.
/// </summary>
public sealed class CoverageException : Exception
{
    public CoverageException()
    {
    }

    public CoverageException(string? message)
        : base(message)
    {
    }

    public CoverageException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

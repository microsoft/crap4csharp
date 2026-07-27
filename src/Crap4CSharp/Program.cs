namespace Microsoft.Crap4CSharp;

/// <summary>
/// Entry point for the crap4csharp CLI. Faithful port of crap4java's <c>Main.main</c>: resolve the
/// project root as the absolute, normalized current directory (<c>Path.GetFullPath(".")</c> ==
/// Java <c>Path.of(".").toAbsolutePath().normalize()</c>), wire the real console streams and a real
/// <see cref="ProcessCommandExecutor"/>, and delegate to <see cref="CliApplication.Execute"/> (which
/// owns the full 0 / 1 / 2 exit table). The catch realizes the T14/T15 propagation boundary (D-T14d):
/// <see cref="CliApplication.Execute"/> deliberately does not catch the coverage-command failure, and
/// because .NET — unlike the JVM — does not guarantee process exit code 1 on an unhandled exception,
/// <see cref="Main"/> catches the domain <see cref="CoverageException"/> specifically and converts it to
/// exit 1. The returned <see cref="int"/> is used as the process exit code (never <c>Environment.Exit</c>).
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        string projectRoot = Path.GetFullPath(".");
        try
        {
            return new CliApplication(
                projectRoot,
                Console.Out,
                Console.Error,
                new CoverageRunner(new ProcessCommandExecutor()))
                .Execute(args);
        }
        catch (CoverageException ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}

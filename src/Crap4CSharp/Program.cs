namespace Microsoft.Crap4CSharp;

/// <summary>
/// Entry point for the crap4csharp CLI. Faithful port of crap4java's <c>Main.main</c>: resolve the
/// project root as the absolute, normalized current directory (<c>Path.GetFullPath(".")</c> ==
/// Java <c>Path.of(".").toAbsolutePath().normalize()</c>), wire the real console streams and a real
/// <see cref="ProcessCommandExecutor"/>, and delegate to <see cref="CliApplication.Execute"/> (which
/// owns the full 0 / 1 / 2 exit table). Java's <c>main</c> is declared <c>throws Exception</c> and the
/// JVM maps any escaping exception to a non-zero exit; .NET does NOT guarantee exit 1 on an unhandled
/// exception, so <see cref="Main"/> installs a top-level <c>catch (Exception)</c> that writes the full
/// exception to stderr and returns 1 -- full parity with Java's "any failure becomes exit 1" contract.
/// Every propagated or unexpected failure (a <c>git</c> failure under <c>--changed</c>, an I/O error,
/// malformed coverage XML or any parser throw, or the coverage-command <see cref="CoverageException"/>)
/// therefore becomes exit 1. Deliberate exit codes are unaffected: usage-error / fail-fast (1) and
/// threshold-exceeded (2) are NORMAL RETURNS from <see cref="CliApplication.Execute"/> that flow through
/// the <c>try</c> and are returned verbatim; the catch-all only intercepts THROWN exceptions, so
/// threshold-exceeded still returns 2 and is never swallowed to 1. The intentional entry-point catch-all
/// is licensed by a scoped <c>CA1031</c> relaxation for <c>Program.cs</c> in <c>.editorconfig</c> (no
/// <c>#pragma</c> / no <c>[SuppressMessage]</c>). The returned <see cref="int"/> is used as the process
/// exit code (never <c>Environment.Exit</c>).
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
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}

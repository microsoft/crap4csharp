namespace Microsoft.Crap4CSharp;

using System.Diagnostics;

// Faithful port of crap4java's ProcessCommandExecutor. Java's `final class` maps to C# `sealed`. It
// launches `command` in `workingDirectory` and returns the child process's exit code.
//
// LOCKED IDIOM -- "async stdout drain" (docs/decisions.md, I-series adopt-now baseline). Java uses
// ProcessBuilder.inheritIO() so the child shares the parent's console. The ratified C# equivalent
// redirects stdout AND stderr, drains BOTH streams asynchronously, and writes them through to
// Console.Out / Console.Error: this preserves inheritIO's user-visible output while guaranteeing a
// full pipe buffer can never deadlock WaitForExit.
public sealed class ProcessCommandExecutor : ICommandExecutor
{
    // Ports run(List<String>, Path) exactly.
    public int Run(IReadOnlyList<string> command, string workingDirectory)
    {
        // Java implicitly NPEs when dereferencing a null command/directory; the idiomatic C# equivalent
        // (and CA1062's required guard, since suppressions are banned) is a fail-fast ArgumentNullException.
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(workingDirectory);

        ProcessStartInfo startInfo = new()
        {
            FileName = command[0],
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // command[0] is the executable; the remaining entries are arguments. Adding them to
        // ArgumentList (not the single Arguments string) sidesteps platform quoting pitfalls.
        for (int index = 1; index < command.Count; index++)
        {
            startInfo.ArgumentList.Add(command[index]);
        }

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                Console.Out.WriteLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                Console.Error.WriteLine(e.Data);
            }
        };

        // Order matters: Start(), then begin the async readers, then WaitForExit() -- the parameterless
        // overload also blocks until both redirected streams have been fully drained.
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        return process.ExitCode;
    }
}

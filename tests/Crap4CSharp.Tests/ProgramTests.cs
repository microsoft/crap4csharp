namespace Microsoft.Crap4CSharp.Tests;

using System.Diagnostics;

// The genuinely-new T15 integration tests: spawn the REAL built crap4csharp entry point end-to-end and
// assert its process exit code. Ports crap4java MainTest's two mainProcess* tests -- the only MainTest
// cases not already pinned in-process by CliApplicationTests (see the T15 contract §4 ledger). Both spawn
// argument paths deliberately short-circuit BEFORE any coverage run (--help -> usage/exit 0; --changed +
// file -> parse error/exit 1), so no real `dotnet test` is ever launched: the tests are fast + deterministic
// even though D-T8 runs them by default. The app is spawned as
// `dotnet <AppContext.BaseDirectory>/Microsoft.Crap4CSharp.dll` (framework-dependent activation) -- NOT the
// OS-specific apphost -- for cross-platform (ubuntu CI) determinism. PascalCase names, no [Trait] (C2/D-T8).
public class ProgramTests
{
    private const string AppDllName = "Microsoft.Crap4CSharp.dll";
    private const int SpawnTimeoutMs = 60_000;

    [Fact]
    public async Task MainProcessExitsZeroForHelp()
    {
        SpawnResult result = await RunEntryPointAsync("--help");

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("Usage:");
    }

    [Fact]
    public async Task MainProcessExitsNonZeroForUnknownOption()
    {
        SpawnResult result = await RunEntryPointAsync("--changed", "src/demo/Sample.cs");

        result.ExitCode.Should().Be(1);
        result.StandardError.Should().Contain("--changed cannot be combined with file arguments");
    }

    // T20 -- the catch-all parity path: `--changed` in a fresh, non-git temp cwd makes ChangedFileDetector
    // run `git status`, which fails (not a repository) and throws a NON-CoverageException
    // (InvalidOperationException). That propagates out of CliApplication.Execute and MUST be converted by
    // Program.Main's top-level catch-all to exit 1, with the full exception written to stderr. This closes
    // the S6 clean-room "fatal exit-code consistency" finding (git / I/O / parser failures -> exit 1, not a
    // platform-specific unhandled-exception code). GIT_CEILING_DIRECTORIES (set in RunEntryPointAsync) makes
    // the git failure deterministic on every dev box and CI OS. No coverage run is ever reached (D-T15e).
    [Fact]
    public async Task MainProcessExitsOneWhenGitFailsForChanged()
    {
        SpawnResult result = await RunEntryPointAsync("--changed");

        result.ExitCode.Should().Be(1);
        result.StandardError.Should().Contain("git status failed");
    }

    private static async Task<SpawnResult> RunEntryPointAsync(params string[] args)
    {
        // The app is a ProjectReference of the test project, so its DLL is copied next to the test assembly;
        // AppContext.BaseDirectory is that output dir at run time. Assembly name is Microsoft.Crap4CSharp
        // (Common.targets: AssemblyName=Microsoft.$(MSBuildProjectName)), hence Microsoft.Crap4CSharp.dll.
        string appDll = Path.Combine(AppContext.BaseDirectory, AppDllName);
        File.Exists(appDll).Should().BeTrue(
            $"the app is a ProjectReference so '{AppDllName}' must be copied next to the test assembly");

        string workingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        try
        {
            // Spawn via `dotnet <dll>` (framework-dependent activation) -- NOT the apphost, whose name is
            // OS-specific (Microsoft.Crap4CSharp.exe on Windows, extension-less on Linux and possibly without
            // the exec bit after xcopy). `dotnet <dll>` is uniform on every platform CI runs on.
            ProcessStartInfo startInfo = new()
            {
                FileName = "dotnet",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            // Hermetic git isolation (T20): the --changed spawn test runs `git status` in this fresh temp
            // cwd; capping git's repo-discovery walk at the temp parent guarantees it is NEVER seen as a git
            // repo (even on a dev box whose home dir is itself a repo), so git exits non-zero deterministically
            // -> ChangedFileDetector throws -> Program.Main's catch-all -> exit 1. Harmless for the non-git
            // spawn paths (--help / parse-error), which never invoke git.
            startInfo.Environment["GIT_CEILING_DIRECTORIES"] = Directory.GetParent(workingDirectory)!.FullName;
            startInfo.ArgumentList.Add(appDll);
            foreach (string arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using Process process = new() { StartInfo = startInfo };
            using CancellationTokenSource cts = new(SpawnTimeoutMs);

            process.Start();

            // Drain BOTH streams asynchronously to avoid a full-pipe deadlock; await async waits to stay
            // clear of CA1849 (no blocking WaitForExit in an async method).
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(cts.Token);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException(
                    $"crap4csharp entry point did not exit within {SpawnTimeoutMs} ms.");
            }

            return new SpawnResult(process.ExitCode, await stdout, await stderr);
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
        }
    }

    private sealed record SpawnResult(int ExitCode, string StandardOutput, string StandardError);
}

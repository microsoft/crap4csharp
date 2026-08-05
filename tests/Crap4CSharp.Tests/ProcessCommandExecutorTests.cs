namespace Microsoft.Crap4CSharp.Tests;

using System.Runtime.InteropServices;

// Faithful counterpart to crap4java's ProcessCommandExecutorTest. The single Java test
// (returnsExitCodeFromLaunchedProcess) launches a shell that exits 7 inside a @TempDir and asserts the
// returned exit code is 7. This port keeps the same single oracle (exit 7) and a real temp working
// directory; only the launch command is adapted per-OS because Java hardcodes the POSIX /bin/sh path,
// which cannot exist on Windows.
public class ProcessCommandExecutorTests
{
    [Fact]
    public void ReturnsExitCodeFromLaunchedProcess()
    {
        string workingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        try
        {
            // cross-platform: Java hardcodes /bin/sh; see T6 adaptation. Same oracle (exit 7) on both
            // branches -- Windows runs cmd.exe locally, ubuntu CI exercises the /bin/sh branch.
            IReadOnlyList<string> command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? ["cmd.exe", "/c", "exit 7"]
                : ["/bin/sh", "-c", "exit 7"];

            int exit = new ProcessCommandExecutor().Run(command, workingDirectory);

            exit.Should().Be(7);
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }
}

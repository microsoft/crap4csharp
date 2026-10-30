namespace Microsoft.Crap4CSharp.Tests;

using System.Globalization;
using System.Runtime.InteropServices;

// Faithful counterpart to crap4java's ProcessCommandExecutorTest. The single Java test
// (returnsExitCodeFromLaunchedProcess) launches a shell that exits 7 inside a @TempDir and asserts the
// returned exit code is 7. This port keeps the same single oracle (exit 7) and a real temp working
// directory; only the launch command is adapted per-OS because Java hardcodes the POSIX /bin/sh path,
// which cannot exist on Windows.
public class ProcessCommandExecutorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoutesChildStreamsToTheConfiguredWriters(bool combineStreams)
    {
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);
        IReadOnlyList<string> command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ["cmd.exe", "/c", "echo child-output & echo child-error 1>&2"]
            : ["/bin/sh", "-c", "echo child-output; echo child-error >&2"];

        int exit = new ProcessCommandExecutor(combineStreams ? error : output, error)
            .Run(command, Path.GetTempPath());

        exit.Should().Be(0);
        error.ToString().Should().Contain("child-error");
        if (combineStreams)
        {
            output.ToString().Should().BeEmpty();
            error.ToString().Should().Contain("child-output");
        }
        else
        {
            output.ToString().Should().Contain("child-output").And.NotContain("child-error");
            error.ToString().Should().NotContain("child-output");
        }
    }

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

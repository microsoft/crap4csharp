namespace Microsoft.Crap4CSharp.Tests;

using System.Diagnostics;

// Faithful port of crap4java's ChangedFileDetectorTest. All five Java tests are ported with identical
// oracles; the *.java tree is adapted to *.cs and the changedJavaFiles* members to ChangedCSharpFiles*.
// The one test-infra adaptation is the git runner: Java drives setup through `sh -c "git ..."` (POSIX
// only), which is replaced here by a direct, cross-platform git invocation with the same commands and
// the same deadlock-free capture pattern used by the detector under test. Each test runs against a
// fresh temp directory that is always removed in a finally (mirrors JUnit's @TempDir).
public class ChangedFileDetectorTests
{
    [Fact]
    public void FindsModifiedAndUntrackedCSharpFiles()
    {
        WithTempRepo(repo =>
        {
            RunGit(repo, "init");
            RunGit(repo, "config", "user.email", "test@example.com");
            RunGit(repo, "config", "user.name", "test");

            string tracked = WriteFile(repo, "class Tracked {}\n", "src", "demo", "Tracked.cs");

            RunGit(repo, "add", ".");
            RunGit(repo, "commit", "-m", "init");

            File.WriteAllText(tracked, "class Tracked { int x = 1; }\n");
            string untracked = WriteFile(repo, "class NewFile {}\n", "src", "demo", "NewFile.cs");
            WriteFile(repo, "ignore me\n", "README.md");

            IReadOnlyList<string> changed = ChangedFileDetector.ChangedCSharpFiles(repo);

            // Ordinal-sorted absolute paths: "...NewFile.cs" precedes "...Tracked.cs" ('N' < 'T'), and
            // README.md is excluded (not ".cs"). Both sides go through Path.GetFullPath so separator
            // normalization matches on Windows.
            changed.Should().Equal(
                Path.GetFullPath(untracked),
                Path.GetFullPath(tracked));
        });
    }

    [Fact]
    public void IncludesGitErrorOutputWhenStatusFails()
    {
        WithTempRepo(repo =>
        {
            // repo exists but was never `git init`-ed, so `git status` fails and its stderr diagnostic
            // must surface in the exception message.
            Action act = () => ChangedFileDetector.ChangedCSharpFiles(repo);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*not a git repository*");
        });
    }

    [Fact]
    public void FiltersChangedFilesToSrcTreeOnly()
    {
        WithTempRepo(repo =>
        {
            RunGit(repo, "init");
            RunGit(repo, "config", "user.email", "test@example.com");
            RunGit(repo, "config", "user.name", "test");

            string tracked = WriteFile(repo, "class Tracked {}\n", "src", "demo", "Tracked.cs");

            RunGit(repo, "add", ".");
            RunGit(repo, "commit", "-m", "init");

            File.WriteAllText(tracked, "class Tracked { int x = 1; }\n");
            WriteFile(repo, "class Something {}\n", "tests", "Something.cs");

            IReadOnlyList<string> changed = ChangedFileDetector.ChangedCSharpFilesUnderSrc(repo);

            // The changed file outside src (tests/Something.cs) is filtered out; only the src file
            // survives.
            changed.Should().Equal(Path.GetFullPath(tracked));
        });
    }

    [Fact]
    public void CandidateLineRequiresAtLeastFourCharacters()
    {
        ChangedFileDetector.IsCandidateLine(null).Should().BeFalse();
        ChangedFileDetector.IsCandidateLine(string.Empty).Should().BeFalse();
        ChangedFileDetector.IsCandidateLine("abc").Should().BeFalse();
        ChangedFileDetector.IsCandidateLine("abcd").Should().BeTrue();
    }

    [Fact]
    public void RenameTargetUsesReplacementSideEvenAtStartOfString()
    {
        ChangedFileDetector.RenameTarget("src/Old.cs -> src/New.cs").Should().Be("src/New.cs");
        ChangedFileDetector.RenameTarget(" -> New.cs").Should().Be("New.cs");
        ChangedFileDetector.RenameTarget("Plain.cs").Should().Be("Plain.cs");
    }

    // cross-platform: Java uses sh -c "git ..."; invoke git directly with the same commands. Captures
    // both streams before waiting (same deadlock-free pattern as the detector) and throws on non-zero
    // exit so setup failures are loud.
    private static void RunGit(string dir, params string[] args)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            WorkingDirectory = dir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        string stdout = outputTask.GetAwaiter().GetResult();
        string stderr = errorTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', args)} failed: {stdout}{stderr}");
        }
    }

    private static void WithTempRepo(Action<string> test)
    {
        string repo = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repo);
        try
        {
            test(repo);
        }
        finally
        {
            if (Directory.Exists(repo))
            {
                // git marks pack/object files read-only (notably on Windows), which blocks a plain
                // recursive delete; clear attributes first so cleanup always succeeds.
                foreach (string file in Directory.EnumerateFiles(repo, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(repo, recursive: true);
            }
        }
    }

    private static string WriteFile(string root, string content, params string[] relativeSegments)
    {
        string path = Path.Combine(root, Path.Combine(relativeSegments));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}

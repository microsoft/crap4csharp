namespace Microsoft.Crap4CSharp;

using System.Diagnostics;

// Faithful port of crap4java's ChangedFileDetector. Java's non-instantiable `final class` with a
// private ctor maps to the idiomatic C# `static class` (public because the `internal` modifier is
// banned in the single-assembly design -- ratified decision C1). It shells out to `git status
// --porcelain` and turns the porcelain lines into the set of changed C# source files. Java analyzed
// ".java"; this port analyzes ".cs" and mirrors SourceFileFinder's `CSharp` naming.
//
// Two ratified adaptations from the Java original (both behavior-preserving):
//   * Deadlock-free capture (W15). Java calls waitFor() and only THEN readAllBytes() -- a latent
//     full-pipe deadlock if git ever wrote more than a pipe buffer. In C# BOTH stream reads are
//     started (asynchronously) BEFORE WaitForExit, so the pipes drain concurrently and can never
//     wedge the child.
//   * Merged error output. Java sets redirectErrorStream(true), so git's "fatal: not a git
//     repository ..." message rides the captured stdout into the thrown exception. C# keeps stdout
//     and stderr separate, then concatenates them (stdout + stderr) into the exception message to
//     reproduce that behavior. Java's IllegalStateException maps to C# InvalidOperationException.
//
// Paths are returned as absolute, ordinal-sorted strings (Path.GetFullPath), consistent with
// SourceFileFinder and the cross-OS determinism departure recorded in docs/decisions.md (#3).
public static class ChangedFileDetector
{
    // Ports changedJavaFiles(Path): runs `git status --porcelain` in projectRoot and returns the
    // absolute paths of every changed ".cs" file, ordinal-sorted.
    public static IReadOnlyList<string> ChangedCSharpFiles(string projectRoot)
    {
        // Java implicitly throws NullPointerException when dereferencing projectRoot; the idiomatic C#
        // equivalent (and CA1062's required guard, since suppressions are banned) is a fail-fast
        // ArgumentNullException.
        ArgumentNullException.ThrowIfNull(projectRoot);

        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(projectRoot);
        startInfo.ArgumentList.Add("status");
        startInfo.ArgumentList.Add("--porcelain");

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        // W15: begin draining BOTH streams before waiting, so a full pipe buffer can never deadlock
        // the child (this deliberately does NOT copy Java's wait-then-read ordering).
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        string stdout = outputTask.GetAwaiter().GetResult();
        string stderr = errorTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
        {
            // Merge stdout + stderr so git's diagnostic (which it writes to stderr) reaches the caller,
            // reproducing Java's redirectErrorStream(true) behavior.
            string combined = stdout + stderr;
            throw new InvalidOperationException("git status failed: " + combined);
        }

        // Mirror Java's split("\\R"): treat "\r\n", "\n" and "\r" as line breaks ("\r\n" first so it is
        // consumed as a single terminator). RemoveEmptyEntries is intentionally NOT used -- the
        // candidate-line check filters blanks.
        string[] lineTerminators = ["\r\n", "\n", "\r"];
        List<string> files = [];
        foreach (string line in stdout.Split(lineTerminators, StringSplitOptions.None))
        {
            string? file = ParseStatusLine(projectRoot, line);
            if (file is not null)
            {
                files.Add(file);
            }
        }

        return [.. files.OrderBy(path => path, StringComparer.Ordinal)];
    }

    // Ports changedJavaFilesUnderSrc(Path): keeps only the changed files that live under
    // <projectRoot>/src. Java uses Path.startsWith, which is SEGMENT-based, so a naive string prefix
    // would be wrong ("srcfoo" must NOT match "src"); the check below is segment-aware.
    public static IReadOnlyList<string> ChangedCSharpFilesUnderSrc(string projectRoot)
    {
        ArgumentNullException.ThrowIfNull(projectRoot);

        string src = Path.GetFullPath(Path.Combine(projectRoot, "src"));
        return [.. ChangedCSharpFiles(projectRoot).Where(path => IsUnderSrc(src, path))];
    }

    // Ports isCandidateLine: null -> false; blank/whitespace-only -> false; otherwise true only when
    // the line is at least four characters long (the two status chars + a space + at least one path
    // char).
    public static bool IsCandidateLine(string? line)
    {
        if (line is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        return line.Length >= 4;
    }

    // Ports renameTarget: for a rename entry ("old -> new"), return the replacement/target side; when
    // there is no " -> " marker, return the path unchanged. The search is Ordinal (Java's indexOf is a
    // plain ordinal scan, unlike C#'s culture-sensitive default).
    public static string RenameTarget(string pathPart)
    {
        ArgumentNullException.ThrowIfNull(pathPart);

        int index = pathPart.IndexOf(" -> ", StringComparison.Ordinal);
        if (index < 0)
        {
            return pathPart;
        }

        return pathPart.Substring(index + 4);
    }

    // Ports parseStatusLine: skip the 2 status chars + 1 space (substring(3)), resolve rename targets,
    // keep only ".cs" paths, and return the absolute normalized path. Returns null for lines that are
    // not changed C# files.
    private static string? ParseStatusLine(string root, string line)
    {
        if (!IsCandidateLine(line))
        {
            return null;
        }

        string pathPart = line.Substring(3).Trim();
        string finalPath = RenameTarget(pathPart);
        if (!IsCSharpPath(finalPath))
        {
            return null;
        }

        return Path.GetFullPath(Path.Combine(root, finalPath));
    }

    // Ports isJavaPath, adapted to ".cs". EndsWith(".cs", Ordinal) mirrors Java's endsWith(".java")
    // exactly (and, like SourceFileFinder, dodges the Windows "*.cs" glob quirk that also matches
    // ".csproj").
    private static bool IsCSharpPath(string path)
    {
        return path.EndsWith(".cs", StringComparison.Ordinal);
    }

    // Segment-aware counterpart to Java's path.startsWith(projectRoot/src): a file is under src only
    // when its path relative to src neither escapes with ".." nor is rooted on a different volume.
    private static bool IsUnderSrc(string src, string file)
    {
        string relative = Path.GetRelativePath(src, file)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(relative))
        {
            return false;
        }

        string[] segments = relative.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);

        return segments.Length > 0 && !string.Equals(segments[0], "..", StringComparison.Ordinal);
    }
}

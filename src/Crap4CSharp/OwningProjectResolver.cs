namespace Microsoft.Crap4CSharp;

// Outcome of OwningProjectResolver.ResolveOwningProjects: the distinct ordinal-sorted owning-project .csproj
// paths, plus the analyzed files that have no owning .csproj within bounds. UnownedFiles non-empty => any
// analyzed file is unowned, so CliApplication.Execute fail-fasts (finding #2; departure #1 fail-fast family).
public readonly record struct OwningProjectResolution(
    IReadOnlyList<string> OwningProjects,
    IReadOnlyList<string> UnownedFiles);

// Model B (departure #9) replacement for the retired ModuleRootResolver. Where the Java original resolved a
// Maven MODULE ROOT via an upward pom.xml walk, and the retired resolver returned a ".sln"/".csproj" directory
// via an UNBOUNDED walk (departure #6, now superseded), this type returns the nearest OWNING PROJECT -- the
// absolute .csproj FILE path -- for each analyzed C# file, BOUNDED so the walk never climbs above the
// invocation root (fixes the S6 ancestor climb). ".sln" is no longer a marker; the owning unit in the .NET
// ecosystem is a .csproj.
//
// Anti-glob discipline (mirrors SourceFileFinder / CoverageReportLocator): a directory's .csproj is found via
// EnumerateFiles + EndsWith(".csproj", Ordinal), NOT a "*.csproj" glob -- the glob has a Windows 8.3-shortname
// quirk and would also match ".csproj.bak"/".csprojx" backups. StringComparer.Ordinal throughout (departure #3
// determinism); all path comparisons are post-Path.GetFullPath from the same on-disk casing, so Ordinal is safe.
//
// There is NO start-directory fallback (departure #6's B1 fallback is gone): a directory that is not a project
// is not an "owning project". No owner within bounds => the file is surfaced as UNOWNED in the result; when ANY
// analyzed file is unowned, CliApplication.Execute fail-fasts (finding #2; departure #1 fail-fast family, exit 1)
// rather than silently dropping it and misattributing all-N/A metrics.
public static class OwningProjectResolver
{
    // Owner resolution for the analyzed files: distinct ordinal-sorted owning-project .csproj paths plus any
    // files with no owner within bounds. OwningProjects empty => none within bounds.
    public static OwningProjectResolution ResolveOwningProjects(
        IReadOnlyList<string> files, string invocationRoot)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(invocationRoot);

        string root = Path.GetFullPath(invocationRoot);
        HashSet<string> owners = new(StringComparer.Ordinal);
        List<string> unowned = [];
        foreach (string file in files)
        {
            string? owner = ResolveOwningProject(file, root);
            if (owner is not null)
            {
                owners.Add(owner);
            }
            else
            {
                unowned.Add(file); // preserves upstream ordinal order (files already distinct+sorted)
            }
        }

        return new OwningProjectResolution(
            [.. owners.OrderBy(p => p, StringComparer.Ordinal)],
            unowned);
    }

    // Nearest .csproj at/above `startPath`, bounded so the walk never leaves the invocationRoot subtree.
    // Returns the absolute .csproj FILE path, or null when none is found within bounds.
    public static string? ResolveOwningProject(string startPath, string invocationRoot)
    {
        ArgumentNullException.ThrowIfNull(startPath);
        ArgumentNullException.ThrowIfNull(invocationRoot);

        string root = Path.GetFullPath(invocationRoot);
        string full = Path.GetFullPath(startPath);
        string start = (File.Exists(full) ? Path.GetDirectoryName(full) : null) ?? full;

        for (string? current = start; current is not null; current = Path.GetDirectoryName(current))
        {
            if (!IsWithin(current, root))
            {
                break; // bounded: never climb above the invocation root (fixes the S6 ancestor climb)
            }

            string? project = NearestProjectFile(current);
            if (project is not null)
            {
                return project;
            }

            if (string.Equals(current, root, StringComparison.Ordinal))
            {
                break; // inclusive of root, then stop
            }
        }

        return null;
    }

    // The lexically-first .csproj FILE directly in `directory`, or null. Anti-glob: EnumerateFiles("*") +
    // EndsWith(".csproj", Ordinal) dodges the 8.3-shortname quirk and rejects ".csproj.bak"/backup names. A
    // missing directory yields null rather than throwing (mirrors the non-throwing marker probe discipline).
    // Ordinal-first when a directory holds more than one .csproj (rare; deterministic).
    private static string? NearestProjectFile(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(directory, "*")
            .Where(file => file.EndsWith(".csproj", StringComparison.Ordinal))
            .OrderBy(file => file, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    // True when `candidate` is `root` itself (ordinal) or a descendant of `root` (starts with
    // root + directory separator, ordinal). Both sides are pre-normalized via Path.GetFullPath. Guards the
    // bound and the "file outside root" edge (such a file contributes no owner).
    private static bool IsWithin(string candidate, string root)
    {
        return string.Equals(candidate, root, StringComparison.Ordinal)
            || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}

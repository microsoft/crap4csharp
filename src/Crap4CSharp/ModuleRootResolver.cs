namespace Microsoft.Crap4CSharp;

// Faithful port of crap4java's CliApplication.moduleRootFor(workspaceRoot, file). Java climbs upward
// from the file (or the file itself when it is a directory) toward a bounded workspaceRoot, returning
// the nearest ancestor that contains the module marker "pom.xml"; if none is found it falls back to the
// workspaceRoot. Two ratified adaptations for the .NET ecosystem (see docs/decisions.md, Module root):
//
//   * Marker swap -- the module marker becomes ".sln" (primary) with ".csproj" as the fallback marker,
//     because that is what lets `dotnet test` actually run for a module. A ".sln" ANYWHERE up the chain
//     beats a NEARER ".csproj", mirroring the single-marker precedence of the Java original: only when no
//     ".sln" exists anywhere do we settle for the nearest ".csproj".
//
//   * B1 -- unbounded walk. Java bounded the climb to workspaceRoot and fell back to it; we intentionally
//     do NOT bound the walk (it climbs to the filesystem root) and fall back to the STARTING directory
//     when no marker is found anywhere. Approved-as-specced departure (Mr. Das ratifies at the boundary).
//
// Marker detection mirrors SourceFileFinder's EndsWith(".ext", Ordinal) discipline rather than a
// "*.sln" glob: the glob has a Windows 8.3-shortname quirk and would also let ".slnx"/".slnf" match,
// whereas EndsWith(".sln", Ordinal) matches ".sln" exactly and nothing longer.
public static class ModuleRootResolver
{
    // Ports moduleRootFor(Path, Path): resolves the module root directory for a start location that may be
    // a directory or a file. Returns an absolute, normalized directory path.
    public static string Resolve(string startDirectory)
    {
        // Java implicitly throws NullPointerException on workspaceRoot.normalize(); the idiomatic,
        // suppression-free C# equivalent (and CA1062's required guard) is a fail-fast ArgumentNullException.
        ArgumentNullException.ThrowIfNull(startDirectory);

        string full = Path.GetFullPath(startDirectory);

        // Mirror Java's `Files.isDirectory(file) ? file : file.getParent()`: when the start is an existing
        // file, begin from its parent directory; otherwise treat the normalized path as the start directory.
        string start = (File.Exists(full) ? Path.GetDirectoryName(full) : null) ?? full;

        string? nearestProject = null;

        // Single upward walk, inclusive of the starting directory, to the filesystem root (B1: unbounded).
        for (string? current = start; current is not null; current = Path.GetDirectoryName(current))
        {
            // A ".sln" is the winning marker: the first one seen while climbing is the nearest, and it
            // outranks any ".csproj" recorded so far (".sln" beats even a nearer ".csproj").
            if (ContainsMarker(current, ".sln"))
            {
                return current;
            }

            // Remember only the FIRST (nearest) ".csproj" ancestor -- the fallback used if no ".sln" exists.
            if (nearestProject is null && ContainsMarker(current, ".csproj"))
            {
                nearestProject = current;
            }
        }

        // No ".sln" anywhere: use the nearest ".csproj" if one was found, else fall back to the starting
        // directory (B1 terminal fallback, in place of Java's workspaceRoot).
        return nearestProject ?? start;
    }

    // True when the directory directly contains a file whose name ends with the given marker extension.
    // Uses EnumerateFiles + EndsWith(Ordinal) -- NOT a "*.sln"/"*.csproj" glob -- to dodge the Windows
    // 8.3-shortname quirk and to keep ".slnx"/".slnf" from ever matching ".sln". A missing directory
    // yields false rather than throwing, mirroring Java's non-throwing Files.exists(...) marker probe.
    private static bool ContainsMarker(string directory, string extension)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        return Directory
            .EnumerateFiles(directory, "*")
            .Any(file => file.EndsWith(extension, StringComparison.Ordinal));
    }
}

namespace Microsoft.Crap4CSharp;

using System.Xml;
using System.Xml.Linq;

// Model B (departure #9) test-project resolver: given an owning project (.csproj), find the test project that
// exercises it, following the naming convention <Project>.Tests.csproj OR <Project>.UnitTests.csproj whose
// ProjectReferences TRANSITIVELY include the owning project. Has no Java counterpart (crap4java ran ALL of a
// resolved Maven module's tests); this is the C#-ecosystem adaptation that lets `dotnet test <TestProject>`
// run the one test project for the analyzed code unit.
//
// Search is bounded to the invocation root and deterministic: enumerate .csproj under the root (anti-glob
// EndsWith(".csproj", Ordinal)), excluding bin/obj segments (parity with departure #5), filter to the two
// candidate names, keep those transitively referencing the owning project, ordinal-first tie-break. No match
// => null (CliApplication.Execute fail-fasts, departure #1, exit 1). StringComparer.Ordinal throughout
// (departure #3); missing/unparseable referenced .csproj is treated as zero references (skip, never throw).
public static class TestProjectResolver
{
    // Resolves <Project>.Tests.csproj OR <Project>.UnitTests.csproj (under invocationRoot) whose
    // ProjectReferences TRANSITIVELY include owningProject. Returns the test-project .csproj path, or null.
    public static string? ResolveTestProject(string owningProject, string invocationRoot)
    {
        ArgumentNullException.ThrowIfNull(owningProject);
        ArgumentNullException.ThrowIfNull(invocationRoot);

        string project = Path.GetFileNameWithoutExtension(owningProject);
        string dotTests = project + ".Tests";
        string dotUnitTests = project + ".UnitTests";
        string owner = Path.GetFullPath(owningProject);
        string root = Path.GetFullPath(invocationRoot);

        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".csproj", StringComparison.Ordinal))
            .Where(file => !HasBuildOutputSegment(file))
            .Where(file =>
            {
                string name = Path.GetFileNameWithoutExtension(file);
                return string.Equals(name, dotTests, StringComparison.Ordinal)
                    || string.Equals(name, dotUnitTests, StringComparison.Ordinal);
            })
            .Where(candidate => ReferencesTransitively(candidate, owner))
            .OrderBy(file => file, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    // Cycle-safe transitive ProjectReference reachability from fromProject to targetProject. Both paths are
    // normalized via Path.GetFullPath before comparison. Determinism: boolean membership, order-independent.
    private static bool ReferencesTransitively(string fromProject, string targetProject)
    {
        string target = Path.GetFullPath(targetProject);
        HashSet<string> visited = new(StringComparer.Ordinal);
        Stack<string> frontier = new();
        frontier.Push(Path.GetFullPath(fromProject));

        while (frontier.Count > 0)
        {
            string current = frontier.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            foreach (string reference in DirectProjectReferences(current))
            {
                if (string.Equals(reference, target, StringComparison.Ordinal))
                {
                    return true;
                }

                frontier.Push(reference);
            }
        }

        return false;
    }

    // The literal <ProjectReference Include="..."/> targets of a .csproj, resolved to absolute normalized
    // paths. Namespace-agnostic (Descendants().LocalName -- SDK-style has no namespace, legacy has the MSBuild
    // namespace). Include is relative to the referencing .csproj dir; Replace('\\','/') normalizes Windows
    // separators for cross-OS Path.Combine (departure #3). A missing/unparseable .csproj yields no references
    // (skip, do not throw -- mirrors the non-throwing marker probe discipline). No MSBuild evaluation.
    private static IReadOnlyList<string> DirectProjectReferences(string csprojPath)
    {
        if (!File.Exists(csprojPath))
        {
            return [];
        }

        XDocument doc;
        try
        {
            doc = XDocument.Load(csprojPath);
        }
        catch (XmlException)
        {
            // An EXISTING-but-malformed .csproj (mid-edit, merge markers, truncated) contributes zero
            // references -- the same non-throwing marker-probe discipline as the File.Exists guard
            // above, so a stray unparseable candidate can never abort speculative resolution. Specific
            // XmlException only (CA1031-clean, no suppression); FS faults (IOException) still propagate
            // to the T20 catch-all.
            return [];
        }

        string dir = Path.GetDirectoryName(Path.GetFullPath(csprojPath))!;
        return
        [
            .. doc.Descendants()
                .Where(e => string.Equals(e.Name.LocalName, "ProjectReference", StringComparison.Ordinal))
                .Select(e => (string?)e.Attribute("Include"))
                .Where(include => !string.IsNullOrEmpty(include))
                .Select(include => Path.GetFullPath(Path.Combine(dir, include!.Replace('\\', '/'))))
        ];
    }

    // True when any path segment is exactly "bin" or "obj" (ordinal), matching departure #5's build-output
    // exclusion so a stray generated .csproj under bin/ or obj/ is never treated as a test project.
    private static bool HasBuildOutputSegment(string path)
    {
        foreach (string segment in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (string.Equals(segment, "bin", StringComparison.Ordinal)
                || string.Equals(segment, "obj", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

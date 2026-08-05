namespace Microsoft.Crap4CSharp;

using System.Text.RegularExpressions;
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
// (departure #3) EXCEPT MSBuild property-NAME lookup, which is OrdinalIgnoreCase per MSBuild semantics
// (NOT a #3 regression -- every path/value stays Ordinal); missing/unparseable referenced .csproj is
// treated as zero references (skip, never throw).
//
// D-T29a (folds under departure #9): before matching each <ProjectReference Include>, the resolver
// evaluates a BOUNDED MSBuild property set so a legitimate $(RepoRoot)/$(MSBuildThisFileDirectory)-based
// Include resolves. Two stages, per csproj N: (1) build a fixpoint-expanded property map = the file-scoped
// intrinsic ten + user-defined props from the invocation-root-bounded Directory.Build.props ancestor chain
// (parent->nearest) then N's own <PropertyGroup>s (last-writer-wins, so the csproj body overrides props
// files); (2) a single regex pass expands each Include over that map. Any unevaluable token (undefined,
// unknown, non-self-guard-conditioned, cyclic, depth-capped, $(SolutionDir)-without-explicit-def, property
// function, item/metadata, env/global prop) is retained VERBATIM -> a non-existent path -> a dropped edge
// -> clean exit 1 (FAIL-SAFE; never throws, never guesses a match). New I/O (Directory.Build.props reads)
// swallows XmlException ONLY, like the existing csproj probe; FS faults propagate to the T20 catch-all.
public static partial class TestProjectResolver
{
    // Belt-and-suspenders cap on the Stage-1 fixpoint recursion depth: guards a pathological deep linear
    // property chain (A1 -> A2 -> ...) that the in-progress cycle set would not catch. At the cap, any
    // remaining $(...) tokens stay VERBATIM -> a non-existent path -> a dropped edge (fail-safe).
    private const int MaxExpansionDepth = 64;

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

        ResolutionContext context = new(root);

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
            .Where(candidate => ReferencesTransitively(candidate, owner, context))
            .OrderBy(file => file, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    // Cycle-safe transitive ProjectReference reachability from fromProject to targetProject. Both paths are
    // normalized via Path.GetFullPath before comparison. Determinism: boolean membership, order-independent.
    private static bool ReferencesTransitively(
        string fromProject, string targetProject, ResolutionContext context)
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

            foreach (string reference in DirectProjectReferences(current, context))
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

    // Memoized entry point: the literal <ProjectReference Include="..."/> targets of a .csproj, resolved to
    // absolute normalized paths. The per-ResolveTestProject-call memo means speculative resolution across
    // candidates never re-parses/re-evaluates a project twice (and keeps the resolver free of static state).
    private static IReadOnlyList<string> DirectProjectReferences(string csprojPath, ResolutionContext context)
    {
        string projectFile = Path.GetFullPath(csprojPath);
        if (context.ReferencesByProject.TryGetValue(projectFile, out IReadOnlyList<string>? cached))
        {
            return cached;
        }

        IReadOnlyList<string> references = ReadProjectReferences(projectFile, context);
        context.ReferencesByProject[projectFile] = references;
        return references;
    }

    // Namespace-agnostic (Descendants().LocalName -- SDK-style has no namespace, legacy has the MSBuild
    // namespace). Include is relative to the referencing .csproj dir; Replace('\\','/') normalizes Windows
    // separators for cross-OS Path.Combine (departure #3). A missing/unparseable .csproj yields no references
    // (skip, do not throw -- mirrors the non-throwing marker probe discipline).
    private static IReadOnlyList<string> ReadProjectReferences(string projectFile, ResolutionContext context)
    {
        if (!File.Exists(projectFile))
        {
            return [];
        }

        XDocument doc;
        try
        {
            doc = XDocument.Load(projectFile);
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

        // Stage 1 (D-T29a): resolve this csproj's MSBuild property map (intrinsic ten + bounded user props).
        Dictionary<string, string> properties = ResolvePropertyMap(projectFile, doc, context);

        // Stage 2 (D-T29a): expand each Include over that map (single pass), THEN the pre-existing
        // Replace('\\','/') -> Path.Combine -> Path.GetFullPath tail, UNCHANGED.
        string dir = Path.GetDirectoryName(projectFile)!;
        return
        [
            .. doc.Descendants()
                .Where(e => string.Equals(e.Name.LocalName, "ProjectReference", StringComparison.Ordinal))
                .Select(e => (string?)e.Attribute("Include"))
                .Where(include => !string.IsNullOrEmpty(include))
                .Select(include => ExpandInclude(include!, properties))
                .Select(include => Path.GetFullPath(Path.Combine(dir, include.Replace('\\', '/'))))
        ];
    }

    // Stage 1 (D-T29a). Builds name -> terminal-value for csproj N. Precedence (earliest-imported first,
    // last-writer-wins): the Directory.Build.props ancestor chain (parent -> nearest), then N's own
    // <PropertyGroup>s (so the csproj body overrides props files). Each raw definition is fixpoint-expanded;
    // the N-scoped intrinsic ten are overlaid last so reserved props win over any user shadow. Property NAMES
    // compare OrdinalIgnoreCase (MSBuild semantics -- the ONLY OrdinalIgnoreCase here; every value stays
    // Ordinal, NOT a departure #3 regression).
    private static Dictionary<string, string> ResolvePropertyMap(
        string projectFile, XDocument projectDoc, ResolutionContext context)
    {
        Dictionary<string, string> rawValues = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> definingFiles = new(StringComparer.OrdinalIgnoreCase);

        foreach (string propsFile in DirectoryBuildPropsChain(projectFile, context.Root))
        {
            CollectDefinitions(propsFile, LoadPropertyGroups(propsFile), rawValues, definingFiles);
        }

        CollectDefinitions(projectFile, PropertyGroupsOf(projectDoc), rawValues, definingFiles);

        // Fixpoint-expand every raw definition to a terminal value. Deterministic: a FRESH in-progress set
        // per property (pure function of the ordered raw map), so a cyclic/self-referential token resolves
        // to a stable verbatim residue rather than depending on entry order.
        Dictionary<string, string> resolved = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in rawValues.Keys)
        {
            resolved[name] = ExpandValue(
                rawValues[name],
                definingFiles[name],
                rawValues,
                definingFiles,
                projectFile,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name },
                depth: 0);
        }

        foreach (KeyValuePair<string, string> intrinsic in IntrinsicProperties(projectFile, projectFile))
        {
            resolved[intrinsic.Key] = intrinsic.Value;
        }

        return resolved;
    }

    // The Directory.Build.props chain for N: every such file from N's own directory up to (and INCLUDING)
    // the invocation root, ordered outermost (root) -> nearest. BOUNDED -- it never climbs above the root
    // (a props file above the root is a documented fail-safe residual), so it never reaches the drive root.
    // Fixed filename + File.Exists (no glob) => deterministic, no enumeration-order ambiguity.
    private static List<string> DirectoryBuildPropsChain(string projectFile, string root)
    {
        List<string> chain = [];
        string? current = Path.GetDirectoryName(projectFile);
        while (current is not null && IsAtOrUnder(current, root))
        {
            string candidate = Path.Combine(current, "Directory.Build.props");
            if (File.Exists(candidate))
            {
                chain.Add(candidate);
            }

            if (string.Equals(current, root, StringComparison.Ordinal))
            {
                break;
            }

            string? parent = Path.GetDirectoryName(current);
            if (parent is null || string.Equals(parent, current, StringComparison.Ordinal))
            {
                break;
            }

            current = parent;
        }

        chain.Reverse();
        return chain;
    }

    private static bool IsAtOrUnder(string path, string root)
    {
        return string.Equals(path, root, StringComparison.Ordinal)
            || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    // Reads a Directory.Build.props file's direct-child <PropertyGroup>s. NEW I/O -> the SAME non-throwing
    // discipline as the csproj probe: a malformed props file (XmlException) contributes ZERO properties, so
    // a valid sibling in the chain still applies. XmlException ONLY (CA1031-clean); genuine FS faults
    // (IOException/UnauthorizedAccessException) propagate to the T20 catch-all. File.Exists is the caller's.
    private static IEnumerable<XElement> LoadPropertyGroups(string propsFile)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(propsFile);
        }
        catch (XmlException)
        {
            return [];
        }

        return PropertyGroupsOf(doc);
    }

    // Direct-child <PropertyGroup>s of <Project> only: groups nested in <Target>/<Choose>/<When> are
    // target-/condition-scoped and intentionally NOT evaluated (fail-safe). Namespace-agnostic (LocalName).
    private static IEnumerable<XElement> PropertyGroupsOf(XDocument doc)
    {
        if (doc.Root is null)
        {
            return [];
        }

        return doc.Root.Elements()
            .Where(e => string.Equals(e.Name.LocalName, "PropertyGroup", StringComparison.Ordinal));
    }

    // Records each honored property definition into rawValues/definingFiles (last-writer-wins). CONDITION
    // rule (Mr. Das): a property is honored iff it carries NO Condition, OR every Condition present (on the
    // property element AND/OR its PropertyGroup) is exactly the self-emptiness guard '$(X)'=='' for the
    // property's OWN name X while X is not yet set. Every other conditioned property/group is SKIPPED
    // (fail-safe -> its $(...) stays verbatim downstream). The DEFINING FILE is recorded for file-scoped
    // intrinsics (MSBuildThisFile* resolve against it).
    private static void CollectDefinitions(
        string definingFile,
        IEnumerable<XElement> propertyGroups,
        Dictionary<string, string> rawValues,
        Dictionary<string, string> definingFiles)
    {
        foreach (XElement group in propertyGroups)
        {
            string? groupCondition = group.Attribute("Condition")?.Value;
            foreach (XElement property in group.Elements())
            {
                string name = property.Name.LocalName;
                if (!ConditionAllows(name, groupCondition, rawValues)
                    || !ConditionAllows(name, property.Attribute("Condition")?.Value, rawValues))
                {
                    continue;
                }

                rawValues[name] = property.Value;
                definingFiles[name] = definingFile;
            }
        }
    }

    private static bool ConditionAllows(string name, string? condition, Dictionary<string, string> rawValues)
    {
        if (condition is null)
        {
            return true;
        }

        // A present Condition is honored ONLY as the self-emptiness guard '$(name)'=='' applied while name
        // is still unset; every other condition -> the property is skipped (fail-safe).
        return !rawValues.ContainsKey(name) && IsSelfEmptinessGuard(condition, name);
    }

    private static bool IsSelfEmptinessGuard(string condition, string name)
    {
        Match match = SelfEmptinessGuardRegex().Match(condition);
        return match.Success
            && string.Equals(match.Groups[1].Value.Trim(), name, StringComparison.OrdinalIgnoreCase);
    }

    // The ten reserved path-derived MSBuild properties, FILE-SCOPED (D-T29a). MSBuildThisFile* describe the
    // file that DEFINES the property being expanded (definingFile); MSBuildProject* ALWAYS describe the csproj
    // N (projectFile) -- this is why RepoRoot=$(MSBuildThisFileDirectory) in a repo-root Directory.Build.props
    // resolves to the props file's dir, not N's. Load-bearing: MSBuildThisFileDirectory carries a TRAILING
    // separator; MSBuildProjectDirectory does NOT. Names OrdinalIgnoreCase; values Ordinal.
    private static Dictionary<string, string> IntrinsicProperties(string definingFile, string projectFile)
    {
        string thisDirectory = Path.GetDirectoryName(definingFile)!;
        string projectDirectory = Path.GetDirectoryName(projectFile)!;
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MSBuildThisFileDirectory"] = thisDirectory + Path.DirectorySeparatorChar,
            ["MSBuildProjectDirectory"] = projectDirectory,
            ["MSBuildThisFileFullPath"] = definingFile,
            ["MSBuildProjectFullPath"] = projectFile,
            ["MSBuildThisFile"] = Path.GetFileName(definingFile),
            ["MSBuildProjectFile"] = Path.GetFileName(projectFile),
            ["MSBuildThisFileName"] = Path.GetFileNameWithoutExtension(definingFile),
            ["MSBuildProjectName"] = Path.GetFileNameWithoutExtension(projectFile),
            ["MSBuildThisFileExtension"] = Path.GetExtension(definingFile),
            ["MSBuildProjectExtension"] = Path.GetExtension(projectFile),
        };
    }

    // Fixpoint DFS expanding one raw property value to terminal. Base case = the file-scoped intrinsic ten.
    // A token that recurses into an in-progress name (cycle/self-ref), exceeds the depth cap, or names an
    // unknown property (env/global/undefined/$(SolutionDir)-without-def/property function/item-metadata) is
    // left VERBATIM (match.Value). Regex.Replace does not re-scan substituted text.
    private static string ExpandValue(
        string value,
        string definingFile,
        Dictionary<string, string> rawValues,
        Dictionary<string, string> definingFiles,
        string projectFile,
        HashSet<string> inProgress,
        int depth)
    {
        if (depth >= MaxExpansionDepth)
        {
            return value;
        }

        Dictionary<string, string> intrinsics = IntrinsicProperties(definingFile, projectFile);
        return PropertyReferenceRegex().Replace(value, match =>
        {
            string token = match.Groups[1].Value;
            if (intrinsics.TryGetValue(token, out string? intrinsicValue))
            {
                return intrinsicValue;
            }

            if (inProgress.Contains(token) || !rawValues.TryGetValue(token, out string? raw))
            {
                return match.Value;
            }

            HashSet<string> next = new(inProgress, StringComparer.OrdinalIgnoreCase) { token };
            return ExpandValue(raw, definingFiles[token], rawValues, definingFiles, projectFile, next, depth + 1);
        });
    }

    // Stage 2 (D-T29a). Single regex pass over the raw Include using the terminal property map. Known token
    // -> value; UNKNOWN token -> VERBATIM (match.Value) -> a non-existent path downstream -> a dropped edge.
    // No fixpoint needed (Stage 1 already drove user props terminal).
    private static string ExpandInclude(string include, Dictionary<string, string> properties)
    {
        return PropertyReferenceRegex().Replace(include, match =>
        {
            return properties.TryGetValue(match.Groups[1].Value, out string? value) ? value : match.Value;
        });
    }

    // Matches a single MSBuild property reference $(Name). [^)]+ stops at the first ')', so a property
    // function $([T]::M(...)) captures only up to that ')' -> not a known name -> the token is retained
    // VERBATIM (fail-safe). Mirrors the CoberturaCoverageParser [GeneratedRegex]+partial idiom.
    [GeneratedRegex(@"\$\(([^)]+)\)")]
    private static partial Regex PropertyReferenceRegex();

    // Matches ONLY the narrow self-emptiness guard Condition="'$(X)'==''" (the ubiquitous RepoRoot idiom);
    // the captured X is compared OrdinalIgnoreCase to the property's own name. Every other condition
    // (including the reversed ''=='$(X)') fails to match -> the property is skipped (fail-safe).
    [GeneratedRegex(@"^\s*'\$\(([^)]+)\)'\s*==\s*''\s*$")]
    private static partial Regex SelfEmptinessGuardRegex();

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

    // Per-ResolveTestProject-call state (guardrail #9: NO static mutable state). Carries the invocation root
    // (the upper bound of the Directory.Build.props walk) and memoizes each csproj's resolved
    // ProjectReference set so speculative resolution across candidates never re-parses/re-evaluates a project.
    private sealed class ResolutionContext(string root)
    {
        public string Root { get; } = root;

        public Dictionary<string, IReadOnlyList<string>> ReferencesByProject { get; } =
            new(StringComparer.Ordinal);
    }
}

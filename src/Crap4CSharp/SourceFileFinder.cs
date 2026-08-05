namespace Microsoft.Crap4CSharp;

// Faithful port of crap4java's SourceFileFinder. Java's non-instantiable `final class` with a private
// ctor maps to the idiomatic C# `static class`. Resolves projectRoot/src; if it does not exist, returns
// an empty list; otherwise it recursively enumerates every file and keeps those whose path ends with
// ".cs" -- an EndsWith test, NOT a "*.cs" glob, to mirror Java's endsWith(".java") exactly and to dodge
// the Windows 8.3 quirk where a "*.cs" glob can also match longer extensions (e.g. ".csproj"). Results
// are sorted with StringComparer.Ordinal for cross-platform determinism.
//
// One deliberate C# departure from Java (Java's src has no build output): files living under any
// directory segment named exactly "bin" or "obj" (case-sensitive, at any depth) are excluded, so
// generated build output never pollutes the analysis. Absolute paths are returned, mirroring the
// absolute java.nio.Paths the original produced.
public static class SourceFileFinder
{
    // Ports findAllJavaFilesUnderSrc(Path), adapted to C# ".cs" files plus the bin/obj exclusion.
    public static IReadOnlyList<string> FindAllCSharpFilesUnderSrc(string projectRoot)
    {
        // Java implicitly throws NullPointerException on projectRoot.resolve(...); the idiomatic C#
        // equivalent (and CA1062's required guard, since suppressions are banned) is a fail-fast
        // ArgumentNullException. Behavior is otherwise identical to crap4java's finder.
        ArgumentNullException.ThrowIfNull(projectRoot);

        string src = Path.Combine(projectRoot, "src");
        if (!Directory.Exists(src))
        {
            return [];
        }

        return
        [
            .. Directory
                .EnumerateFiles(src, "*", SearchOption.AllDirectories)
                .Where(file => file.EndsWith(".cs", StringComparison.Ordinal))
                .Where(file => !IsUnderBuildOutput(src, file))
                .OrderBy(file => file, StringComparer.Ordinal),
        ];
    }

    // True when the file lives under a directory segment named exactly "bin" or "obj" (case-sensitive,
    // at any depth beneath src). The relative path is decomposed into real directory segments -- never a
    // naive substring test -- so lookalikes such as "Robin" or "object" are NOT excluded.
    private static bool IsUnderBuildOutput(string src, string file)
    {
        string relative = Path.GetRelativePath(src, file)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        string[] segments = relative.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string segment in segments)
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

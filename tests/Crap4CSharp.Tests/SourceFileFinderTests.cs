namespace Microsoft.Crap4CSharp.Tests;

// Faithful port of crap4java's SourceFileFinderTest. The one Java test (findsAllJavaFilesUnderSrcOnly)
// is ported as FindsCSharpFilesUnderSrcOnly, with the demo/*.java tree adapted to src/Demo/*.cs. The
// remaining tests pin the C# adaptations the port introduces: bin/obj build-output exclusion, the
// ordinal (not culture) sort, the missing-src empty result, and the EndsWith(".cs") extension filter.
// Each test runs against a fresh temp project root that is always removed in a finally.
public class SourceFileFinderTests
{
    [Fact]
    public void FindsCSharpFilesUnderSrcOnly()
    {
        WithTempProjectRoot(projectRoot =>
        {
            string inSrc = WriteFile(projectRoot, "src", "Demo", "Sample.cs");
            WriteFile(projectRoot, "other", "Elsewhere.cs");

            IReadOnlyList<string> files = SourceFileFinder.FindAllCSharpFilesUnderSrc(projectRoot);

            files.Should().Equal(inSrc);
        });
    }

    [Fact]
    public void ReturnsEmptyWhenSrcDirectoryMissing()
    {
        WithTempProjectRoot(projectRoot =>
        {
            Directory.CreateDirectory(projectRoot);

            IReadOnlyList<string> files = SourceFileFinder.FindAllCSharpFilesUnderSrc(projectRoot);

            files.Should().BeEmpty();
        });
    }

    [Fact]
    public void ExcludesBinAndObjOutputButKeepsSegmentLookalikes()
    {
        WithTempProjectRoot(projectRoot =>
        {
            string real = WriteFile(projectRoot, "src", "App", "Real.cs");
            WriteFile(projectRoot, "src", "App", "bin", "Debug", "Generated.cs");
            WriteFile(projectRoot, "src", "App", "obj", "Sample.g.cs");
            string notExcluded = WriteFile(projectRoot, "src", "Robin", "NotExcluded.cs");

            IReadOnlyList<string> files = SourceFileFinder.FindAllCSharpFilesUnderSrc(projectRoot);

            // Ordinal order: "...src\App\Real.cs" precedes "...src\Robin\NotExcluded.cs" ('A' < 'R').
            // Only these two survive: the bin/ and obj/ files are dropped, while "Robin" (which contains
            // the substring "bin") is kept -- proving the match is segment-exact, not Contains("bin").
            files.Should().Equal(real, notExcluded);
        });
    }

    [Fact]
    public void OrdersResultsByOrdinalComparison()
    {
        WithTempProjectRoot(projectRoot =>
        {
            // These names sort differently under ordinal vs. culture rules: ordinal places every
            // upper-case letter (B=66, Z=90) ahead of lower-case (a=97) -> [Banana, Zebra, apple]; an
            // invariant-culture sort would interleave case -> [apple, Banana, Zebra]. Asserting the
            // ordinal order pins the StringComparer.Ordinal determinism guarantee.
            string apple = WriteFile(projectRoot, "src", "apple.cs");
            string banana = WriteFile(projectRoot, "src", "Banana.cs");
            string zebra = WriteFile(projectRoot, "src", "Zebra.cs");

            IReadOnlyList<string> files = SourceFileFinder.FindAllCSharpFilesUnderSrc(projectRoot);

            files.Should().Equal(banana, zebra, apple);
        });
    }

    [Fact]
    public void KeepsOnlyCSharpFiles()
    {
        WithTempProjectRoot(projectRoot =>
        {
            string source = WriteFile(projectRoot, "src", "App", "Program.cs");
            WriteFile(projectRoot, "src", "App", "README.md");

            // ".csproj" ends with "proj", not ".cs", so EndsWith(".cs") excludes it -- this also guards
            // the Windows 8.3 quirk where a "*.cs" glob could wrongly match longer extensions.
            WriteFile(projectRoot, "src", "App", "App.csproj");

            IReadOnlyList<string> files = SourceFileFinder.FindAllCSharpFilesUnderSrc(projectRoot);

            files.Should().Equal(source);
        });
    }

    private static void WithTempProjectRoot(Action<string> test)
    {
        string projectRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            test(projectRoot);
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    private static string WriteFile(string projectRoot, params string[] relativeSegments)
    {
        string path = Path.Combine(projectRoot, Path.Combine(relativeSegments));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "// test fixture\n");
        return path;
    }
}

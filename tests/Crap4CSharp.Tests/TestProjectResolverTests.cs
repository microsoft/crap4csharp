namespace Microsoft.Crap4CSharp.Tests;

using System.Linq;

// Covers TestProjectResolver (Model B, departure #9): the <Project>.Tests / <Project>.UnitTests naming gate,
// transitive ProjectReference reachability (cycle-safe), the bin/obj + bounded-scope exclusions, ordinal-first
// tie-break, backslash/`..` Include normalization, and the non-throwing missing-reference discipline. Each test
// scaffolds minimal on-disk SDK-style .csproj fixtures in a fresh temp tree, removed in a finally. PascalCase,
// no [Trait].
public class TestProjectResolverTests
{
    [Fact]
    public void ResolvesDotTestsProjectByDirectReference()
    {
        WithTempRoot(root =>
        {
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            string test = WriteProject(
                SubDir(root, "tests", "Foo.Tests"), "Foo.Tests", @"..\..\src\Foo\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().Be(test);
        });
    }

    [Fact]
    public void ResolvesDotUnitTestsProjectVariant()
    {
        WithTempRoot(root =>
        {
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            string test = WriteProject(
                SubDir(root, "tests", "Foo.UnitTests"), "Foo.UnitTests", @"..\..\src\Foo\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().Be(test);
        });
    }

    [Fact]
    public void FollowsTransitiveProjectReferences()
    {
        // Foo.Tests -> Bar -> Foo: the test project references Foo only TRANSITIVELY through Bar.
        WithTempRoot(root =>
        {
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            WriteProject(SubDir(root, "src", "Bar"), "Bar", @"..\Foo\Foo.csproj");
            string test = WriteProject(
                SubDir(root, "tests", "Foo.Tests"), "Foo.Tests", @"..\..\src\Bar\Bar.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().Be(test);
        });
    }

    [Fact]
    public void IgnoresNameMatchWithoutReference()
    {
        WithTempRoot(root =>
        {
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            WriteProject(SubDir(root, "tests", "Foo.Tests"), "Foo.Tests");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().BeNull();
        });
    }

    [Fact]
    public void IgnoresReferencingProjectWithWrongName()
    {
        // Refs Foo, but the name (Foo.IntegrationTests) matches neither .Tests nor .UnitTests -> name gate.
        WithTempRoot(root =>
        {
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            WriteProject(
                SubDir(root, "tests", "Foo.IntegrationTests"),
                "Foo.IntegrationTests",
                @"..\..\src\Foo\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().BeNull();
        });
    }

    [Fact]
    public void IsCycleSafeInProjectReferenceGraph()
    {
        // A <-> B form a reference cycle that never reaches Foo; the BFS must terminate (visited set) and
        // return null rather than looping forever.
        WithTempRoot(root =>
        {
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            WriteProject(SubDir(root, "a"), "A", @"..\b\B.csproj");
            WriteProject(SubDir(root, "b"), "B", @"..\a\A.csproj");
            WriteProject(SubDir(root, "tests", "Foo.Tests"), "Foo.Tests", @"..\..\a\A.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().BeNull();
        });
    }

    [Fact]
    public void ReturnsNullWhenCandidateAboveInvocationRoot()
    {
        // The Foo.Tests project sits ABOVE the invocation root, so the bounded search never sees it.
        WithTempRoot(outer =>
        {
            string invocationRoot = SubDir(outer, "inner");
            string owning = WriteProject(SubDir(invocationRoot, "src", "Foo"), "Foo");
            WriteProject(
                SubDir(outer, "Foo.Tests"), "Foo.Tests", @"..\inner\src\Foo\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, invocationRoot);

            result.Should().BeNull();
        });
    }

    [Fact]
    public void ExcludesBinObjCandidates()
    {
        // A name-matching, correctly-referencing test project under an obj/ segment is excluded (departure #5).
        WithTempRoot(root =>
        {
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            WriteProject(
                SubDir(root, "obj", "Foo.Tests"), "Foo.Tests", @"..\..\src\Foo\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().BeNull();
        });
    }

    [Fact]
    public void PicksOrdinalFirstWhenMultipleQualify()
    {
        WithTempRoot(root =>
        {
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            string tests = WriteProject(
                SubDir(root, "tests", "Foo.Tests"), "Foo.Tests", @"..\..\src\Foo\Foo.csproj");
            string unitTests = WriteProject(
                SubDir(root, "tests", "Foo.UnitTests"), "Foo.UnitTests", @"..\..\src\Foo\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            string expected = new[] { tests, unitTests }
                .OrderBy(p => p, StringComparer.Ordinal)
                .First();
            result.Should().Be(expected);
        });
    }

    [Fact]
    public void HandlesBackslashAndDotDotIncludePaths()
    {
        WithTempRoot(root =>
        {
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            string test = WriteProject(
                SubDir(root, "tests", "unit", "Foo.Tests"),
                "Foo.Tests",
                @"..\..\..\src\Foo\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().Be(test);
        });
    }

    [Fact]
    public void IgnoresMissingOrUnparseableReferencedProject()
    {
        // The test project's only ProjectReference is dangling (points at a non-existent .csproj). Resolution
        // must not throw; the dangling ref contributes zero references, so Foo is unreachable -> null.
        WithTempRoot(root =>
        {
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            WriteProject(
                SubDir(root, "tests", "Foo.Tests"),
                "Foo.Tests",
                @"..\..\src\Missing\Missing.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().BeNull();
        });
    }

    [Fact]
    public void TreatsMalformedExistingCandidateAsZeroReferencesAndResolvesValidSibling()
    {
        // A stray EXISTING-but-malformed name-matching candidate (Foo.UnitTests.csproj with truncated
        // XML) must NOT abort resolution: XDocument.Load would throw XmlException, but the catch treats
        // it as zero references, so it is filtered out and the VALID Foo.Tests.csproj still resolves.
        // Proves the malformed-EXISTING branch of the non-throwing marker-probe discipline (File.Exists
        // already guards the MISSING branch). Note: OrderBy forces ReferencesTransitively to run on the
        // malformed candidate regardless of ordinal order, so without the catch this would throw.
        WithTempRoot(root =>
        {
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            string valid = WriteProject(
                SubDir(root, "tests", "Foo.Tests"), "Foo.Tests", @"..\..\src\Foo\Foo.csproj");
            File.WriteAllText(
                Path.Combine(SubDir(root, "tests", "Foo.UnitTests"), "Foo.UnitTests.csproj"),
                "<Project><ItemGroup>");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().Be(valid);
        });
    }

    [Fact]
    public void ThrowsOnNullOwningProject()
    {
        Action act = () => TestProjectResolver.ResolveTestProject(null!, "root");

        act.Should().Throw<ArgumentNullException>().WithParameterName("owningProject");
    }

    [Fact]
    public void ThrowsOnNullInvocationRoot()
    {
        Action act = () => TestProjectResolver.ResolveTestProject("Foo.csproj", null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("invocationRoot");
    }

    private static void WithTempRoot(Action<string> test)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            test(root);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string SubDir(string parent, params string[] segments)
    {
        string path = Path.Combine(parent, Path.Combine(segments));
        Directory.CreateDirectory(path);
        return path;
    }

    // Emits a minimal SDK-style .csproj named <name>.csproj in dir, with a <ProjectReference> per relative
    // Include path. Returns the absolute .csproj path.
    private static string WriteProject(string dir, string name, params string[] referenceRelPaths)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name + ".csproj");
        string references = string.Concat(
            referenceRelPaths.Select(rel => $"<ProjectReference Include=\"{rel}\" />"));
        File.WriteAllText(
            path, $"<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup>{references}</ItemGroup></Project>");
        return path;
    }
}

namespace Microsoft.Crap4CSharp.Tests;

// Reshape of the retired ModuleRootResolverTests for Model B (departure #9). All ".sln"-precedence tests are
// dropped (".sln" is no longer a marker); the resolver now returns the nearest OWNING PROJECT (.csproj FILE)
// bounded by the invocation root. The KEY new test is BoundsWalkAtInvocationRoot -- the walk must never climb
// above the invocation root (the S6 ancestor-climb fix). Every test scaffolds a fresh temp tree and removes it
// in a finally; because the walk is BOUNDED, no marker-free-ancestry assumption is needed. PascalCase, no [Trait].
public class OwningProjectResolverTests
{
    [Fact]
    public void ResolvesNearestOwningProjectForFileInProjectDirectory()
    {
        WithTempRoot(root =>
        {
            string project = Touch(root, "Foo.csproj");
            string file = Touch(root, "Foo.cs");

            string? result = OwningProjectResolver.ResolveOwningProject(file, root);

            result.Should().Be(project);
        });
    }

    [Fact]
    public void WalksUpToNearestProjectFromNestedFile()
    {
        WithTempRoot(root =>
        {
            string project = Touch(root, "Foo.csproj");
            string file = Touch(SubDir(root, "a", "b"), "Bar.cs");

            string? result = OwningProjectResolver.ResolveOwningProject(file, root);

            result.Should().Be(project);
        });
    }

    [Fact]
    public void PrefersNearestProjectAmongAncestors()
    {
        WithTempRoot(root =>
        {
            Touch(root, "Outer.csproj");
            string mid = SubDir(root, "mid");
            string inner = Touch(mid, "Inner.csproj");
            string file = Touch(SubDir(mid, "leaf"), "X.cs");

            string? result = OwningProjectResolver.ResolveOwningProject(file, root);

            result.Should().Be(inner);
        });
    }

    [Fact]
    public void BoundsWalkAtInvocationRoot()
    {
        // THE S6 FIX: a ".csproj" sits ABOVE the invocation root; the file under the root has none between.
        // The bounded walk must NOT climb out of the invocation root to that ancestor project -> null.
        WithTempRoot(outer =>
        {
            Touch(outer, "App.csproj");
            string invocationRoot = SubDir(outer, "inner");
            string file = Touch(SubDir(invocationRoot, "sub"), "File.cs");

            string? result = OwningProjectResolver.ResolveOwningProject(file, invocationRoot);

            result.Should().BeNull();
        });
    }

    [Fact]
    public void IsInclusiveOfInvocationRoot()
    {
        WithTempRoot(root =>
        {
            string project = Touch(root, "App.csproj");
            string file = Touch(SubDir(root, "sub"), "File.cs");

            string? result = OwningProjectResolver.ResolveOwningProject(file, root);

            result.Should().Be(project);
        });
    }

    [Fact]
    public void ReturnsNullWhenNoProjectWithinBounds()
    {
        WithTempRoot(root =>
        {
            string file = Touch(SubDir(root, "a", "b"), "File.cs");

            string? result = OwningProjectResolver.ResolveOwningProject(file, root);

            result.Should().BeNull();
        });
    }

    [Fact]
    public void ReturnsNullWhenStartOutsideInvocationRoot()
    {
        // The file (with its own project right beside it) sits OUTSIDE the invocation-root subtree, so
        // IsWithin fails immediately and it contributes no owner (FLAG-2 behavior).
        WithTempRoot(root =>
        {
            string invocationRoot = SubDir(root, "inside");
            string outsideDir = SubDir(root, "outside");
            Touch(outsideDir, "Outside.csproj");
            string file = Touch(outsideDir, "File.cs");

            string? result = OwningProjectResolver.ResolveOwningProject(file, invocationRoot);

            result.Should().BeNull();
        });
    }

    [Fact]
    public void AntiGlobIgnoresNonCsprojByOrdinalSuffix()
    {
        // ".csproj.bak" / ".csprojx" near the file must NOT match; the walk climbs past them to the real
        // ".csproj" higher up -- EndsWith(".csproj", Ordinal) discipline.
        WithTempRoot(root =>
        {
            string real = Touch(root, "Real.csproj");
            string near = SubDir(root, "near");
            Touch(near, "Foo.csproj.bak");
            Touch(near, "Foo.csprojx");
            string file = Touch(near, "X.cs");

            string? result = OwningProjectResolver.ResolveOwningProject(file, root);

            result.Should().Be(real);
        });
    }

    [Fact]
    public void NormalizesFileStartToParentDirectory()
    {
        // Start is a ".cs" FILE; resolution must begin from its parent directory.
        WithTempRoot(root =>
        {
            string project = Touch(root, "App.csproj");
            string file = Touch(root, "Program.cs");

            string? result = OwningProjectResolver.ResolveOwningProject(file, root);

            result.Should().Be(project);
        });
    }

    [Fact]
    public void ResolvesRelativeStartPath()
    {
        // Scaffold under the current directory so a genuine relative path exists; the start path must be
        // GetFullPath-normalized back to the absolute owning-project path.
        string root = Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString("N"));
        try
        {
            string project = Touch(root, "App.csproj");
            Touch(root, "Program.cs");
            string relative = Path.GetRelativePath(
                Directory.GetCurrentDirectory(), Path.Combine(root, "Program.cs"));

            string? result = OwningProjectResolver.ResolveOwningProject(relative, root);

            result.Should().Be(project);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ResolveOwningProjectsReturnsSingleForSingleProject()
    {
        WithTempRoot(root =>
        {
            string project = Touch(root, "Foo.csproj");
            string a = Touch(root, "a.cs");
            string c = Touch(SubDir(root, "b"), "c.cs");

            OwningProjectResolution result = OwningProjectResolver.ResolveOwningProjects([a, c], root);

            result.OwningProjects.Should().ContainSingle().Which.Should().Be(project);
            result.UnownedFiles.Should().BeEmpty();
        });
    }

    [Fact]
    public void ResolveOwningProjectsReturnsDistinctSortedForSpan()
    {
        WithTempRoot(root =>
        {
            string projA = Touch(SubDir(root, "projA"), "A.csproj");
            string projB = Touch(SubDir(root, "projB"), "B.csproj");
            string x = Touch(SubDir(root, "projA"), "x.cs");
            string y = Touch(SubDir(root, "projB"), "y.cs");

            OwningProjectResolution result = OwningProjectResolver.ResolveOwningProjects([y, x], root);

            result.OwningProjects.Should().Equal(
                new[] { projA, projB }.OrderBy(p => p, StringComparer.Ordinal));
            result.UnownedFiles.Should().BeEmpty();
        });
    }

    [Fact]
    public void ResolveOwningProjectsSurfacesFilesWithoutOwner()
    {
        WithTempRoot(root =>
        {
            string project = Touch(SubDir(root, "proj"), "P.csproj");
            string owned = Touch(SubDir(root, "proj"), "owned.cs");
            string orphan = Touch(SubDir(root, "noproj"), "orphan.cs");

            OwningProjectResolution result = OwningProjectResolver.ResolveOwningProjects([owned, orphan], root);

            result.OwningProjects.Should().ContainSingle().Which.Should().Be(project);
            result.UnownedFiles.Should().ContainSingle().Which.Should().Be(orphan);
        });
    }

    [Fact]
    public void ThrowsOnNullFiles()
    {
        Action act = () => OwningProjectResolver.ResolveOwningProjects(null!, "root");

        act.Should().Throw<ArgumentNullException>().WithParameterName("files");
    }

    [Fact]
    public void ThrowsOnNullInvocationRoot()
    {
        Action act = () => OwningProjectResolver.ResolveOwningProjects([], null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("invocationRoot");
    }

    [Fact]
    public void ThrowsOnNullStartPath()
    {
        Action act = () => OwningProjectResolver.ResolveOwningProject(null!, "root");

        act.Should().Throw<ArgumentNullException>().WithParameterName("startPath");
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

    private static string Touch(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        File.WriteAllText(path, string.Empty);
        return path;
    }
}

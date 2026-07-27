namespace Microsoft.Crap4CSharp.Tests;

// Pins ModuleRootResolver against crap4java's CliApplication.moduleRootFor: the upward marker walk, the
// ".sln"-beats-".csproj" precedence, file-argument normalization, and the ratified B1 unbounded fallback
// to the starting directory. Every test scaffolds a fresh temp tree (empty ".sln"/".csproj"/dirs) and
// removes it in a finally. Tests that rely on NO marker being found up-chain (Nos. 4-6) assume the temp
// ancestry is marker-free -- ratified to hold on CI /tmp and dev temp.
public class ModuleRootResolverTests
{
    [Fact]
    public void ResolvesDirectoryContainingSolution()
    {
        WithTempRoot(root =>
        {
            Touch(root, "App.sln");

            string result = ModuleRootResolver.Resolve(root);

            result.Should().Be(Path.GetFullPath(root));
        });
    }

    [Fact]
    public void PrefersSolutionOverNearerProjectFile()
    {
        // THE LINCHPIN: the start dir carries a ".csproj" while an ancestor carries a ".sln". The ".sln"
        // must win even though the ".csproj" is nearer -- exactly Java's single-marker precedence.
        WithTempRoot(root =>
        {
            string projectDir = SubDir(root, "src", "App");
            Touch(root, "Solution.sln");
            Touch(projectDir, "App.csproj");

            string result = ModuleRootResolver.Resolve(projectDir);

            result.Should().Be(Path.GetFullPath(root));
        });
    }

    [Fact]
    public void WalksUpToFindSolution()
    {
        WithTempRoot(root =>
        {
            Touch(root, "App.sln");
            string start = SubDir(root, "a", "b");

            string result = ModuleRootResolver.Resolve(start);

            result.Should().Be(Path.GetFullPath(root));
        });
    }

    [Fact]
    public void FallsBackToNearestProjectWhenNoSolution()
    {
        WithTempRoot(root =>
        {
            string projectDir = SubDir(root, "lib");
            Touch(projectDir, "Lib.csproj");
            string start = SubDir(projectDir, "nested");

            string result = ModuleRootResolver.Resolve(start);

            result.Should().Be(Path.GetFullPath(projectDir));
        });
    }

    [Fact]
    public void PrefersNearestProjectAmongMultiple()
    {
        WithTempRoot(root =>
        {
            Touch(root, "Outer.csproj");
            string innerDir = SubDir(root, "mid");
            Touch(innerDir, "Inner.csproj");
            string start = SubDir(innerDir, "leaf");

            string result = ModuleRootResolver.Resolve(start);

            result.Should().Be(Path.GetFullPath(innerDir));
        });
    }

    [Fact]
    public void ReturnsStartDirectoryWhenNoMarker()
    {
        // B1 fallback: an isolated temp tree with no ".sln"/".csproj" anywhere resolves to the start dir.
        WithTempRoot(root =>
        {
            string start = SubDir(root, "x", "y");

            string result = ModuleRootResolver.Resolve(start);

            result.Should().Be(Path.GetFullPath(start));
        });
    }

    [Fact]
    public void NormalizesFileArgumentToParentDirectory()
    {
        WithTempRoot(root =>
        {
            Touch(root, "App.sln");
            string file = Touch(root, "Program.cs");

            string result = ModuleRootResolver.Resolve(file);

            result.Should().Be(Path.GetFullPath(root));
        });
    }

    [Fact]
    public void ResolvesRelativeStartDirectory()
    {
        // Scaffold under the current directory so a genuine relative path exists on every platform/drive;
        // Resolve must GetFullPath-normalize it back to the absolute solution directory.
        string root = Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString("N"));
        try
        {
            Touch(root, "App.sln");
            string relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), root);

            string result = ModuleRootResolver.Resolve(relative);

            result.Should().Be(Path.GetFullPath(root));
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
    public void DoesNotMatchSlnxOrSlnfAsSolution()
    {
        // Only ".slnx"/".slnf" sit in the start dir (a real ".sln" is higher up). If EndsWith(".sln")
        // discipline slipped, Resolve would stop at the start dir; instead it must climb to the real ".sln".
        WithTempRoot(root =>
        {
            Touch(root, "App.sln");
            string start = SubDir(root, "module");
            Touch(start, "foo.slnx");
            Touch(start, "foo.slnf");

            string result = ModuleRootResolver.Resolve(start);

            result.Should().Be(Path.GetFullPath(root));
        });
    }

    [Fact]
    public void SolutionInStartDirectoryBeatsProjectInStartDirectory()
    {
        // Both markers live in the start dir; the ".sln" path returns it without falling through to any
        // ".csproj"-only handling -- precedence sanity at the same directory level.
        WithTempRoot(root =>
        {
            Touch(root, "App.sln");
            Touch(root, "App.csproj");

            string result = ModuleRootResolver.Resolve(root);

            result.Should().Be(Path.GetFullPath(root));
        });
    }

    [Fact]
    public void ThrowsOnNullStartDirectory()
    {
        Action act = () => ModuleRootResolver.Resolve(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("startDirectory");
    }

    [Fact]
    public void ReturnsSolutionDirectoryDespiteNearerProjectsAtDepth()
    {
        // Nested-depth linchpin: a ".sln" three levels up with ".csproj" files scattered below still wins.
        WithTempRoot(root =>
        {
            Touch(root, "Top.sln");
            string levelA = SubDir(root, "a");
            Touch(levelA, "A.csproj");
            string levelB = SubDir(levelA, "b");
            Touch(levelB, "B.csproj");
            string start = SubDir(levelB, "c");

            string result = ModuleRootResolver.Resolve(start);

            result.Should().Be(Path.GetFullPath(root));
        });
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

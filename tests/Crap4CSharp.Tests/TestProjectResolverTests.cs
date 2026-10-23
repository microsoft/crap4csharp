namespace Microsoft.Crap4CSharp.Tests;

using System.Linq;

// Covers TestProjectResolver (Model B, departure #9): the <Project>.Tests / <Project>.UnitTests naming gate,
// transitive ProjectReference reachability (cycle-safe), bin/obj + bounded-scope exclusions, deterministic
// ambiguity rejection, explicit selection, backslash/`..` Include normalization, and (D-T29a)
// the two-stage MSBuild-property evaluation that expands `$(...)` tokens in ProjectReference Includes before
// matching. Each test scaffolds minimal on-disk SDK-style .csproj (and Directory.Build.props) fixtures in a
// fresh temp tree, removed in a finally. PascalCase, no [Trait].
public class TestProjectResolverTests
{
    [Theory]
    [InlineData("tests/*.csproj", 1)]
    [InlineData("tests/?.csproj", 1)]
    [InlineData("tests/**/*.csproj", 3)]
    [InlineData(@"tests\**\?.csproj", 2)]
    [InlineData("tests/*/*.csproj", 1)]
    [InlineData("tests/**/B.csproj", 1)]
    [InlineData("tests/**/space suite/*.csproj", 1)]
    public void ExpandsSupportedGlobsWithZeroOrDeepDirectories(string selector, int expected)
    {
        WithTempRoot(root =>
        {
            string owner = WriteProject(SubDir(root, "src"), "Foo");
            string a = WriteProject(SubDir(root, "tests"), "A", owner);
            WriteProject(SubDir(root, "tests", "space suite"), "B", owner);
            WriteProject(SubDir(root, "tests", "deep", "suite"), "Long", owner);
            WriteProject(SubDir(root, "tests", "obj"), "Ignored", owner);
            WriteProject(SubDir(root, "tests", "bin", "deep"), "Ignored", owner);
            var selected = TestProjectResolver.ResolveTestProjects(owner, root, [selector]);
            selected.Should().HaveCount(expected).And.BeInAscendingOrder(StringComparer.Ordinal);
            TestProjectResolver.ResolveTestProjects(owner, root, [selector, a, a])
                .Should().HaveCount(selected.Contains(a, StringComparer.Ordinal) ? expected : expected + 1);
        });
    }

    [Theory]
    [InlineData("tests/No*.csproj")]
    [InlineData("Tests/*.csproj")]
    [InlineData("tests/*.CSPROJ")]
    [InlineData("tests/[AB].csproj")]
    [InlineData("tests/{A,B}.csproj")]
    [InlineData("!tests/*.csproj")]
    [InlineData("tests/A**.csproj")]
    [InlineData("../**/*.csproj")]
    [InlineData("tests/../../*.csproj")]
    [InlineData("tests/obj/*.csproj")]
    [InlineData("tests/bin/Bad.csproj")]
    public void InvalidSelectorCannotBeMaskedByValidSelector(string selector)
    {
        WithTempRoot(root =>
        {
            string owner = WriteProject(SubDir(root, "src"), "Foo");
            string valid = WriteProject(SubDir(root, "tests"), "A", owner);
            WriteProject(SubDir(root, "tests", "obj"), "Bad", owner);
            WriteProject(SubDir(root, "tests", "bin"), "Bad", owner);
            FluentActions.Invoking(() => TestProjectResolver.ResolveTestProjects(owner, root, [valid, selector]))
                .Should().Throw<ArgumentException>();
            FluentActions.Invoking(() => TestProjectResolver.ResolveTestProjects(owner, root, [valid, root + "/**/*.csproj"]))
                .Should().Throw<ArgumentException>();
        });
    }

    [Fact]
    public void EveryGlobMatchMustReferenceOwnerRegardlessOfName()
    {
        WithTempRoot(root =>
        {
            string owner = WriteProject(SubDir(root, "src"), "Foo");
            string valid = WriteProject(SubDir(root, "tests"), "Anything", owner);
            WriteProject(SubDir(root, "tests"), "Unrelated");
            TestProjectResolver.ResolveTestProjects(owner, root, [valid]).Should().Equal(valid);
            FluentActions.Invoking(() => TestProjectResolver.ResolveTestProjects(owner, root, ["tests/*.csproj"]))
                .Should().Throw<ArgumentException>().WithMessage("*Unrelated*");
        });
    }

    [Fact]
    public void ExplicitMultiSelectionKeepsBoundedPropertyAndCycleGraph()
    {
        WithTempRoot(root =>
        {
            string owner = WriteProject(SubDir(root, "src"), "Foo");
            string bridge = WriteProject(SubDir(root, "bridge"), "Bridge", owner, "../tests/A.csproj");
            string a = WriteProject(SubDir(root, "tests"), "A", bridge);
            string b = WriteProject(SubDir(root, "tests"), "B", "$(RepoRoot)src/Foo.csproj");
            File.WriteAllText(
                Path.Combine(root, "Directory.Build.props"),
                "<Project><PropertyGroup><RepoRoot>$(MSBuildThisFileDirectory)</RepoRoot></PropertyGroup></Project>");
            TestProjectResolver.ResolveTestProjects(owner, root, ["tests/*.csproj"]).Should().Equal(a, b);
        });
    }

    [Fact]
    public void DoesNotTraverseOutOfRootGraphOrSymlinkEscapes()
    {
        WithTempRoot(outer =>
        {
            string root = SubDir(outer, "root");
            string owner = WriteProject(SubDir(root, "src"), "Foo");
            string outside = WriteProject(SubDir(outer, "outside"), "Bridge", owner);
            string test = WriteProject(SubDir(root, "tests"), "A", outside);
            Directory.CreateSymbolicLink(Path.Combine(root, "escape"), Path.GetDirectoryName(outside)!);
            File.CreateSymbolicLink(Path.Combine(root, "tests", "Link.csproj"), outside);
            FluentActions.Invoking(() => TestProjectResolver.ResolveTestProjects(owner, root, [test]))
                .Should().Throw<ArgumentException>();
            FluentActions.Invoking(() => TestProjectResolver.ResolveTestProjects(owner, root, ["escape/*.csproj"]))
                .Should().Throw<ArgumentException>();
            FluentActions.Invoking(() => TestProjectResolver.ResolveTestProjects(owner, root, ["tests/Link.csproj"]))
                .Should().Throw<ArgumentException>();
            FluentActions.Invoking(() => TestProjectResolver.ResolveTestProjects(owner, root, [outside]))
                .Should().Throw<ArgumentException>();
        });
    }

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
    public void RejectsAmbiguityListingCandidatesInOrdinalOrder()
    {
        WithTempRoot(root =>
        {
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            string tests = WriteProject(
                SubDir(root, "tests", "Foo.Tests"), "Foo.Tests", @"..\..\src\Foo\Foo.csproj");
            string unitTests = WriteProject(
                SubDir(root, "tests", "Foo.UnitTests"), "Foo.UnitTests", @"..\..\src\Foo\Foo.csproj");

            FluentActions.Invoking(() => TestProjectResolver.ResolveTestProject(owning, root))
                .Should().Throw<ArgumentException>()
                .WithMessage($"*{tests}, {unitTests}*--test-project*");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitCustomNameBypassesNamingOnly(bool transitive)
    {
        WithTempRoot(root =>
        {
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            WriteProject(SubDir(root, "src", "Bridge"), "Bridge", "../Foo/Foo.csproj");
            string test = WriteProject(
                SubDir(root, "tests", "Custom"),
                "Custom",
                transitive ? "../../src/Bridge/Bridge.csproj" : "../../src/Foo/Foo.csproj");
            TestProjectResolver.ResolveTestProject(owning, root, Path.GetRelativePath(root, test))
                .Should().Be(test);
            TestProjectResolver.ResolveTestProject(owning, root).Should().BeNull();
        });
    }

    [Theory]
    [InlineData("tests/Missing.csproj")]
    [InlineData("tests/Unrelated.csproj")]
    [InlineData("../Outside.csproj")]
    [InlineData("bin/Custom.csproj")]
    [InlineData("obj/Custom.csproj")]
    [InlineData("tests/Custom.txt")]
    public void RejectsInvalidExplicitSelectionEvenWhenDiscoveryWouldSucceed(string selected)
    {
        WithTempRoot(root =>
        {
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            WriteProject(SubDir(root, "tests"), "Foo.Tests", "../src/Foo/Foo.csproj");
            WriteProject(SubDir(root, "tests"), "Unrelated");
            WriteProject(SubDir(root, "bin"), "Custom", "../src/Foo/Foo.csproj");
            WriteProject(SubDir(root, "obj"), "Custom", "../src/Foo/Foo.csproj");
            FluentActions.Invoking(() => TestProjectResolver.ResolveTestProject(owning, root, selected))
                .Should().Throw<ArgumentException>().WithMessage("*Invalid --test-project*");
        });
    }

    [Fact]
    public void RejectsExistingOutsideProjectDespiteGenuineReference()
    {
        WithTempRoot(parent =>
        {
            string root = SubDir(parent, "repo");
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            string outside = WriteProject(SubDir(parent, "outside"), "Custom", "../repo/src/Foo/Foo.csproj");
            FluentActions.Invoking(() => TestProjectResolver.ResolveTestProject(owning, root, outside))
                .Should().Throw<ArgumentException>().WithMessage("*Invalid --test-project*");
        });
    }

    [Fact]
    public void ExplicitSelectionUsesSameBoundedPropertyExpansionAndNormalizesPaths()
    {
        WithTempRoot(root =>
        {
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            File.WriteAllText(
                Path.Combine(root, "Directory.Build.props"),
                "<Project><PropertyGroup><RepoRoot>$(MSBuildThisFileDirectory)</RepoRoot></PropertyGroup></Project>");
            string test = WriteProject(SubDir(root, "tests"), "Custom", "$(RepoRoot)src/Foo/Foo.csproj");
            TestProjectResolver.ResolveTestProject(owning, root + Path.DirectorySeparatorChar, "tests/../tests/Custom.csproj")
                .Should().Be(test);
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

    // ---- D-T29a: property-based ProjectReference resolution (folds under departure #9) ----
    [Fact]
    public void ResolvesReferenceThroughRepoRootFromRootDirectoryBuildProps()
    {
        // Finding #7: a root Directory.Build.props defines RepoRoot=$(MSBuildThisFileDirectory) (the repo
        // root), and the test project references the owner via $(RepoRoot). Pre-T29 the token survived
        // verbatim and dropped the edge -> a false "No test project"; now it resolves.
        WithTempRoot(root =>
        {
            WriteDirectoryBuildProps(
                root, "<PropertyGroup><RepoRoot>$(MSBuildThisFileDirectory)</RepoRoot></PropertyGroup>");
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            string test = WriteProjectWithProperties(
                SubDir(root, "tests", "Foo.Tests"),
                "Foo.Tests",
                string.Empty,
                @"$(RepoRoot)src\Foo\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().Be(test);
        });
    }

    [Fact]
    public void ResolvesReferenceThroughMSBuildThisFileDirectoryTrailingSeparator()
    {
        // MSBuildThisFileDirectory carries a TRAILING separator, so a separator-less Include concatenates
        // cleanly: $(MSBuildThisFileDirectory)..\..\src\Foo\Foo.csproj. Dropping that separator would form a
        // "Foo.Tests.." segment and break resolution -- this pins the load-bearing trailing separator.
        WithTempRoot(root =>
        {
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            string test = WriteProjectWithProperties(
                SubDir(root, "tests", "Foo.Tests"),
                "Foo.Tests",
                string.Empty,
                @"$(MSBuildThisFileDirectory)..\..\src\Foo\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().Be(test);
        });
    }

    [Fact]
    public void ResolvesReferenceThroughMSBuildProjectDirectoryWithExplicitSeparator()
    {
        // MSBuildProjectDirectory expands to the csproj's own directory; the user supplies the separator.
        WithTempRoot(root =>
        {
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            string test = WriteProjectWithProperties(
                SubDir(root, "tests", "Foo.Tests"),
                "Foo.Tests",
                string.Empty,
                @"$(MSBuildProjectDirectory)\..\..\src\Foo\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().Be(test);
        });
    }

    [Fact]
    public void MSBuildProjectDirectoryOmitsTrailingSeparatorSoSeparatorlessIncludeDrops()
    {
        // Contrast with MSBuildThisFileDirectory: MSBuildProjectDirectory has NO trailing separator, so the
        // same separator-less Include yields the segment "Foo.Tests.." (not the project dir + sep) and Foo is
        // unreachable -> null. A wrongly-added trailing separator would make this resolve, so it pins no-sep.
        WithTempRoot(root =>
        {
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            WriteProjectWithProperties(
                SubDir(root, "tests", "Foo.Tests"),
                "Foo.Tests",
                string.Empty,
                @"$(MSBuildProjectDirectory)..\..\src\Foo\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().BeNull();
        });
    }

    [Fact]
    public void MatchesPropertyNameCaseInsensitively()
    {
        // Property NAMES are MSBuild-case-insensitive: $(reporoot) resolves the RepoRoot definition (values,
        // however, stay Ordinal per departure #3).
        WithTempRoot(root =>
        {
            WriteDirectoryBuildProps(
                root, "<PropertyGroup><RepoRoot>$(MSBuildThisFileDirectory)</RepoRoot></PropertyGroup>");
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            string test = WriteProjectWithProperties(
                SubDir(root, "tests", "Foo.Tests"),
                "Foo.Tests",
                string.Empty,
                @"$(reporoot)src\Foo\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().Be(test);
        });
    }

    [Fact]
    public void ExpandsMultipleTokensInOneInclude()
    {
        // A single regex pass replaces every $(...) token in the Include.
        WithTempRoot(root =>
        {
            string props =
                "<PropertyGroup><SrcRoot>$(MSBuildThisFileDirectory)src</SrcRoot><Proj>Foo</Proj></PropertyGroup>";
            WriteDirectoryBuildProps(root, props);
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            string test = WriteProjectWithProperties(
                SubDir(root, "tests", "Foo.Tests"),
                "Foo.Tests",
                string.Empty,
                @"$(SrcRoot)\$(Proj)\$(Proj).csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().Be(test);
        });
    }

    [Fact]
    public void CsprojBodyPropertyOverridesDirectoryBuildProps()
    {
        // Same property name in the root props (pointing nowhere) and the csproj body (pointing at the owner).
        // Last-writer-wins with the csproj body evaluated last -> the body value is used -> resolves. If the
        // props file won, the Include would point at a nonexistent dir -> null.
        WithTempRoot(root =>
        {
            string rootProps =
                "<PropertyGroup><OwnerDir>$(MSBuildThisFileDirectory)nonexistent</OwnerDir></PropertyGroup>";
            WriteDirectoryBuildProps(root, rootProps);
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            string bodyProps =
                @"<PropertyGroup><OwnerDir>$(MSBuildThisFileDirectory)..\..\src\Foo</OwnerDir></PropertyGroup>";
            string test = WriteProjectWithProperties(
                SubDir(root, "tests", "Foo.Tests"),
                "Foo.Tests",
                bodyProps,
                @"$(OwnerDir)\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().Be(test);
        });
    }

    [Fact]
    public void NearerDirectoryBuildPropsOverridesFartherForSameProperty()
    {
        // Two Directory.Build.props define OwnerDir: the farther (repo root) points nowhere; the nearer (the
        // intermediate tests/ dir, one level above the csproj) points at the owner. The chain is ordered
        // outermost -> nearest with last-writer-wins, so the NEARER value drives the Include -> resolves. If
        // the farther file won, the Include would point at a nonexistent dir -> null.
        WithTempRoot(root =>
        {
            WriteDirectoryBuildProps(
                root,
                "<PropertyGroup><OwnerDir>$(MSBuildThisFileDirectory)nonexistent</OwnerDir></PropertyGroup>");
            WriteDirectoryBuildProps(
                SubDir(root, "tests"),
                @"<PropertyGroup><OwnerDir>$(MSBuildThisFileDirectory)..\src\Foo</OwnerDir></PropertyGroup>");
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            string test = WriteProjectWithProperties(
                SubDir(root, "tests", "Foo.Tests"),
                "Foo.Tests",
                string.Empty,
                @"$(OwnerDir)\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().Be(test);
        });
    }

    [Fact]
    public void SkipsPropertyWithNonSelfEmptinessGuardCondition()
    {
        // A Condition that is NOT the self-emptiness guard (here a Configuration check) -> the property is
        // skipped -> $(RepoRoot) stays verbatim -> the edge drops -> null.
        WithTempRoot(root =>
        {
            string props =
                "<PropertyGroup><RepoRoot Condition=\"'$(Configuration)'=='Debug'\">"
                + "$(MSBuildThisFileDirectory)</RepoRoot></PropertyGroup>";
            WriteDirectoryBuildProps(root, props);
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            WriteProjectWithProperties(
                SubDir(root, "tests", "Foo.Tests"),
                "Foo.Tests",
                string.Empty,
                @"$(RepoRoot)src\Foo\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().BeNull();
        });
    }

    [Fact]
    public void HonorsSelfEmptinessGuardCondition()
    {
        // The ubiquitous RepoRoot idiom: Condition="'$(RepoRoot)'==''" is honored (RepoRoot is not yet set)
        // -> the property applies -> resolves.
        WithTempRoot(root =>
        {
            string props =
                "<PropertyGroup><RepoRoot Condition=\"'$(RepoRoot)'==''\">"
                + "$(MSBuildThisFileDirectory)</RepoRoot></PropertyGroup>";
            WriteDirectoryBuildProps(root, props);
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            string test = WriteProjectWithProperties(
                SubDir(root, "tests", "Foo.Tests"),
                "Foo.Tests",
                string.Empty,
                @"$(RepoRoot)src\Foo\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().Be(test);
        });
    }

    [Fact]
    public void SkipsSelfEmptinessGuardNamingDifferentProperty()
    {
        // Locks the NAME-MATCH half of the self-guard rule. Condition="'$(SomethingElse)'==''" is a
        // well-formed emptiness guard, but names a DIFFERENT property than RepoRoot (and SomethingElse is
        // undefined, so a name-blind check would wrongly honor it). Because the guarded name != the defined
        // name, the def is SKIPPED -> $(RepoRoot) stays verbatim -> the edge drops -> null. Inverts
        // HonorsSelfEmptinessGuardCondition; deleting the string.Equals(name) check would resolve here.
        WithTempRoot(root =>
        {
            string props =
                "<PropertyGroup><RepoRoot Condition=\"'$(SomethingElse)'==''\">"
                + "$(MSBuildThisFileDirectory)</RepoRoot></PropertyGroup>";
            WriteDirectoryBuildProps(root, props);
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            WriteProjectWithProperties(
                SubDir(root, "tests", "Foo.Tests"),
                "Foo.Tests",
                string.Empty,
                @"$(RepoRoot)src\Foo\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().BeNull();
        });
    }

    [Fact]
    public void SkipsReversedAndNegatedEmptinessGuardConditions()
    {
        // The self-guard regex matches ONLY '$(X)'=='' -- neither the reversed ''=='$(RepoRoot)' (a valid
        // MSBuild guard the resolver deliberately does NOT match, D-T29a) nor the negated '$(RepoRoot)'!=''
        // qualifies, so each RepoRoot def is SKIPPED -> $(RepoRoot) stays verbatim -> the edge drops -> null.
        foreach (string condition in new[] { "''=='$(RepoRoot)'", "'$(RepoRoot)'!=''" })
        {
            WithTempRoot(root =>
            {
                string props =
                    $"<PropertyGroup><RepoRoot Condition=\"{condition}\">"
                    + "$(MSBuildThisFileDirectory)</RepoRoot></PropertyGroup>";
                WriteDirectoryBuildProps(root, props);
                string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
                WriteProjectWithProperties(
                    SubDir(root, "tests", "Foo.Tests"),
                    "Foo.Tests",
                    string.Empty,
                    @"$(RepoRoot)src\Foo\Foo.csproj");

                string? result = TestProjectResolver.ResolveTestProject(owning, root);

                result.Should().BeNull($"condition {condition} is not a self-emptiness guard");
            });
        }
    }

    [Fact]
    public void ReturnsNullOnCyclicPropertyReferences()
    {
        // A=$(B), B=$(A): the fixpoint's in-progress set breaks the cycle (tokens left verbatim), so the
        // Include never resolves -> null. The test simply RETURNING (not hanging) proves termination.
        WithTempRoot(root =>
        {
            WriteDirectoryBuildProps(root, "<PropertyGroup><A>$(B)</A><B>$(A)</B></PropertyGroup>");
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            WriteProjectWithProperties(
                SubDir(root, "tests", "Foo.Tests"),
                "Foo.Tests",
                string.Empty,
                @"$(A)src\Foo\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().BeNull();
        });
    }

    [Fact]
    public void ReturnsNullOnSelfReferentialProperty()
    {
        // Loop=$(Loop): a token that recurses into its own in-progress name is left verbatim -> null.
        WithTempRoot(root =>
        {
            WriteDirectoryBuildProps(root, "<PropertyGroup><Loop>$(Loop)</Loop></PropertyGroup>");
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            WriteProjectWithProperties(
                SubDir(root, "tests", "Foo.Tests"),
                "Foo.Tests",
                string.Empty,
                @"$(Loop)src\Foo\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().BeNull();
        });
    }

    [Fact]
    public void ResolvesNestedPropertyChain()
    {
        // RepoRoot -> SrcDir -> FooDir, each referencing the previous; the fixpoint resolves the whole chain.
        WithTempRoot(root =>
        {
            string props =
                @"<PropertyGroup><RepoRoot>$(MSBuildThisFileDirectory)</RepoRoot>"
                + @"<SrcDir>$(RepoRoot)src</SrcDir><FooDir>$(SrcDir)\Foo</FooDir></PropertyGroup>";
            WriteDirectoryBuildProps(root, props);
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            string test = WriteProjectWithProperties(
                SubDir(root, "tests", "Foo.Tests"),
                "Foo.Tests",
                string.Empty,
                @"$(FooDir)\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().Be(test);
        });
    }

    [Fact]
    public void ReturnsNullWhenSolutionDirHasNoExplicitDefinition()
    {
        // $(SolutionDir) is explicit-def ONLY (no .sln synthesis, honoring departure #9's .sln retirement):
        // with no <SolutionDir> anywhere it stays verbatim -> the edge drops -> null.
        WithTempRoot(root =>
        {
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            WriteProjectWithProperties(
                SubDir(root, "tests", "Foo.Tests"),
                "Foo.Tests",
                string.Empty,
                @"$(SolutionDir)src\Foo\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().BeNull();
        });
    }

    [Fact]
    public void ResolvesReferenceThroughExplicitSolutionDir()
    {
        // An explicit <SolutionDir> definition IS honored (only .sln synthesis is withheld).
        WithTempRoot(root =>
        {
            WriteDirectoryBuildProps(
                root, "<PropertyGroup><SolutionDir>$(MSBuildThisFileDirectory)</SolutionDir></PropertyGroup>");
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            string test = WriteProjectWithProperties(
                SubDir(root, "tests", "Foo.Tests"),
                "Foo.Tests",
                string.Empty,
                @"$(SolutionDir)src\Foo\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().Be(test);
        });
    }

    [Fact]
    public void MalformedDirectoryBuildPropsContributesNothingAndValidAncestorResolves()
    {
        // A malformed Directory.Build.props in the chain (truncated XML) must NOT abort resolution: it
        // contributes zero properties (XmlException swallowed, like the csproj probe), and the valid
        // root-level props still defines RepoRoot -> resolves.
        WithTempRoot(root =>
        {
            WriteDirectoryBuildProps(
                root, "<PropertyGroup><RepoRoot>$(MSBuildThisFileDirectory)</RepoRoot></PropertyGroup>");
            File.WriteAllText(
                Path.Combine(SubDir(root, "tests"), "Directory.Build.props"), "<Project><PropertyGroup>");
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            string test = WriteProjectWithProperties(
                SubDir(root, "tests", "Foo.Tests"),
                "Foo.Tests",
                string.Empty,
                @"$(RepoRoot)src\Foo\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().Be(test);
        });
    }

    [Fact]
    public void PreservesBinObjExclusionAndCycleSafetyWithPropertyBearingReferences()
    {
        // With property-based Includes throughout: an obj/-nested, correctly-referencing candidate is still
        // excluded (departure #5) even though it would sort ordinal-first, and an A<->B property cycle in the
        // reference graph still terminates (visited set). The valid tests/ candidate resolves.
        WithTempRoot(root =>
        {
            WriteDirectoryBuildProps(
                root, "<PropertyGroup><RepoRoot>$(MSBuildThisFileDirectory)</RepoRoot></PropertyGroup>");
            string owning = WriteProject(SubDir(root, "src", "Foo"), "Foo");
            WriteProjectWithProperties(
                SubDir(root, "src", "A"), "A", string.Empty, @"$(RepoRoot)src\B\B.csproj");
            WriteProjectWithProperties(
                SubDir(root, "src", "B"), "B", string.Empty, @"$(RepoRoot)src\A\A.csproj");
            WriteProjectWithProperties(
                SubDir(root, "obj", "Foo.UnitTests"),
                "Foo.UnitTests",
                string.Empty,
                @"$(RepoRoot)src\Foo\Foo.csproj");
            string test = WriteProjectWithProperties(
                SubDir(root, "tests", "Foo.Tests"),
                "Foo.Tests",
                string.Empty,
                @"$(RepoRoot)src\A\A.csproj",
                @"$(RepoRoot)src\Foo\Foo.csproj");

            string? result = TestProjectResolver.ResolveTestProject(owning, root);

            result.Should().Be(test);
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

    // Emits an SDK-style .csproj named <name>.csproj in dir with the given raw <PropertyGroup> XML (which may
    // embed $(...) tokens and Conditions) followed by a <ProjectReference> per Include. The distinct name and
    // params signature never collide with (nor silently capture) the reference-only WriteProject overload
    // above, and params spreads individual string args so callers avoid constant array literals (CA1861).
    // Returns the absolute .csproj path.
    private static string WriteProjectWithProperties(
        string dir, string name, string propertyGroups, params string[] references)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name + ".csproj");
        string includes = string.Concat(
            references.Select(include => $"<ProjectReference Include=\"{include}\" />"));
        File.WriteAllText(
            path,
            $"<Project Sdk=\"Microsoft.NET.Sdk\">{propertyGroups}<ItemGroup>{includes}</ItemGroup></Project>");
        return path;
    }

    // Writes a Directory.Build.props carrying the given raw <PropertyGroup> XML at dir. Returns its path.
    private static string WriteDirectoryBuildProps(string dir, string propertyGroups)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "Directory.Build.props");
        File.WriteAllText(path, $"<Project>{propertyGroups}</Project>");
        return path;
    }
}

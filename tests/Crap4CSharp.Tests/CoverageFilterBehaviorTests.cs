namespace Microsoft.Crap4CSharp.Tests;

using System.Diagnostics;

// Real CLI/VSTest/Coverlet critical path: execute the CLI against an isolated fixture,
// observe test-subset markers, and parse its one report. net8.0 is selected from two TFMs. Default filtering
// includes untagged tests, custom filtering REPLACES the default, all-tests omits it, and malformed native
// filters produce native diagnostics without running tests. Pinned packages reuse the warm cache.
public class CoverageFilterBehaviorTests
{
    // A cold restore(warm-cache)+build+run of a 2-method fixture is far heavier than ProgramTests' `dotnet <dll>`
    // spawns; allow a generous ceiling.
    private const int SpawnTimeoutMs = 120_000;

    private const string FixtureProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFrameworks>net8.0;net8.0-windows</TargetFrameworks>
            <Nullable>disable</Nullable>
            <ImplicitUsings>disable</ImplicitUsings>
            <IsPackable>false</IsPackable>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
            <PackageReference Include="xunit" Version="2.5.3" />
            <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
            <PackageReference Include="coverlet.collector" Version="6.0.0" />
            <ProjectReference Include="src/Subject.csproj" />
            <Compile Remove="src/**/*.cs" />
          </ItemGroup>
        </Project>
        """;

    // Clear inherited sources (isolating the fixture from any stray machine-global feed, mirroring the repo
    // nuget.config) and restore from nuget.org only -- satisfied entirely from the warm global-packages cache.
    private const string FixtureNuGetConfig = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
          </packageSources>
        </configuration>
        """;

    // Two [Fact]s: one UNTAGGED, one [Trait("type","IntegrationTests")]. Each writes a marker into the directory
    // named by CRAP4CSHARP_FILTER_MARKER_DIR (inherited from the spawner), so the parent can observe which ran.
    private const string FilterProbeSource = """
        using System;
        using System.IO;
        using Xunit;

        public class FilterProbe
        {
            [Fact]
            public void UntaggedTestRuns()
            {
                WriteMarker("untagged.marker");
            }

            [Fact]
            [Trait("type", "IntegrationTests")]
            public void IntegrationTestExcluded()
            {
                WriteMarker("integration.marker");
            }

            private static void WriteMarker(string name)
            {
                string dir = Environment.GetEnvironmentVariable("CRAP4CSHARP_FILTER_MARKER_DIR");
                if (dir != null)
                {
                    File.WriteAllText(Path.Combine(dir, name), "ran");
                }
                Assert.Equal(4, Subject.Twice(2));
            }
        }
        """;

    [Theory]
    [InlineData("default", true, false)]
    [InlineData("custom", false, true)]
    [InlineData("all", true, true)]
    [InlineData("invalid", false, false)]
    [InlineData("unmatched", false, false)]
    public async Task NativeFiltersAndSingleFrameworkProduceExpectedSubsetAndOneReport(
        string mode, bool untaggedRuns, bool integrationRuns)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string projectPath = Path.Combine(tempDir, "Fixture.Tests.csproj");
            await File.WriteAllTextAsync(projectPath, FixtureProject);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "nuget.config"), FixtureNuGetConfig);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "FilterProbe.cs"), FilterProbeSource);
            string sourceDir = Path.Combine(tempDir, "src");
            Directory.CreateDirectory(sourceDir);
            await File.WriteAllTextAsync(
                Path.Combine(sourceDir, "Subject.csproj"),
                """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>""");
            await File.WriteAllTextAsync(
                Path.Combine(sourceDir, "Subject.cs"),
                "public static class Subject { public static int Twice(int value) => value > 0 ? value * 2 : 0; }");
            string markerDir = Path.Combine(tempDir, "markers");
            Directory.CreateDirectory(markerDir);

            CliArguments options = new(CliMode.AllSrc, [])
            {
                TestFilter = mode switch
                {
                    "custom" => "type=IntegrationTests",
                    "invalid" => "(",
                    "unmatched" => "FullyQualifiedName~NoSuchTest",
                    _ => null,
                },
                AllTests = mode == "all",
                Framework = "net8.0",
            };
            SpawnResult result = await RunCliAsync(tempDir, projectPath, markerDir, options);

            string diagnostics =
                $"exit={result.ExitCode}{Environment.NewLine}--- stdout ---{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}--- stderr ---{Environment.NewLine}{result.StandardError}";

            File.Exists(Path.Combine(markerDir, "untagged.marker"))
                .Should().Be(untaggedRuns, diagnostics);
            File.Exists(Path.Combine(markerDir, "integration.marker"))
                .Should().Be(integrationRuns, diagnostics);
            if (mode == "invalid")
            {
                result.ExitCode.Should().Be(1, diagnostics);
                (result.StandardOutput + result.StandardError).Should().Contain("Exception filtering tests", diagnostics);
                result.StandardError.Should().Contain(nameof(CoverageException), diagnostics);
                result.StandardOutput.Should().NotContain("CRAP Report", diagnostics);
                return;
            }

            result.ExitCode.Should().Be(
                0, "valid filters, including zero matches, are not native errors. " + diagnostics);
            result.StandardOutput.Should().Contain("CRAP Report", diagnostics);
            var reports = CoverageReportLocator.LocateAll(tempDir);
            reports.Should().ContainSingle("one requested TFM produces one report. " + diagnostics);
            CoberturaCoverageParser.Parse(reports[0]).Should().NotBeEmpty(diagnostics);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TwoSelectedSuitesComplementLinesAndPassOnlyTheCombinedThreshold()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "nuget.config"), FixtureNuGetConfig);
            Directory.CreateDirectory(Path.Combine(root, "src"));
            await File.WriteAllTextAsync(
                Path.Combine(root, "src/Subject.csproj"),
                """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>""");
            const string source = """
                public static class Subject
                {
                    public static int Choose(bool value)
                    {
                        if (value)
                        {
                            return 1;
                        }
                        return 2;
                    }
                }
                """;
            await File.WriteAllTextAsync(Path.Combine(root, "src/Subject.cs"), source);
            foreach (string suite in new[] { "A", "B" })
            {
                string directory = Path.Combine(root, "tests", suite);
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(
                    Path.Combine(directory, "Same.csproj"),
                    FixtureProject.Replace("src/Subject.csproj", "../../src/Subject.csproj", StringComparison.Ordinal));
                string probe = $$"""
                    using Xunit;
                    public class Probe
                    {
                        [Fact]
                        public void CoversOneBranch() => Assert.Equal({{(suite == "A" ? "1" : "2")}}, Subject.Choose({{(suite == "A" ? "true" : "false")}}));
                    }
                    """;
                await File.WriteAllTextAsync(Path.Combine(directory, "Probe.cs"), probe);
            }

            CliArguments options = new(CliMode.AllSrc, [])
            {
                TestProjects = ["tests/**/*.csproj"],
                Framework = "net8.0",
                CrapThreshold = 2,
            };
            SpawnResult result = await RunCliAsync(root, "unused", root, options);
            string diagnostics = result.StandardOutput + Environment.NewLine + result.StandardError;
            result.ExitCode.Should().Be(0, diagnostics);
            result.StandardOutput.Should().Contain("CRAP Report", diagnostics);
            string[] directories = ["coverage/project-0001", "coverage/project-0002"];
            var reports = directories
                .SelectMany(directory => CoverageReportLocator.LocateInResultsDirectory(Path.Combine(root, directory))).ToArray();
            reports.Should().HaveCount(2, diagnostics);
            string[] sources = [Path.Combine(root, "src/Subject.cs")];
            foreach (string report in reports)
            {
                var individual = CrapAnalyzer.Analyze(sources, report).Single();
                individual.CoveragePercent.Should().BeLessThan(100, diagnostics);
                individual.CrapScore.Should().BeGreaterThan(2, diagnostics);
            }

            var aggregate = CrapAnalyzer.AnalyzeCoverage(sources, CoberturaCoverageParser.ParseReports(reports)).Single();
            aggregate.CoveragePercent.Should().Be(100, diagnostics);
            aggregate.CrapScore.Should().Be(2, diagnostics);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<SpawnResult> RunCliAsync(
        string workingDirectory, string projectPath, string markerDir, CliArguments options)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // The spawned `dotnet test` host (and its testhost child) inherit this, so the fixture [Fact]s can
        // observe where to drop their markers.
        startInfo.Environment["CRAP4CSHARP_FILTER_MARKER_DIR"] = markerDir;
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Microsoft.Crap4CSharp.dll"));
        startInfo.ArgumentList.Add("src/Subject.cs");
        foreach (string selector in options.TestProjects.Count == 0 ? new[] { projectPath } : options.TestProjects)
        {
            startInfo.ArgumentList.Add("--test-project");
            startInfo.ArgumentList.Add(selector);
        }

        startInfo.ArgumentList.Add("--crap-threshold");
        startInfo.ArgumentList.Add(options.CrapThreshold.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--framework");
        startInfo.ArgumentList.Add(options.Framework!);
        if (options.AllTests)
        {
            startInfo.ArgumentList.Add("--all-tests");
        }
        else if (options.TestFilter is not null)
        {
            startInfo.ArgumentList.Add("--test-filter");
            startInfo.ArgumentList.Add(options.TestFilter);
        }

        using Process process = new() { StartInfo = startInfo };
        using CancellationTokenSource cts = new(SpawnTimeoutMs);

        process.Start();

        // Drain BOTH streams asynchronously to avoid a full-pipe deadlock; await async waits (no blocking
        // WaitForExit) to stay clear of CA1849.
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cts.Token);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"nested 'dotnet test' did not exit within {SpawnTimeoutMs} ms.");
        }

        return new SpawnResult(process.ExitCode, await stdout, await stderr);
    }

    private sealed record SpawnResult(int ExitCode, string StandardOutput, string StandardError);
}

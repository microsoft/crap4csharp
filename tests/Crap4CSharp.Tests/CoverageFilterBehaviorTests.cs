namespace Microsoft.Crap4CSharp.Tests;

using System.Diagnostics;

// The highest-risk behavioral proof (contract §4.5): unlike CoverageRunnerTests (which pins only the --filter
// TOKEN via a fake), this test spawns a REAL nested `dotnet test <Fixture.Tests.csproj> --filter
// "type!=IntegrationTests"` and observes WHICH methods actually execute -- empirically proving the VSTest
// exclusion-form semantics departure #10 depends on: an UNTAGGED test RUNS (an absent `type` property satisfies
// `!=` any value) while a [Trait("type","IntegrationTests")] test is EXCLUDED. The fixture is an isolated,
// minimal net8.0 xUnit project built in a temp dir OUTSIDE the repo (no Directory.Build.props / analyzer /
// warnings-as-errors inheritance) with its OWN nuget.config clearing sources to nuget.org-only, and package
// versions pinned to those the solution already restored (xunit 2.5.3, xunit.runner.visualstudio 2.5.3,
// Microsoft.NET.Test.Sdk 17.8.0) so restore hits the WARM cache -- no network. Each fixture [Fact] writes an
// env-var-directed marker file (CRAP4CSHARP_FILTER_MARKER_DIR, inherited by the spawned test host); the
// assertions read the markers. No --collect -- this proves EXECUTION, not coverage. ProgramTests-style async
// stdout/stderr drain + cancellation-token timeout + Kill(entireProcessTree) on timeout; temp tree removed in a
// finally. This test itself carries NO [Trait] (D-T8: crap4csharp marks none of its own tests), so it runs in
// the default suite -- and it is precisely what verifies the scope-(a) TARGET-filter semantics.
public class CoverageFilterBehaviorTests
{
    // A cold restore(warm-cache)+build+run of a 2-method fixture is far heavier than ProgramTests' `dotnet <dll>`
    // spawns; allow a generous ceiling.
    private const int SpawnTimeoutMs = 120_000;

    private const string FixtureProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <Nullable>disable</Nullable>
            <ImplicitUsings>disable</ImplicitUsings>
            <IsPackable>false</IsPackable>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
            <PackageReference Include="xunit" Version="2.5.3" />
            <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
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
            }
        }
        """;

    [Fact]
    public async Task UnitOnlyFilterRunsUntaggedAndExcludesIntegration()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string projectPath = Path.Combine(tempDir, "Fixture.Tests.csproj");
            await File.WriteAllTextAsync(projectPath, FixtureProject);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "nuget.config"), FixtureNuGetConfig);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "FilterProbe.cs"), FilterProbeSource);
            string markerDir = Path.Combine(tempDir, "markers");
            Directory.CreateDirectory(markerDir);

            SpawnResult result = await RunDotnetTestAsync(tempDir, projectPath, markerDir);

            string diagnostics =
                $"exit={result.ExitCode}{Environment.NewLine}--- stdout ---{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}--- stderr ---{Environment.NewLine}{result.StandardError}";

            File.Exists(Path.Combine(markerDir, "untagged.marker"))
                .Should().BeTrue(
                    "an untagged test satisfies type!=IntegrationTests and MUST run. " + diagnostics);
            File.Exists(Path.Combine(markerDir, "integration.marker"))
                .Should().BeFalse(
                    "a type=IntegrationTests test MUST be excluded by the filter. " + diagnostics);
            result.ExitCode.Should().Be(
                0, "at least one test ran and passed (excluded != failed). " + diagnostics);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static async Task<SpawnResult> RunDotnetTestAsync(
        string workingDirectory, string projectPath, string markerDir)
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
        startInfo.ArgumentList.Add("test");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--filter");
        startInfo.ArgumentList.Add("type!=IntegrationTests");

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

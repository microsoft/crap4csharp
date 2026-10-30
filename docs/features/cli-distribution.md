# Feature: versioned CLI distribution

**Branch:** `vibe/cli-distribution`  
**Status:** Ready for review

## Requirements

Ship the existing JSON-capable CLI as an installable, versioned .NET tool for CRAP IDE users. Preserve the CLI behavior and keep publication as a separate decision. Mr. Das approved the .NET tool format and the `crap4csharp` command on 2026-10-28.

## Design options

| Option | Result | Tradeoff |
| --- | --- | --- |
| .NET tool | One NuGet package, one command on all supported hosts | Requires a compatible .NET runtime and a separate NuGet publication |
| Platform archives | Native/self-contained downloads per OS and architecture | Larger release matrix and more installation paths to document |

**Decision:** package `Crap4CSharp.Tool` as a framework-dependent .NET tool targeting .NET 8, with command `crap4csharp`. The package ID distinguishes the maintained fork from a Microsoft-owned package name. Publish only after separate authorization; the local package and CI artifact are reviewable first.

The package declares the repository's CC0-1.0 license and contributor attribution. Its NuGet metadata must not imply that Microsoft publishes or endorses this fork.

## Slices

| Slice | Outcome |
| --- | --- |
| S1 | Package metadata and installation instructions |
| S2 | Build, install, and JSON smoke test from the packaged tool |
| S3 | CI artifact and cross-host validation |

## Risks and limits

- The upstream JSON contribution is still under review. This package pins the fork's tested contract; an upstream release can replace it after a compatibility check.
- `crap4csharp` invokes `dotnet test` on target projects, so users need a suitable .NET SDK and the target's test dependencies even though the tool itself targets .NET 8.
- NuGet package ID availability and publisher ownership must be checked again before public publication.
- The package contains runtime dependency assemblies. Review and include their third-party license notices before public publication.

## Local verification

- With .NET SDK 8.0.416 in a network-isolated container, locked restore, Release pack, local tool installation, `--help`, and empty JSON output passed.
- The packaged command analyzed this checkout, ran 259 passing tests, emitted a populated JSON report and returned the expected threshold exit code `2`.
- The Release development suite passed 259 tests. The package metadata contains the CC0-1.0 license and README, and the archive contains no `.cs` source files.

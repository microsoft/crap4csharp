---
name: build-and-test
description: Builds crap4csharp and runs unit tests (excludes integration tests). Used for fast feedback.
---

## Commands

Run from the repository root:

    dotnet build crap4csharp.sln --configuration Release
    dotnet test crap4csharp.sln --configuration Release --filter "Category!=Integration"

## Pass criteria

- Build succeeds with **zero warnings and zero errors** (warnings are errors in Release).
- All unit tests pass.

Integration tests (real `git`, spawning the built CLI, spawning processes) are tagged
`[Trait("Category", "Integration")]` and excluded here; run `build-test-full` for those.

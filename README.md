# crap4csharp

## Attribution

`crap4csharp` continues the lineage of Robert C. ("Uncle Bob") Martin's original **crap4clj**, and is
a C# port of its Java sibling **crap4java**.

---

`crap4csharp` is a standalone CRAP metric tool for C# projects, modeled after `crap4java`.

It combines method cyclomatic complexity with Coverlet (Cobertura) method coverage and reports CRAP
scores. On each run it deletes stale coverage artifacts, runs coverage, then analyzes the selected
files.

## Formula

`CRAP = CC^2 * (1 - coverage)^3 + CC`

- `CC` is cyclomatic complexity.
- `coverage` is method coverage fraction from Cobertura line counters (the .NET analog of JaCoCo
  `INSTRUCTION` counters).

## Module & Test Resolution

Each invocation resolves the code unit under analysis and the test project that exercises it, both
**bounded** by the invocation directory (the walk never climbs above it):

1. **Owning project** — the nearest `.csproj` **at or above** the analyzed C# files. `.sln` is not a
   marker; the owning unit is a `.csproj`. All analyzed files must resolve to a **single** owning project
   (one owning project per run).
2. **Test project** — `<Project>.Tests.csproj` **or** `<Project>.UnitTests.csproj` whose
   `ProjectReference`s **transitively** include the owning project. crap4csharp runs **only** that test
   project, and only its **unit tests** (see *Unit-test determination* below).
3. **Fail fast (exit 1)** when there is **no owning `.csproj`**, the analyzed files **span multiple
   projects**, or there is **no matching test project** — each with a greppable stderr message.

**Baseline requirements:** the target follows the `<Project>.Tests`/`.UnitTests` naming convention, the
test project references `coverlet.collector`, and a run targets one owning project.

### Unit-test determination (on an analyzed target)

crap4csharp runs **only the unit tests** of the target it analyzes, mirroring `mutate4csharp`:

- A target test counts as a **unit test** unless it is tagged `[Trait("type", "IntegrationTests")]`.
  Tests tagged `type` = `UnitTests` or `Unit`, **and untagged tests**, all count as unit tests and run.
- This is realized by `--filter "type!=IntegrationTests"`. VSTest treats an **absent** `type` property as
  satisfying `!=` any value, so **untagged tests are included** — the exclusion form is deliberate (an
  inclusion form would wrongly drop untagged tests). Only `type=IntegrationTests` tests are excluded.
- This is a deliberate departure from `crap4java`, which ran *all* of a module's tests.

> **Two distinct scopes — do not conflate.** The `type` trait above governs **which tests crap4csharp
> runs on a *target project* it analyzes**. It is separate from **how crap4csharp categorizes its own dev
> tests** for the tight agentic loop (see *Test policy* below), which uses the `Category` trait example
> and marks none — different trait key, different scope.

## Coverage Pipeline

Coverage runs **exactly once**, against the resolved test project:

1. Delete stale coverage artifacts:
   - `coverage/`
2. Run `dotnet test <TestProject>.csproj --collect:"XPlat Code Coverage" --filter
   "type!=IntegrationTests" --results-directory coverage`
3. Read the produced `coverage.cobertura.xml` (exactly one report — one owning project → one test project
   → one report)
4. Analyze the selected C# files

## Build and Test

```bash
dotnet test
```

## Test policy (agentic dev loop)

This section is about **crap4csharp's OWN dev-test suite** — a **distinct scope** from the *Unit-test
determination* convention above, which governs the tests of a *target project* crap4csharp analyzes
(trait key `type`). Here the trait-key example is `Category`, and crap4csharp marks none.

Tight agentic development loops run the **unit tests by default** for fast feedback.

- **Unit-test convention:** a crap4csharp test is treated as a **unit test unless it is explicitly marked
  an integration test** (e.g. `[Trait("Category", "Integration")]`).
- crap4csharp marks **none** of its tests, so **all** of them are treated as unit tests and run by
  default — including the process- and `git`-touching tests.
- Running the **full** suite (including any long-running tests) requires **explicit user approval**,
  because slow tests delay the tight loop.

## Run

Build:

```bash
dotnet build -c Release
```

From the project root you want to analyze:

```bash
dotnet run --project src/Crap4CSharp -c Release
```

## CLI

```text
--help                Print usage to stdout
(no args)             Analyze all C# files under src/
--changed             Analyze changed C# files under src/
<file ...>            Analyze only these files
<directory ...>       Analyze all C# files under each directory's src/ subtree
```

Examples:

```bash
dotnet run --project src/Crap4CSharp -c Release -- --help
dotnet run --project src/Crap4CSharp -c Release
dotnet run --project src/Crap4CSharp -c Release -- --changed
dotnet run --project src/Crap4CSharp -c Release -- src/Sample.cs
dotnet run --project src/Crap4CSharp -c Release -- project-a project-b
```

## Exit codes

- `0` success, threshold respected
- `1` invalid CLI usage, or a fatal error — no owning `.csproj`, files span multiple projects, no
  matching test project, no tests ran, or no coverage produced (see *Module & Test Resolution* / Notes)
- `2` CRAP threshold exceeded (`> 8.0`)

## Notes

- **Fail fast:** if a module runs no tests or produces no coverage, `crap4csharp` exits non-zero
  rather than continuing — a deliberate, stricter departure from `crap4java`. A method simply absent
  from an otherwise-populated report is still reported as `N/A`.
- **One owning project, one report:** crap4csharp resolves a single owning `.csproj` and its single
  `<Project>.Tests`/`.UnitTests` project, so `dotnet test --collect` emits **exactly one**
  `coverage.cobertura.xml` — no multi-report ambiguity. If the analyzed files span more than one owning
  project, crap4csharp **fails fast** (exit 1) rather than silently under-reporting; narrow the run to a
  single project.
- **Async & iterator coverage (no longer a blind spot):** coverlet records an `async`/iterator method's
  executable lines on its compiler-generated state machine (`<Method>d__N`), not on the method itself.
  crap4csharp attributes that state machine's `MoveNext` coverage back to the source method, so a
  **tested** async/iterator method reports its real coverage and CRAP (and can trip the exit-2 gate)
  instead of `N/A`. **Lambdas & local functions too:** coverage the compiler hosts on display classes
  — lambda bodies (`<Method>b__N`), local functions (`<Method>g__L|N`) and async locals — is likewise
  folded back into the enclosing source method, so an untested lambda or local function drags its
  method's real coverage and CRAP down instead of letting the method escape the exit-2 gate.
- Report output is sorted by CRAP descending, with `N/A` at the bottom.

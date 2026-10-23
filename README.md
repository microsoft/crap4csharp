# crap4csharp

## Attribution

`crap4csharp` continues the lineage of Robert C. ("Uncle Bob") Martin's original **crap4clj**, and is
a C# port of its Java sibling **crap4java**.

---

`crap4csharp` is a standalone CRAP metric tool for C# projects, modeled after `crap4java`.

It combines the cyclomatic complexity of each logic-bearing member — methods, property/indexer
accessors, operators, conversions, finalizers, custom event accessors — with Coverlet (Cobertura)
per-member coverage and reports CRAP scores. On a normal run it deletes stale coverage artifacts, runs
coverage, then analyzes the selected files. `--dry-run` previews the selection and commands without these
side effects.

## Formula

`CRAP = CC^2 * (1 - coverage)^3 + CC`

- `CC` is cyclomatic complexity, computed per logic-bearing member.
- `coverage` is that member's coverage fraction from Cobertura line counters (the .NET analog of JaCoCo
  `INSTRUCTION` counters).

## Module & Test Resolution

Each invocation resolves the code unit under analysis and the test projects that exercise it, both
**bounded** by the invocation directory (the walk never climbs above it):

1. **Owning project** — the nearest `.csproj` **at or above** the analyzed C# files. `.sln` is not a
   marker; the owning unit is a `.csproj`. All analyzed files must resolve to a **single** owning project
   (one owning project per run).
2. **Test project** — `<Project>.Tests.csproj` **or** `<Project>.UnitTests.csproj` whose
   `ProjectReference`s **transitively** include the owning project. crap4csharp runs **only** that test
   project, by default only its **unit tests** (see *Unit-test determination* below).
   If more than one candidate qualifies, resolution fails with an ordinal-sorted candidate list and a
   `--test-project` hint, rather than silently choosing one.
   To select multiple suites explicitly, repeat `--test-project <path-or-glob>` or use a quoted glob.
   Explicit selection bypasses naming only: **every** matched project must have a genuine
   direct/transitive reference chain to the same owner using the existing bounded property evaluator.
   Each selector must match at least one eligible project; a valid selector never masks an invalid or
   unmatched one. Paths must be existing in-root `.csproj` files outside `bin`/`obj`; graph traversal
   never reads out-of-root reference edges. Absolute literal paths inside the root remain supported.
   Selection is deduplicated and sorted by ordinal absolute path; each project runs once.
   **Without explicit selectors, automatic naming and ambiguity errors are unchanged.**
3. **Fail fast (exit 1)** when there is **no owning `.csproj`**, the analyzed files **span multiple
   projects**, or there is **no matching/unambiguous test project** — each with a greppable stderr message.

**Baseline requirements:** the target follows the `<Project>.Tests`/`.UnitTests` naming convention
(or uses `--test-project`), the test project references `coverlet.collector` and runs with VSTest, and a
run targets one owning project. Owning-project resolution is unchanged; there is no solution-wide or
multiple-owner analysis.

### Unit-test determination (on an analyzed target)

By default, crap4csharp runs **only the unit tests** of the target it analyzes, mirroring `mutate4csharp`:

- A target test counts as a **unit test** unless it is tagged `[Trait("type", "IntegrationTests")]`.
  Tests tagged `type` = `UnitTests` or `Unit`, **and untagged tests**, all count as unit tests and run.
- This is realized by `--filter "type!=IntegrationTests"`. VSTest treats an **absent** `type` property as
  satisfying `!=` any value, so **untagged tests are included** — the exclusion form is deliberate (an
  inclusion form would wrongly drop untagged tests). Only `type=IntegrationTests` tests are excluded.
- This is a deliberate departure from `crap4java`, which ran *all* of a module's tests.
- `--test-filter <expression>` **replaces**, rather than combines with, this default. The exact native
  VSTest expression is forwarded as one process argument. Only nonblankness is validated locally;
  VSTest diagnoses malformed expressions. Failed commands are never retried without a filter.
  Native filter errors in the tool-owned TRX diagnostics fail with exit 1 before any CRAP report,
  even if an adapter returns success. Valid zero-match filters have no separate zero-tests gate.
- `--all-tests` omits the filter entirely. It conflicts with `--test-filter`.

> **Two distinct scopes — do not conflate.** The `type` trait above governs **which tests crap4csharp
> runs on a *target project* it analyzes**. It is separate from **how crap4csharp categorizes its own dev
> tests** for the tight agentic loop (see *Test policy* below), which uses the `Category` trait example
> and marks none — different trait key, different scope.

## Coverage Pipeline

All selected projects and known VSTest routing are validated **before** deleting coverage or running
any command. Coverage then runs **once per selected test project**, sequentially in ordinal order:

1. Delete stale `coverage/` artifacts **once** for the entire batch.
2. Run `dotnet test <TestProject>.csproj --collect:"XPlat Code Coverage" --filter
   "type!=IntegrationTests" --results-directory coverage --logger "trx;LogFilePrefix=crap4csharp-native"`
   for a singleton. Multiple projects instead use distinct `coverage/project-0001`,
   `coverage/project-0002`, … directories assigned by sorted order, not project basename.
   The same filter/all-tests and framework options apply to every command. No concurrency or retries.
3. Validate each results directory's tool-owned `crap4csharp-native*.trx` native diagnostics (distinct names per
   framework/run). Missing, unreadable or malformed diagnostics, native RunInfo errors, and native
   `Exception filtering tests` warnings fail with exit 1. Console stdout/stderr remain visible.
   Only deployment copies declared by that directory's TRX metadata are excluded from discovery.
   Each test project must independently produce **exactly one usable** `coverage.cobertura.xml`.
4. A single selected suite retains legacy parsing, including distinct body/synthetic hit/miss counters
   on a shared source line. For multiple selected suites, combine reports **in memory into one CRAP
   report**: union each attributed member's executable source-line identities, covered when **any
   selected suite** hits that line. The denominator is
   unique observed lines, **not an average of percentages or a sum of counters**. Missing coverage
   in one suite does not erase another suite's observations. Existing overload/file segregation and
   async/lambda/local-function attribution apply across all suites before scoring.
5. Analyze the selected C# files and apply the threshold to the **combined** scores.

Any project's command, diagnostics, or missing/empty/malformed/unreadable/multiple-report failure exits
1 **without a normal CRAP report or partial threshold result**. Partial artifacts may remain for diagnosis.

`--framework <tfm>` adds one `--framework` pair to that command. Lists, whitespace, separators and
argument/property assignments are rejected; the runner validates actual framework availability. This
supports selecting one TFM of a multi-targeted project. The report gates still require exactly one
populated report **per project**; a multi-report error recommends `--framework`.

### Supported runner boundary

The supported pipeline is **VSTest + coverlet.collector**, not Microsoft.Testing.Platform (MTP).
Before deleting coverage (also during dry-run), a read-only check rejects known MTP routing:

- The nearest applicable `global.json` from the invocation directory upward selects
  `test.runner = Microsoft.Testing.Platform`. This SDK-config search may read above the invocation
  root; it never writes there.
- `DOTNET_TEST_RUNNER` overrides that setting, case-insensitively, **when the same config explicitly
  pins SDK 11 preview 6 or later** (the SDK boundary introducing the variable). Empty/unrecognized values
  do not override it. On older pinned SDKs the variable is ignored.
- Straightforward unconditional `TestingPlatformDotnetTestSupport=true` in the test project or
  nearest conventional `Directory.Build.props`/`Directory.Build.targets` within the invocation root.
  Literal overrides are read in props → project → targets order.

This is deliberately **not** SDK resolution or MSBuild evaluation: SDK roll-forward/unpinned SDK
selection, custom imports, conditional/computed properties, and conventional build files above the
invocation root are not evaluated. In particular, an effective runner environment override with no
qualifying explicit SDK pin cannot be determined by this bounded check. Keep the target on VSTest;
successful dry-run is not proof that uninspected configuration is compatible. An MTP package reference
alone does **not** imply MTP routing and is not rejected.

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

## Install as a .NET tool from source

The portable, AnyCPU, framework-dependent package supports **Windows and Linux, x64 and ARM64**.
Install the matching native-architecture **.NET 8 runtime and SDK**: the runtime runs the tool;
the SDK is needed to pack from source and to run `dotnet test` during analysis.
From this source repository's root, pack and install globally from the local package feed
(version: `v1.1.0`):

```bash
dotnet pack src/Crap4CSharp/Crap4CSharp.csproj -c Release -o ./artifacts/packages
dotnet tool install --global Microsoft.Crap4CSharp --version v1.1.0 --add-source "$PWD/artifacts/packages"
```

PowerShell equivalent:

```powershell
dotnet pack src/Crap4CSharp/Crap4CSharp.csproj -c Release -o ./artifacts/packages
dotnet tool install --global Microsoft.Crap4CSharp --version v1.1.0 --add-source (Join-Path (Get-Location).Path "artifacts/packages")
```

The package ID defaults to `Microsoft.Crap4CSharp`; the installed command is `crap4csharp`.
These instructions use a locally built package, not a published NuGet release. Ensure the
.NET global tools directory is on your `PATH`.

From the **target repository root** you want to analyze:

```bash
crap4csharp --help
crap4csharp
crap4csharp --changed
crap4csharp src/Sample.cs
```

The target must meet the requirements in *Module & Test Resolution* above.

## Releases

Merge a same-repository PR from `releases/vX.X.X` into `master` to publish GitHub release `vX.X.X`
with its `.nupkg` attached. Use canonical numeric components in `0..65534` (no prerelease suffix).
The workflow updates the project's `Version` on `master` if necessary, then builds/tests that exact
commit and smoke-tests the shared package on Windows/Linux x64/ARM64 before publishing.
Download the release package into a local feed and install using the commands above with its version.
There is no NuGet publishing. Publishing fails if `master` advances, branch protection blocks the
version-update push, or the tag already exists; partial retries require manual resolution.

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
  matching/unambiguous test project, incompatible known routing, a failed coverage command, or missing,
  multiple, or empty coverage reports (see *Module & Test Resolution* / Notes)
- `2` CRAP threshold exceeded (strict unrounded maximum `> --crap-threshold`, default `8.0`)

### Selection and diagnostics options

Options may appear among source paths, with `--changed`, or alone (options-only selects all `src/`
sources). `--changed` still conflicts with file paths. Only `--test-project` is repeatable, and each
occurrence consumes one value. Unknown options, all other duplicates (including boolean flags),
missing/blank values, and conflicting options fail with exit 1. Exact `--help` wins over all
other arguments. There is no arbitrary runner-argument passthrough or `--` separator.

Quote globs so the shell does not expand them. Use `/` separators (user backslashes are normalized).
Matching is ordinal, case-sensitive, and against the **entire root-relative path**: `*` spans characters
within one segment, `?` matches one character within a segment, and a whole-segment `**` spans zero or
more directories. Spaces in paths work. Absolute globs, root-escaping `..`, exclusions, character
classes, brace expansion, and other glob syntax are unsupported. Globs do not descend through directory
links or `bin`/`obj`; explicit paths cannot escape the root through links.

`--crap-threshold <number>` accepts an invariant finite nonnegative number without thousands separators.
Equality passes; the strict comparison uses the original unrounded score. Threshold diagnostics show
round-trip precision, independently of the unchanged report table. `N/A` members do not contribute;
an all-`N/A` maximum remains zero, including when the threshold is zero.

`--dry-run` resolves sources, the owner, and **all** test projects, then prints deterministic selected files,
the owner, filter mode/value, threshold, requested framework (or
`project default; no framework override`), invocation cwd, and **every** project with its assigned results
directory and escaped JSON command-token array in execution order.
The same planned commands supply execution. It does not build, restore, run tests, create/delete result
directories, read coverage/TRX reports, or calculate a score/threshold outcome. `--changed` may still invoke git.
A valid plan exits 0, a parse/resolution failure exits 1, and no files keeps the existing exit-0 shortcut.

```bash
crap4csharp --test-project tests/CustomChecks/CustomChecks.csproj --framework net8.0 --dry-run
crap4csharp src/Foo/Service.cs --test-project "tests/**/*.Tests.csproj" --framework net8.0
crap4csharp src/Foo/Service.cs --test-project tests/Foo.UnitTests/Foo.UnitTests.csproj --test-project tests/Foo.MoreTests/Foo.MoreTests.csproj
crap4csharp src/Foo/Service.cs --test-filter 'FullyQualifiedName~ServiceTests' --crap-threshold 10.5
crap4csharp --changed --all-tests --framework net8.0
```

## Notes

- **Fail fast:** missing or empty coverage fails rather than silently continuing — a deliberate,
  stricter departure from `crap4java`. There is **no separate zero-tests gate**; the runner's exit and
  coverage-report gates determine the outcome. A member simply absent from an otherwise-populated report
  is still reported as `N/A`.
- **One owning project, one combined report:** crap4csharp resolves a single owning `.csproj` and
  requires **exactly one** `coverage.cobertura.xml` **per selected test project**. Use `--framework <tfm>` when
  necessary to select a single TFM. If the analyzed files span more than one owning project,
  crap4csharp **fails fast** (exit 1) rather than silently under-reporting; narrow the run to a single
  project.
- **Async & iterator coverage (no longer a blind spot):** coverlet records an `async`/iterator method's
  executable lines on its compiler-generated state machine (`<Method>d__N`), not on the method itself.
  crap4csharp attributes that state machine's `MoveNext` coverage back to the source method, so a
  **tested** async/iterator method reports its real coverage and CRAP (and can trip the exit-2 gate)
  instead of `N/A`. **Lambdas & local functions too:** coverage the compiler hosts on display classes
  — lambda bodies (`<Method>b__N`), local functions (`<Method>g__L|N`) and async locals — is likewise
  folded back into the enclosing source method, so an untested lambda or local function drags its
  method's real coverage and CRAP down instead of letting the method escape the exit-2 gate.
- **Executable-member decomposition:** logic-bearing members beyond plain methods each get their own
  CC/coverage/CRAP row under their **CLR name** — property/indexer accessors
  (`get_Value`/`set_Value`/`get_Item`), user-defined operators (`op_Addition`, `op_UnsignedRightShift`),
  conversions (`op_Implicit`/`op_Explicit`), finalizers (`~T` → `Finalize`) and custom event accessors
  (`add_Evt`/`remove_Evt`). Bodyless members carry no row (auto-properties, abstract/`get;set;`
  accessors, field-like events); **constructors stay excluded** (crap4java §8.1). A renamed indexer
  (`[IndexerName]`) or explicit-interface member whose CLR name does not match its coverage entry
  degrades to a safe per-member `N/A`, never a false pass.
- Report output is sorted by CRAP descending, with `N/A` at the bottom.

# Feature: crap4csharp — faithful C# port of crap4java
**Branch:** vibe/crap4csharp-port
**Status:** Planning

## Requirements

Convert the read-only Java tool `crap4java` (`../crap4java`) into its C# equivalent, `crap4csharp`,
with **high fidelity**. The C# tool analyzes **C# projects**. Preserve class decomposition, CRAP
formula, CLI contract, report format, and exit codes; adapt only the ecosystem adapters. Every Java
test gets a faithful C# counterpart with identical verification. Use popular OSS libraries for
equivalents (escalate any non-1:1 / multi-candidate / inexact choice to Mr. Das). Locked choices,
the augmented complexity node set, and approved deliberate departures are in `docs/decisions.md`.

## Design Options (Ox)

### O1 — True C# equivalent (analyze C# projects) — **chosen**
- Description: `crap4csharp` analyzes C# projects. Roslyn replaces the JDK tree API; Coverlet →
  Cobertura replaces JaCoCo; `dotnet test`/MSBuild replaces Maven. Same architecture and CRAP logic.
- Pros: the natural `crap4clj → crap4java → crap4csharp` lineage; genuinely useful; high fidelity of
  structure/logic/CLI/report/exit codes with only three adapters swapped.
- Cons: three adapters need real redesign; coverage granularity differs from JaCoCo (documented).

### O2 — Literal transliteration (still analyze Java projects)
- Description: a C# rewrite that still shells out to Maven + JaCoCo and parses Java.
- Pros: highest source-level fidelity.
- Cons: near-zero practical value; still depends on the Java toolchain.

**Recommended: O1 — the true C# equivalent. (Ruled by Mr. Das.)**

## Slices (Sx)

| Slice | Outcome | Depends on |
|-------|---------|------------|
| S1 | Repo/solution scaffolding + settings build; empty `dotnet test` runs | - |
| S2 | Pure core (CRAP formula, report, CLI parse, domain types) with parity tests | S1 |
| S3 | Ecosystem adapters (process exec, file finders, Roslyn parser, Cobertura parser) with parity tests | S1 |
| S4 | Composition + CLI wiring + fail-fast + end-to-end | S2, S3 |

## Tasks (Tx)

One or more tasks per slice. Full task detail and the fail-fast delta live in `docs/decisions.md`.

| #   | Slice | Task | Status | Commit |
|-----|-------|------|--------|--------|
| T1  | S1 | Repo/solution scaffolding: `crap4csharp.sln`, `src/Crap4CSharp` (Exe, net8.0, Roslyn ref), `tests/Crap4CSharp.Tests` (xUnit + coverlet + FA 7.x), import shared `.targets`; empty build + `dotnet test` run | Pending | - |
| T2  | S2 | Domain types: `CliMode`, `CliArguments`, `CoverageData`(+`CoveragePercent`), `MethodDescriptor`(+`TypeName`), `MethodMetrics` | Pending | - |
| T3  | S2 | `CrapScore` + `CrapScoreTests` (oracles 5.0/30.0/18.648/null) | Pending | - |
| T4  | S2 | `ReportFormatter` + golden `ReportFormatterTests` (InvariantCulture, `"\n"`) | Pending | - |
| T5  | S2 | `CliArgumentsParser` + `CliArgumentsParserTests` (7 cases) | Pending | - |
| T6  | S3 | `ICommandExecutor` + `ProcessCommandExecutor` + tests | Pending | - |
| T7  | S3 | `SourceFileFinder` (`src/**/*.cs`, exclude `bin`/`obj`, ordinal sort) + tests | Pending | - |
| T8  | S3 | `ChangedFileDetector` (git porcelain) + integration tests | Pending | - |
| T9  | S3 | `CSharpMethodParser` + `ComplexityWalker` (augmented node set) + CC oracle tests | Pending | - |
| T10 | S3 | `CoberturaCoverageParser` (+ empty-report case) + tests; pin FQN normalization vs a real coverlet sample | Pending | - |
| T11 | S4 | `CrapAnalyzer` (exact→nearest-line lookup, per-method `TypeName`) + tests | Pending | - |
| T12 | S4 | `CoverageRunner` (`dotnet test --collect`) + `CoverageReportLocator` + tests | Pending | - |
| T13 | S4 | `ModuleRootResolver` (nearest `.sln` → `.csproj` → root) + tests | Pending | - |
| T14 | S4 | `CliApplication` + tests; **fail-fast gate** (no-coverage/empty-report → exit 1) | Pending | - |
| T15 | S4 | `Program` entry (+ `CoverageException`) + integration tests (spawn built exe) | Pending | - |
| T16 | S4 | README usage section + end-to-end smoke (positive + negative fail-fast) | Pending | - |

Critical path: T1 → T2 → T9/T10 → T11 → T14 → T15 → T16. T3/T4/T5 and T6/T7/T8/T13 parallelize early.

## Risks (Rx)

- R1: Coverage granularity — Cobertura **line** counters ≠ JaCoCo **instruction** counters; absolute
  coverage numbers differ for identical code. Algorithm/attribution preserved. (Documented.)
- R2: `coverlet.collector` prerequisite — the analyzed project's **test project** must reference it,
  and the module root must be a `.sln` so `dotnet test` runs tests. Otherwise fail-fast fires.
- R3: Cobertura FQN normalization — nested/generic type naming (`Outer.Inner`, backtick arity) and
  compiler-generated names (`get_`/`set_`, lambdas, async `MoveNext`) must be normalized/ignored to
  match parsed method FQNs. Pin against a real coverlet sample (T10).
- R4: Fail-fast changes ~4 parity tests (+2 new) — the one knowing test-verification break.
- R5: FluentAssertions v8 licensing — the `[7.0.0,8.0.0)` pin + lock file must hold.

## Assumptions (Ax)

- A1: The repo has **12** Java test files (not 13); all 12 are paired 1:1 (mod. ecosystem).
- A2: Coverage is carried as **percent (0–100)** end-to-end, matching the crap4java *implementation*
  (spec §10 says fraction; the impl uses percent — impl wins for fidelity).
- A3: crap4csharp analyzes **external** C# projects (like crap4java analyzes external Maven modules);
  its own `Microsoft.Crap4CSharp` namespace does not affect analysis logic.
- A4: `net8.0`; SDK per `global.json`. Roslyn parses regardless of the tool's own LangVersion.
- A5: The `mutate4java-manifest` trailers in the Java sources are unrelated tooling artifacts —
  excluded from the port.

## Deferrals (Dx)

- D1: O1 — `ExitCode` enum + typed failure exceptions (greenlit, after baseline green).
- D2: O2 — subprocess timeout + cancellation (greenlit, after baseline green).
- D3: O3 — typed structured coverage lookup replacing string-key map (reshapes parity tests; Mr. Das
  to schedule).
- D4: O4–O6 — fraction-coverage, globbing discovery, `[Theory]` consolidation (nice-to-have).

## Notes & Decisions

- Full locked decisions, the authoritative complexity node set, and the fail-fast semantics/exit-code/
  test-parity delta are in `docs/decisions.md`.
- Namespace is `Microsoft.Crap4CSharp` (per Mr. Das).
- This feature file + `docs/decisions.md` are seeded on `master`. Implementation runs on
  `vibe/crap4csharp-port`; JARVIS creates that branch and drives T1→T16 via Dave/Bhaskar, with Anders
  review per task.

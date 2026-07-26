# Feature: crap4csharp — faithful C# port of crap4java
**Branch:** vibe/crap4csharp-port
**Status:** In progress — **S2 complete** (T2 `545c000`, T3 `47c1a1c`, T4 `f830f0e`, T5 `1b7833b`); pure core landed, 16 tests green, 0/0 Release. Paused at S2 boundary for Mr. Das's steer before S3.

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
| T1  | S1 | Repo/solution scaffolding: `crap4csharp.sln`, `src/Crap4CSharp` (Exe, net8.0, Roslyn ref), `tests/Crap4CSharp.Tests` (xUnit + coverlet + FA 7.x), import shared `.targets`; empty build + `dotnet test` run | Done | `d3dd17f` |
| T2  | S2 | Domain types: `CliMode`, `CliArguments`, `CoverageData`(+`CoveragePercent`), `MethodDescriptor`(+`TypeName`), `MethodMetrics` | Done | `545c000` |
| T3  | S2 | `CrapScore` + `CrapScoreTests` (oracles 5.0/30.0/18.648/null) | Done | `47c1a1c` |
| T4  | S2 | `ReportFormatter` + golden `ReportFormatterTests` (InvariantCulture, `"\n"`) | Done | `f830f0e` |
| T5  | S2 | `CliArgumentsParser` + `CliArgumentsParserTests` (7 cases) | Done | `1b7833b` |
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

### Decisions ruled by Mr. Das at S1 boundary

- **C1 — Assembly structure → Option A (RULED).** Keep the single production assembly
  `src/Crap4CSharp`; "core has no ecosystem deps" is enforced by review/discipline, not a compile-time
  boundary. No Core/Adapters/Exe split now. Recorded in `.github/copilot-instructions.md`.
- **C2 — Test-naming under CA1707 → Option A (RULED).** Keep CA1707 on everywhere → PascalCase test
  names (no underscores); parity tests mirror Java *behavior*, not names. Recorded in
  `.github/copilot-instructions.md`.
- **C3 (process note).** The throwaway `ScaffoldingSanityTests.cs` is deleted by whichever task first
  adds real tests to `Crap4CSharp.Tests` (not carried to T16). *(Done in T2.)*

### Carry-forward watch-items (from Anders's T2 review — must be honored at the noted task)

- **W1 — CoverageData field naming.** *(RESOLVED — Mr. Das ruled RENAME.)* Fields renamed
  `MissedInstructions`/`CoveredInstructions` → `MissedLines`/`CoveredLines` (accurate for the
  line-counter Cobertura adapter; the JaCoCo "instruction" concept has no referent here). Recorded as
  **departure #4** in `docs/decisions.md`; landed in the W1 rename commit (Dave code + Anders register
  line, Bhaskar-verified 16/16).
- **W2 — T5 must decompose `CliArguments` assertions.** C# records use *reference* equality on the
  `IReadOnlyList<string> FileArgs` member (unlike Java's structural `List.equals`), so T5 tests must
  assert `Mode` and `FileArgs` **separately** (`FileArgs.Should().Equal(...)`), never whole-record
  equality — else a false parity break. (Mirrors how `crap4java`'s `CliArgumentsParserTest`
  decomposes.) *(Done in T5 — tests assert `Mode` + `FileArgs` separately; Bhaskar + Anders confirmed.)*
- **W3 — T9 populates `MethodDescriptor.TypeName` at construction.** Set it from the Roslyn
  enclosing-type symbol at parse time; no empty/placeholder value. Use named args at the call site
  (`Name` and `TypeName` are both `string` → transposition risk).
- **W4 — `null → "N/A"` invariant.** T4 (`ReportFormatter`) and T11 (`CrapAnalyzer`) must render a
  missing coverage/crap value as `N/A`; never let a default `0.0` leak in for "method absent from
  report" (that would report 0% as a real measurement).
- **W5 — Test-namespace nesting (convention).** Test classes live in `Microsoft.Crap4CSharp.Tests`
  (nested under the production `Microsoft.Crap4CSharp`), so production types resolve in tests with **no
  explicit `using`**; global usings exist only for `Xunit` + `FluentAssertions`. This is load-bearing
  for test authoring and reinforces the flat-namespace decision (keep domain types flat, no `Domain`
  namespace).
- **C4 (deferred trivia).** If ever packed as a `dotnet tool`, set `ToolCommandName=crap4csharp` so the
  CLI invocation name matches the contract.

### Carry-forward watch-items (from Anders's T5 review — for the T14/T15 CLI seam)

- **W6 — T14 catches `ArgumentException`, not narrower.** Java `parseArguments` catches
  `IllegalArgumentException` → prints `ex.Message` to **stderr** + usage → **exit 1**. Faithful port:
  `catch (ArgumentException)`. This also catches the CA1062 `ArgumentNullException` subtype (unreachable,
  same routing) — do **not** add a separate narrower `catch (ArgumentNullException)`.
- **W7 — Help short-circuits before file resolution, exit 0.** Java handles `HELP` inside
  `parseArguments` (print usage, exit 0) *before* `filesForMode`. Mirror in T14. Use a `switch`
  expression over all four `CliMode` arms with a `default` throw so a future enum value fails loud.
- **W8 — `AllSrc`/`ChangedSrc`/`Help` carry empty `FileArgs` by design.** Files for those modes come
  from downstream (`SourceFileFinder` T7 / `ChangedFileDetector` T8), **not** from `FileArgs`. T14 must
  not read empty `FileArgs` as "nothing to do."
- **W9 — Unknown-flag tolerance is contract.** `--bogus` is silently dropped by the `--` filter (parity
  test). Downstream must **never** re-validate/reject unknown flags. Only `--changed` + non-flag files is
  an error — and `--help` wins over `--changed` (checked first), so `--help --changed foo.cs` → `Help`,
  not a throw.
- **W10 — The `--changed` combined-args message is a user-facing contract.** Because `parseArguments`
  prints `ex.Message`, T14 should assert **stderr** contains `--changed cannot be combined with file
  arguments`. The parser tests correctly assert only the exception *type* (parity); the message
  assertion belongs at the T14 integration seam.
- **W11 — T10 must derive line counters, not read them ready-made (Anders, W1 review).** Cobertura
  emits per-line `hits`, not aggregate missed/covered per method. `CoberturaCoverageParser` (T10) must
  compute, within each method's line span, `CoveredLines = count(lines with hits > 0)` and
  `MissedLines = count(lines with hits == 0)` (missed = valid − covered). The rename makes the intent
  explicit but does not perform the mapping — keep it on T10's plate alongside the FQN-normalization pin
  (R3).

### Carry-forward watch-items (from Anders's T7 review — must be honored at the noted task)

- **W12 — AllSrc + directory-arg expansion must route through `SourceFileFinder` (T14).** Java calls
  `findAllJavaFilesUnderSrc` in **two** places: `ALL_SRC` mode and expanding an explicit **directory**
  argument (`CliApplication.explicitFiles`). T14 must send both through `SourceFileFinder` (not a
  re-enumeration), or the bin/obj build-output exclusion won't hold end-to-end.
- **W13 — Generated-code skew watch (T9/T11/T12).** The finder excludes `bin`/`obj` but intentionally
  **keeps** checked-in `*.g.cs`/`*.Designer.cs` under normal source folders (fidelity — Java analyzed
  generated `.java` under `src` too). Because T12 runs `dotnet test`, which **repopulates** `bin`/`obj`,
  confirm T9/T11 consume the finder's filtered list so freshly-generated build output never reaches the
  parser and skews CRAP. If checked-in generated files later prove to distort scores, escalate as a
  product call — do **not** silently broaden the exclusion (that breaks Java parity). Recorded as
  departure #5 in `docs/decisions.md`.
- **W14 — Private fields use `_camelCase` (T9).** `ComplexityWalker` needs a private mutable complexity
  counter; make it `_complexity` (or similar `_camelCase`). It is CA1707-exempt and Release-clean, and
  `_camelCase` is the `.editorconfig`-configured style. See the **C2 clarification** in
  `docs/decisions.md`; don't repeat T7's avoidance of a private field.

### Open decision for Mr. Das (from T5, design-lane)

- **D-T5 — CA1062 `ArgumentNullException.ThrowIfNull` idiom.** *(RESOLVED — Mr. Das ruled
  BELOW-THRESHOLD.)* The analyzer-forced null guards are a trivial idiomatic adaptation, **not** a
  logged departure: **no** standing-policy line is added to `docs/decisions.md`. Dave continues writing
  the CA1062-required guards exactly as before (code unchanged); Bhaskar/Anders treat them as
  non-noteworthy going forward.
- **D-T9 — Parser plumbing / signature (non-1:1).** *(OPEN — escalated to Mr. Das.)* Drop
  `JavaMethodParser.sourcePath`/`sourceUri` and their 2 tests (`buildsSourcePathAndUriFromClassNames`,
  `acceptsClassNamesWithJavaSuffix`) as javac file-URI plumbing with no Roslyn referent, and adopt
  `Parse(string source)` (drops the vestigial `className`). Anders recommends **Option A (drop both)**;
  the 7 remaining Java parser tests port as faithful behavioral counterparts. **Blocks T9** (parser
  signature + test set depend on the ruling). Design-lane FYIs (Anders, vetoable): collection scope =
  methods only; expression-bodied methods included; nested-type class-name format is a T10 pin.

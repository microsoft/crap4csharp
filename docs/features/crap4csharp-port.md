# Feature: crap4csharp — faithful C# port of crap4java
**Branch:** vibe/crap4csharp-port
**Status:** **Core port COMPLETE (S1–S4) + T20 exit-code parity + S5/T17 Model-B resolution** — T1–T16 landed (T6 `da02028`, T7 `1b90d24`, T8 `c023ef8`, T13 `e4d1329`, B1 ratify `d0784b3`, T9 `400d49d`, T10 `87488ce`, T11 `debf9de`, T12 `9041c2c`, T14 `6e8456c`, T15 `1f7581e`, T16 `ff422bd`), **T20** (`4e41c62`), **T17** (`d86feb6`), **T21** (`b489a08`); **172 tests green, 0/0 Release, CI green**. The full CRAP pipeline is shipped: parse → **resolve bounded owning `.csproj` + `.Tests`/`.UnitTests` test project once (#9)** → run coverage once on that test project (**unit-only `--filter`, #10**) → single report → fail-fast gate (#1, exit 0/1/2) → analyze → format → threshold; `Program.Main` catches **every** escaping exception → exit 1 (T20: full Java `main throws Exception` parity via a scoped `[Program.cs]` CA1031 relaxation; `CoverageException` retained). S6/T18 independent clean-room eval **executed (report-only, gpt-5.6-sol)** — verdict relayed to Mr. Das; T20 closed its lone exit-code-consistency finding. **S5/T17 = Model B** (bounded nearest-`.csproj` owning project + `<Project>.Tests`/`.UnitTests` transitive-ref test project + unit-only target filter): supersedes departure #6, retires #8, keeps #7, appends #9/#10. S6 **re-run** on the Model-B port and S7 (T19 dogfood) **executed (report-only, no commit)** — verdicts relayed to Mr. Das (S7 confirmed finding #1). See the Slices/Tasks tables. **S8/T21** (async/iterator coverage attribution — closes S7 dogfood finding #1: tested async/iterator methods no longer escape the exit-2 gate as `N/A`) **landed green (143 tests, 0/0 Release; `b489a08`)**. **S9** (final S6 sign-off hardening — three fixes required before merge) **COMPLETE**: **T22** multi-TFM fail-fast, **T24** coverage-key basename collision fix (`TypeName#method#basename:line`), and **T23** lambda/local-fn/async-local coverage attribution (generalizes the T21 demangler; closes finding #3's false pass) all **landed green** (155 tests, 0/0 Release, CI green). **S10 — Capstone-evaluator fixes COMPLETE (final slice):** **T25** stale-README correction (doc-only), **T27** orphan/unowned-file fail-fast (**exit 1**; folds under departure #1, no new number), and **T26** executable-member decomposition (**new departure #14** — property/indexer accessors, operators, conversions, finalizers and custom event accessors now get their own CC/coverage/CRAP rows under CLR names; constructors stay excluded; frozen key unchanged) all **landed green** (172 tests, 0/0 Release, CI green). Ledger is now **#1–#14**. **S6 re-run (#3, gpt-5.6-sol, report-only)** reconciled by Anders: 6/9 findings + all parity deltas already ratified; Mr. Das ruled three open items → **S11** — **T28** (literal `[IndexerName]` gate-evasion fix + D-T26f/#14(b) wording correction) **landed green — 176 tests, 0/0 Release**, **T29** (property-based `ProjectReference` resolution — **Mr. Das ruled O2: intrinsic ten + bounded user-defined via `Directory.Build.props`; explicit-only `$(SolutionDir)`; self-emptiness-guard conditions; fail-safe preserved; folds under #9 as D-T29a**; **landed green — 33/33 resolver, 197/197 suite, 0/0 Release, CI green** [+ Anders-nit polish: self-guard name-match / reversed-negated guard exclusion / nearer-props precedence tests]), and **Finding 5b** (top-level statements → no descriptor) accepted as a documented narrow residual.

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
| S5 | Module & test resolution finalization — **RESOLVED: Mr. Das ruled Model B** (bounded nearest-`.csproj` owning project + `<Project>.Tests`/`.UnitTests` transitive-ref test project + unit-only target-test `--filter`). Retires `.sln`-first `ModuleRootResolver` → `OwningProjectResolver` + `TestProjectResolver`; resolve-once (#7); fail-fast exit 1 on no-owner / span-multiple-projects / no-test-project. One test project ⇒ one report, so multi-report **aggregation is RETIRED** (departure #8 closed) — not implemented. Appends departures #9/#10. | S4 |
| S6 | Independent clean-room evaluation: a neutral evaluator on gpt-5.6-sol judges the delivered port against the VERBATIM original Requirements block (report-only) | S5 |
| S7 | Real-world dogfooding: run the finished crap4csharp on three real C# repos — crap4csharp, mutate4csharp, dry4csharp — producing actual CRAP reports | S6 |
| S8 — Async/iterator coverage attribution | Attribute a compiler-generated `<Method>d__N` state machine's `MoveNext` coverage back to its source async/iterator method, so **tested** async/iterator methods report REAL coverage/CRAP (not `N/A`) and participate in the exit-2 gate. Closes S7 dogfood finding #1. Task: T21. | S7 |
| S9 — Post-eval hardening (final S6 sign-off) | Three determinism/attribution fixes the final S6 evaluator reproduced, required before merge: **(T22)** multi-TFM fail-fast — >1 `coverage.cobertura.xml` after a clean coverage run ⇒ fail-fast **exit 1** (anchor `multiple coverage reports`), killing the nondeterministic ordinal-first pick on multi-targeted (`<TargetFrameworks>`) test projects; **(T23)** lambda/local-function coverage attribution — generalize the T21 `<M>d__N` demangler to also attribute `<M>b__` (lambdas on `<>c`/`<>c__DisplayClassN`), `<M>g__Local|N_M` (local functions) and `<<M>g__…|N>d__N` (async local functions) coverage back to the enclosing SOURCE method, closing the finding-#3 false-pass (a never-invoked local function reporting 100%/PASS); **(T24)** coverage-key collision — enrich the FROZEN `NormalizeTypeName#method:line` reciprocal key so overloads in a partial class split across files stop borrowing coverage. Tasks: T22–T24. | S8 |
| S10 — Capstone-evaluator fixes | Close the three findings the S6 capstone evaluator surfaced: **(T25)** stale-README correction — the async/iterator Notes bullet still claims lambda/local-function coverage "is not yet attributed", which is FALSE post-T23/departure #13; **(T27)** orphan/unowned-file fail-fast — an analyzed `.cs` with no owning `.csproj` is silently dropped from the owner set yet still analyzed → `N/A`/exit 0 (finding #2); now **exit 1** (departure #1 fail-fast family); **(T26)** executable-member decomposition — the T9 parser emitted only `MethodDeclarationSyntax`, so logic-bearing members got no metric row and escaped the gate; Mr. Das ruled **MAXIMAL scope** and it now adds descriptors + CC + coverage attribution for property/indexer accessors, operators, conversions, finalizers and custom event accessors, keyed by CLR name (**new departure #14**). Tasks: T25–T27. | S9 |
| S11 — S6 re-run (#3) fixes | Third independent clean-room S6 eval (gpt-5.6-sol, report-only), reconciled by Anders vs the #1–#14 ledger: 6/9 findings + all test-parity deltas are already-ratified departures. Three open items ruled by Mr. Das: **(T28)** literal `[IndexerName]` gate-evasion — a renamed indexer emitted parser `get_Item` while coverlet emits `get_X` → per-member `N/A` → an uncovered high-CC indexer (CC 9, true CRAP 90) FALSE-PASSED the exit-2 gate; parser now reads a literal `[IndexerName("X")]` → `get_X`/`set_X` (fallback `Item`), and the disproven "never a false pass" wording at D-T26f/#14(b) is corrected; **(T29, fast-follow)** property-based `ProjectReference` (`$(RepoRoot)`) resolution — documented now as an accepted fail-safe limitation (`TestProjectResolver` does no MSBuild evaluation → clean exit 1); **Finding 5b** (C# top-level statements → no descriptor → entry-point logic escapes the gate) accepted as a documented narrow fail-safe residual. Tasks: T28–T29. | S10 |

## Tasks (Tx)

One or more tasks per slice. Full task detail and the fail-fast delta live in `docs/decisions.md`.

| #   | Slice | Task | Status | Commit |
|-----|-------|------|--------|--------|
| T1  | S1 | Repo/solution scaffolding: `crap4csharp.sln`, `src/Crap4CSharp` (Exe, net8.0, Roslyn ref), `tests/Crap4CSharp.Tests` (xUnit + coverlet + FA 7.x), import shared `.targets`; empty build + `dotnet test` run | Done | `d3dd17f` |
| T2  | S2 | Domain types: `CliMode`, `CliArguments`, `CoverageData`(+`CoveragePercent`), `MethodDescriptor`(+`TypeName`), `MethodMetrics` | Done | `545c000` |
| T3  | S2 | `CrapScore` + `CrapScoreTests` (oracles 5.0/30.0/18.648/null) | Done | `47c1a1c` |
| T4  | S2 | `ReportFormatter` + golden `ReportFormatterTests` (InvariantCulture, `"\n"`) | Done | `f830f0e` |
| T5  | S2 | `CliArgumentsParser` + `CliArgumentsParserTests` (7 cases) | Done | `1b7833b` |
| T6  | S3 | `ICommandExecutor` + `ProcessCommandExecutor` + tests | Done | `da02028` |
| T7  | S3 | `SourceFileFinder` (`src/**/*.cs`, exclude `bin`/`obj`, ordinal sort) + tests | Done | `1b90d24` |
| T8  | S3 | `ChangedFileDetector` (git porcelain) + integration tests | Done | `c023ef8` |
| T9  | S3 | `CSharpMethodParser` + `ComplexityWalker` (augmented node set) + CC oracle tests | Done | `400d49d` |
| T10 | S3 | `CoberturaCoverageParser` (+ empty-report case) + tests; pin FQN normalization vs a real coverlet sample | Done | `87488ce` |
| T11 | S4 | `CrapAnalyzer` (exact→nearest-line lookup, per-method `TypeName`) + tests | Done | `debf9de` |
| T12 | S4 | `CoverageRunner` (`dotnet test --collect`) + `CoverageReportLocator` + tests | Done | `9041c2c` |
| T13 | S4 | `ModuleRootResolver` (nearest `.sln` → `.csproj` → root) + tests *(retired at T17/S5 → `OwningProjectResolver`, departure #9)* | Done | `e4d1329` |
| T14 | S4 | `CliApplication` + tests; **fail-fast gate** (no-coverage/empty-report → exit 1) | Done | `6e8456c` |
| T15 | S4 | `Program` entry (+ `CoverageException`) + integration tests (spawn built exe) | Done | `1f7581e` |
| T16 | S4 | README usage section + end-to-end smoke (positive + negative fail-fast) | Done | `ff422bd` |
| T17 | S5 | **Module & test resolution — Model B (Mr. Das ruled).** Retire `.sln`-first `ModuleRootResolver` → **bounded nearest-`.csproj` `OwningProjectResolver`** (never climbs above the invocation root; fixes the S6 ancestor-climb) + new **`TestProjectResolver`** (`<Project>.Tests`/`.UnitTests` whose `ProjectReference`s transitively include the owner). Resolve-once (#7); **fail-fast exit 1** on no-owner / **span multiple projects** / no-test-project. Coverage runs once on that one test project with **unit-only `--filter "type!=IntegrationTests"`** (untagged included; departure #10). One test project ⇒ one report — multi-report **aggregation RETIRED** (departure #8 closed). Appends departures **#9/#10**, supersedes **#6**. Malformed-existing `.csproj` → zero refs (FLAG-1). | Done | `d86feb6` |
| T18 | S6 | Independent evaluation on gpt-5.6-sol. Neutral sub-agent (NOT Anders/Dave/Bhaskar). Inputs = verbatim `## Requirements` block + read the delivered port + crap4java ONLY; must NOT read `docs/decisions.md`, rest of `docs/features`, `.github` playbook/agents, or any rationale. Report-only verdict to Mr. Das. | Done (report-only, ×2 runs) | - |
| T19 | S7 | Dogfood crap4csharp on crap4csharp + `../mutate4csharp` + `../dry4csharp`; capture CRAP reports; summarize crappy methods to Mr. Das. | Done (report-only; confirmed finding #1) | - |
| T20 | S4 | **Exit-code parity hardening** (closes the S6/T18 eval's exit-code-consistency finding): broaden `Program.Main` from `catch (CoverageException)` to a top-level `catch (Exception ex)` → full `ex` to stderr → exit 1 (Java `main throws Exception` parity — git/`--changed`, I/O, malformed-XML/parser throws now → 1, not a platform code); scoped `[Program.cs]` CA1031 `.editorconfig` relaxation (Mr. Das-approved; no `#pragma`/`[SuppressMessage]`; supersedes ruling A for `Program.cs` only); `CoverageException` retained; exit-matrix preserved (threshold still → **2**). +1 spawn test (`MainProcessExitsOneWhenGitFailsForChanged`). See the `docs/decisions.md` T20 register (D-T20a–d). | Done | `4e41c62` |
| T21 | S8 | **Async/iterator coverage attribution** (closes S7 dogfood finding #1). `CoberturaCoverageParser` detects a compiler-generated state-machine class (nested `<Method>d__N`, non-empty `Method` — async + `yield` iterators; **NOT** the empty-bracket `<>c`/`<>c__DisplayClassN` lambda display classes), demangles it to the enclosing type + `Method`, and attributes its `MoveNext` line coverage to key `NormalizeTypeName(enclosing)#Method:minMoveNextLine`. `CrapAnalyzer`'s exact→nearest lookup then resolves the source method with **no analyzer change**. Reshapes 2 wholesale-skip parser tests; adds async + iterator attribution tests (real `<M>d__N` sample; oracles CRAP **2.108** @ CC2/70%, **3.243** @ CC3/70%) + a `<>c__DisplayClassN` skip regression guard. Lambdas (`b__`)/local functions (`g__`) remain skipped (finding #3 **DEFERRED**). Appends departure **#11**; T21 register (D-T21a–e). | Done | `b489a08` |
| T22 | S9 | **Multi-TFM fail-fast** (final S6 finding #4). A multi-targeted test project (`<TargetFrameworks>`) makes coverlet emit one `coverage.cobertura.xml` per TFM, so the locator's ordinal-first single-pick flips the exit code 0↔2 across runs on unchanged code (S6 probe: 5×exit0, 1×exit2). Replace `CoverageReportLocator.Locate`→`LocateAll` (all matches, ordinal-sorted; `[]` for none) and have `CliApplication` fail-fast **exit 1** with anchor **`multiple coverage reports`** when >1 report is found after the clean coverage run (`CoverageRunner` already deletes `coverage/` pre-run, so >1 ⇒ genuinely multi-TFM). Same determinism ethos as the span-multiple-projects fail-fast (#9). Reshapes `CoverageReportLocatorTests` (`SelectsOrdinalFirst…` inverts to return both) + adds a `CliApplicationTests` multi-report case. | Done | `12b58ee` |
| T23 | S9 | **Lambda/local-function coverage attribution** (final S6 finding #3 — false pass). Generalize the T21 state-machine demangler to attribute compiler-generated member coverage back to the enclosing SOURCE method for lambdas (`<M>b__N` on `<>c`/`<>c__DisplayClassN`), local functions (`<M>g__Local|N_M`) and async local functions (`<<M>g__…|N>d__N`), keyed off the `<SourceMethod>…` name pattern — making coverage CONSISTENT with CC (the walker already folds lambda/local-fn complexity into the container). A never-invoked local function no longer false-passes the exit-2 gate at 100%. INVERTS the #11 `<>c__DisplayClassN` skip regression guard; adds lambda / invoked+never-invoked local-fn / async-local-fn / nested tests. Emits into T24's enriched key (coordinated). | Done | `4c3a957` |
| T24 | S9 | **Coverage-key collision** (new — C#-specific; no Java analog). The frozen reciprocal key `NormalizeTypeName#method:line` omitted the filename, so overloaded methods in a partial class split across files with overlapping line numbers collided/borrowed coverage via the nearest-line lookup. **Mr. Das ruled Option A (basename):** the key becomes `TypeName#method#basename:line` (`:line` stays LAST so `ParseTrailingLine` + the nearest-line scan are unchanged) — both producers (`ReadClassMethods` + the T21 `ReadStateMachineMoveNext`) and the consumer (`CrapAnalyzer`) thread `Path.GetFileName` identically; a missing/empty `filename` → unmatchable `Type#method#:line` → per-method `N/A` (fail-safe, never mis-attributes). `NormalizeTypeName`/frozen `TypeName` unchanged. Reshaped the FROZEN T10/T11 reciprocal-key parity suite (+2 collision tests, `Render(int)`/A.cs→100% vs `Render(string)`/B.cs→0%); Bhaskar re-verified the entire attribution suite. Updated the "coverage key = Roslyn enclosing-type FQN" locked choice; appends departure **#12** (T24 register D-T24a–g). Residual: same-basename-different-dir still collides (documented). Coordinated with T23. | Done | `ec99b35` |
| T25 | S10 | **Stale-README correction** (capstone finding: doc drift). `README.md`'s async/iterator Notes bullet still says lambda bodies and local functions "are not yet attributed" — FALSE post-T23/departure #13 (they ARE attributed: their coverage is UNIONed into the enclosing source method). Replace the stale **Known gap** sentence with the accurate statement, mirroring the async/iterator "no longer a blind spot" wording. Doc-only; no code, no test. | Done | `c65d246` |
| T26 | S10 | **Executable-member decomposition** (departure **#14** — C#-only breadth, NOT crap4java parity). Mr. Das ruled **MAXIMAL scope** (Scope B + both extras). The T9 parser (`CSharpMethodParser`) previously collected only `MethodDeclarationSyntax`, so logic-bearing members got no metric row and escaped the exit-2 gate; it now emits descriptors + CC + coverage attribution for property/indexer accessors (`get_`/`set_`/`init`→`set_`), user-defined operators (`op_*`, incl. `op_UnsignedRightShift`), conversions (`op_Implicit`/`op_Explicit`), finalizers (`~T`→`Finalize`) and custom event accessors (`add_`/`remove_`) — all keyed by **CLR name (b-1)**. EXCLUDE constructors (crap4java §8.1); SKIP bodyless (auto/abstract/field-like) members; `op_Checked*` deferred (skip). Coverage-side deletes the Rule-3 accessor skip in `CoberturaCoverageParser` (`op_*`/`Finalize` already flowed through); frozen `TypeName#method#basename:line` key + `NormalizeTypeName` UNCHANGED. See **departure #14** + the **T26 register** (D-T26a–g). | Done | `fd0fc84` |
| T27 | S10 | **Orphan/unowned-file fail-fast** (capstone finding #2). An analyzed `.cs` with no owning `.csproj` is dropped from `OwningProjectResolver.ResolveOwningProjects` yet still analyzed against the surviving project's coverage → silent `N/A`, exit 0 (misattribution). Mr. Das ruled FAIL-FAST: the resolver surfaces unowned files; `CliApplication.Execute` fails fast **exit 1** with anchor `has no owning project` when any analyzed file has no owner (departure #1 fail-fast family; subsumes the old count==0 branch). Inverts `ResolveOwningProjectsIgnoresFilesWithoutOwner` + adds a mixed-orphan CLI test. | Done | `ef006a1` |
| T28 | S11 | **Literal `[IndexerName]` gate-evasion fix** (S6 re-run #3, finding #6). The indexer case in `CSharpMethodParser` hardcoded the CLR default base name `Item` (`get_Item`/`set_Item`), but coverlet emits the `[IndexerName]` CLR spelling (`get_X`/`set_X`) → frozen reciprocal key never matches → per-member `N/A` → an uncovered high-CC renamed indexer (CC 9, true CRAP 90.0) escapes the exit-2 gate (evaluator probe confirmed). Parser now derives the base name from a **literal** `[IndexerName("X")]` attribute (syntax-only) → `get_X`/`set_X`, falling back to `Item` when absent or non-literal. Corrects the disproven **"never a false pass"** wording at D-T26f(1)/#14 residual (b): the remaining `N/A` residual is scoped to non-literal `[IndexerName]` + explicit-interface members and honestly flagged as a gate-evasion residual. Inverts the old renamed-indexer null-CRAP test. Scope = literal only (Mr. Das). | Done | `a7a36e3` |
| T29 | S11 | **Property-based `ProjectReference` resolution** (S6 re-run #3, finding #7 — fast-follow). `TestProjectResolver.DirectProjectReferences` did a raw `Path.Combine(csprojDir, Include)`, so any `$(Prop)` token in a `ProjectReference Include` → bogus path → `File.Exists` false → edge dropped → transitive closure misses → false **"No test project" exit 1**. **Mr. Das ruled O2 (intrinsic + bounded user-defined), full ten intrinsic props.** Two-stage, no public API change: (1) fixpoint-expand a property map — base = the **ten** reserved path-derived props (`MSBuildThisFileDirectory` [trailing sep, load-bearing], `MSBuildProjectDirectory` [no sep], `…FullPath`/`…File`/`…FileName`/`…Extension` `MSBuildThisFile*`+`MSBuildProject*` families; names matched `OrdinalIgnoreCase`, paths stay `Ordinal` per #3) + user-defined props from the csproj `<PropertyGroup>`s and an **all-ancestors `Directory.Build.props` walk bounded to the invocation root** (nearest-wins; csproj body overrides); (2) O1's **single-pass** regex substitution (`[GeneratedRegex]`, `CoberturaCoverageParser` idiom) on each Include. `Condition=` honored ONLY for the narrow self-emptiness guard `Condition="'$(X)'==''"` (the ubiquitous `RepoRoot` idiom); all other conditioned props skipped. **`$(SolutionDir)` explicit-def only** — `.sln`-synthesis stays a documented fail-safe residual (honors departure #9's `.sln` retirement; finding #7 named `$(RepoRoot)`/`$(MSBuildThisFileDirectory)`, not `$(SolutionDir)`). **Fail-safe preserved:** any undefined/unevaluable/conditioned/cyclic/depth-capped token → retained VERBATIM → non-existent path → dropped → exit 1; never throws (per-`Directory.Build.props` `XmlException` swallowed like the existing probe, FS faults propagate to the T20 catch-all), never guesses a match. Cycle/self-ref + depth-cap safe; per-call memo (no static state). Folds under **departure #9** as `D-T29a` (no new number); rewords D-T28c → resolved for the intrinsic + bounded user-defined subset, residuals (`.sln`-`$(SolutionDir)`, non-self-guard conditions, property functions/item-metadata/env-global props, `<Import>` chains, props above root) deferred fail-safe. **Landed green — 33/33 resolver tests (14 existing + 19 new: 16 T29 + 3 Anders-nit polish — self-guard name-match, reversed/negated guard-form exclusion, nearer-props-over-farther precedence), full suite 197/197, 0/0 Release, CI green.** | Done | - |

Critical path: T1 → T2 → T9/T10 → T11 → T14 → T15 → T16. T3/T4/T5 and T6/T7/T8/T13 parallelize early.

## Risks (Rx)

- R1: Coverage granularity — Cobertura **line** counters ≠ JaCoCo **instruction** counters; absolute
  coverage numbers differ for identical code. Algorithm/attribution preserved. (Documented.)
- R2: `coverlet.collector` prerequisite — the analyzed project's **test project** must reference it,
  and the module root must be a `.sln` so `dotnet test` runs tests. Otherwise fail-fast fires.
- R3: Cobertura FQN normalization — nested/generic type naming (`Outer.Inner`, backtick arity) and
  compiler-generated names (`get_`/`set_`, lambdas, async `MoveNext`) must be normalized/ignored to
  match parsed method FQNs. Pin against a real coverlet sample (T10). **T21/S8 update:** async/iterator
  `<Method>d__N` `MoveNext` coverage is no longer *ignored* — it is now ATTRIBUTED to the source method
  (departure #11); only lambda/local-function display names remain ignored (finding #3, deferred).
- R4: Fail-fast changes ~4 parity tests (+2 new) — the one knowing test-verification break.
- R5: FluentAssertions v8 licensing — the `[7.0.0,8.0.0)` pin + lock file must hold.
- R6: crap4csharp requires each target to have a test project referencing `coverlet.collector` and a
  resolvable module root (`.sln`), else fail-fast fires. `mutate4csharp`/`dry4csharp` may not satisfy
  this and cannot be modified in place — S7 may surface tool gaps or need target prerequisites handled
  on the copies. (Noted, not solved now.)

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

### Carry-forward watch-items (from Anders's T6 review — must be honored at the noted task)

- **W15 — `ICommandExecutor` is an exit-code-only seam; do NOT route git (T8) through it (T8/T12).**
  In crap4java the `CommandExecutor` seam has exactly one consumer — `CoverageRunner` (T12) — which
  needs only the child **exit code**. `ProcessCommandExecutor`'s async drain writes child output
  **through to `Console.Out`/`Console.Error`** (the `inheritIO()` analog) and returns `int`; it does
  **not** capture output. `ChangedFileDetector` (T8) deliberately uses its **own** process
  (Java: `redirectErrorStream(true)` + `readAllBytes()`) to **capture** git's stdout for parsing.
  Preserve that split: T8 captures stdout via its own `Process` and must read the stream to end
  **before/while** awaiting exit (Java's `waitFor()`-then-read ordering is a latent full-pipe deadlock —
  fix it in the port, don't copy it); do **not** reuse `ICommandExecutor` for git. T12 injects
  `ICommandExecutor` (cf. `CoverageRunnerTest`'s `RecordingExecutor` fake) and consumes only the exit
  code — large `dotnet test` output through the seam is safe (both streams drained → no deadlock).

### Carry-forward watch-items (from Anders's T8 review — must be honored at the noted task)

- **W16 — T14 `ChangedSrc` must call `ChangedCSharpFilesUnderSrc`, not `ChangedCSharpFiles`.**
  `ChangedFileDetector` exposes **two** public entry points; Java's `CliApplication`
  (`CHANGED_SRC -> changedJavaFilesUnderSrc`, `CliApplication.java:89`) uses the **src-filtered**
  variant. T14's `ChangedSrc` arm must route through `ChangedCSharpFilesUnderSrc` (the segment-aware
  under-`src` filter), never the unfiltered `ChangedCSharpFiles` — otherwise changed files outside
  `src/` leak into the analysis set. Reinforces W8 (files for `ChangedSrc` come from the detector, not
  `FileArgs`).

### Carry-forward watch-items (from Anders's T13 review — must be honored at the noted task)

- **W17 — T14 resolves the module root ONCE; multi-module grouping is a product call.** *(RESOLVED —
  Mr. Das ruled RESOLVE-ONCE; recorded as departure #7 in `docs/decisions.md`. **T17/Model B lineage:**
  the `ModuleRootResolver.Resolve` call below became `OwningProjectResolver.ResolveOwningProjects` +
  `TestProjectResolver.ResolveTestProject` — departure #9; resolve-once #7 unchanged.)* T14 (with
  T11/T12)
  resolves the module root by calling `ModuleRootResolver.Resolve` **once** at the discovered `.sln`
  and runs coverage **once**; it does **not** port Java's per-file
  `groupByModuleRoot`/`analyzeByModule` — there is **no module-group loop** in
  `CliApplication`/`CrapAnalyzer`. This **drops** crap4java §6's multi-module (multi-`.sln`) grouping —
  a knowing fidelity reduction (R2/R6 already assume a single resolvable `.sln`). **Test-parity:**
  crap4java's module-grouping tests **adapt to resolve-once (single resolve + single coverage run) or
  drop** at T11/T12/T14. Cross-ref departure #7, R2, R6, T11 (`CrapAnalyzer` per-method
  `TypeName`/coverage lookup) and T12 (`CoverageRunner` `dotnet test` at the module root).
- **W18 — Nonexistent-start-path normalization (below threshold today).** `Resolve` uses
  `File.Exists(full) ? parent : full`, which diverges from Java's `isDirectory ? self : parent`
  **only** for a nonexistent leaf (C# returns the leaf; Java returns its parent). Harmless in the
  current pipeline (callers pass existing discovered paths; `ContainsMarker` no-ops on a nonexistent
  dir, so the divergence surfaces only in the terminal no-marker fallback that B1 already redefines).
  **If any future task routes an unverified/nonexistent path into `Resolve`**, switch to the zero-cost
  faithful form `Directory.Exists(full) ? full : (Path.GetDirectoryName(full) ?? full)` and add a
  nonexistent-leaf test (no current test exercises this).

### Carry-forward watch-items (from Anders's T9 review — must be honored at the noted task)

- **W-T9a — T10 `NormalizeTypeName` must yield the frozen `TypeName` form.** `CoberturaCoverageParser`
  must normalize the coverlet `class name=` attribute to the **frozen reciprocal form** recorded in
  `docs/decisions.md` ("Frozen reciprocal contract — `TypeName` canonical form"): coverlet nested
  separators (`/` **or** `+`) → `.`, **keep** the per-level backtick arity (`` `1 ``, `` `2 ``),
  bare-ify the global namespace. This is the exact string `CSharpMethodParser.TypeNameOf` (T9) emits and
  is already pinned by `CSharpMethodParserTests`. **Pin `NormalizeTypeName` against a real coverlet
  sample** (R3) — do not hand-fabricate the expected class attribute. Load-bearing: any mismatch drops
  the method to `N/A`.
- **W-T9b — T10 must skip compiler-generated class & method names.** Coverlet reports lambdas, local
  functions, iterators/async state machines, property accessors and constructors under synthesized
  names. `CoberturaCoverageParser` must **ignore** entries whose class **or** method name is
  compiler-generated — at minimum: any name containing `<...>` (display/state-machine classes and
  `<Name>b__n` lambda methods), `get_`/`set_` accessors, `.ctor`/`.cctor`, `MoveNext`, and `b__`
  helpers — so only human-authored members (the ones `CSharpMethodParser` collects) are matched. Mirrors
  R3's "generated names must be normalized/ignored." Pin against the same real coverlet sample as W-T9a.
- **W-T9c — Coverage-map key shape (recommended) for T11 lookup alignment.** Recommended map key is
  `NormalizeTypeName(class)#method@name:line` (type FQN in the frozen form + `#` + method name + line),
  so T11's **exact** match keys off `TypeName#Name:line`. T11's **nearest-line** fallback prefix must
  stay **method-scoped** — `TypeName#Name:` — so a fallback never crosses into a different method or
  overload. Keep the separators (`#`, `@`, `:`) out of any identifier that can legally contain them
  (none can), so the key stays unambiguous. Reconfirm the exact key shape when T10/T11 land; the binding
  invariant is that emit (T9), produce (T10) and consume (T11) share the frozen `TypeName` form.

### Carry-forward watch-items (from Anders's T10 review — must be honored at the noted task)

- **W-T10a — async/iterator coverage-attribution gap (T11 visibility).** Coverlet attributes an
  `async`/iterator body's coverage to the synthetic state machine (`Outer+<M>d__N`/`MoveNext`), which
  T10 skips (W-T9b/W-T10c). T9 emits the real `M`, so such methods have no matching coverage entry and
  resolve to per-method `N/A` in T11 — consistent with departure #1 (**not** a new departure).
  Async-heavy targets will show more `N/A` than the Java tool; surfaced for Mr. Das's visibility.
  Recorded in `docs/decisions.md` (T10 register).
- **W-T10b — accessor-prefix skip false-positive (T11).** The rule-3 `get_`/`set_`/`add_`/`remove_`
  prefix skip has a negligible false-positive on a real method literally named `get_Foo`
  (underscored) → `N/A`. Sanctioned by W-T9b; logged.
- **W-T10c — synthetic `MoveNext` skipped via its containing class (ACCEPTED).** T10 does not
  blanket-skip `MoveNext` by name; the synthetic one is caught by its angle-bracket state-machine class
  (rule 1) while a user-authored `IEnumerator.MoveNext` on a real class is kept and attributed. Ratified
  as the more-faithful reading of W-T9b (not a departure); pinned both ways by test 9
  (`SkipsSyntheticStateMachineClassButKeepsRealMoveNext`).

### Open decision for Mr. Das (from T5, design-lane)

- **D-T5 — CA1062 `ArgumentNullException.ThrowIfNull` idiom.** *(RESOLVED — Mr. Das ruled
  BELOW-THRESHOLD.)* The analyzer-forced null guards are a trivial idiomatic adaptation, **not** a
  logged departure: **no** standing-policy line is added to `docs/decisions.md`. Dave continues writing
  the CA1062-required guards exactly as before (code unchanged); Bhaskar/Anders treat them as
  non-noteworthy going forward.
- **D-T9 — Parser plumbing / signature (non-1:1).** *(RESOLVED — Mr. Das ruled **Option A**: drop both
  plumbing methods + their 2 tests; adopt `Parse(string source)`. Shipped in T9; Anders-reviewed 🟢.)*
  Drop
  `JavaMethodParser.sourcePath`/`sourceUri` and their 2 tests (`buildsSourcePathAndUriFromClassNames`,
  `acceptsClassNamesWithJavaSuffix`) as javac file-URI plumbing with no Roslyn referent, and adopt
  `Parse(string source)` (drops the vestigial `className`). Anders recommends **Option A (drop both)**;
  the 7 remaining Java parser tests port as faithful behavioral counterparts. **Unblocked T9** (parser
  signature + test set followed the ruling; 17 tests = 7 faithful ports + 10 new, 56/56 green).
  Design-lane FYIs (Anders, vetoable): collection scope =
  methods only; expression-bodied methods included; nested-type class-name format is a T10 pin.
- **D-T8 — Integration-test categorization / fast-loop scope (agentic-loop policy).** *(RESOLVED —
  Mr. Das ruled NO TRAITS.)* **Verdict:** crap4csharp adds **no** `[Trait]` attributes to its own
  tests; **all** crap4csharp tests are treated as unit tests and are run by the tight agentic dev loop
  **by default** — including the T6/T8 process/git integration-style tests
  (`ProcessCommandExecutorTests`, `ChangedFileDetectorTests`), which run **as-is**. **No** `[Trait]` is
  added and **no** `.github/skills/build-test.md` edit is needed; the policy is documented in
  `README.md` and recorded in `docs/decisions.md` (Ratified conventions). The historical context below
  (the doc/code gap and the two candidate resolutions) is retained for the record but is **superseded
  by this NO-TRAITS ruling**. `.github/skills/build-test.md` promises
  the fast loop excludes integration tests via `--filter "Category!=Integration"`, but **no** test
  carries that trait (0 grep matches), so the fast loop currently spawns real `git`/processes
  (`ChangedFileDetectorTests`, `ProcessCommandExecutorTests`) — doc and code disagree. Two coherent
  resolutions: **(a)** keep plain `[Fact]` and reword `build-test.md` to drop the "tests are tagged"
  claim (collapses the two-tier loop — `build-test` and `build-test-full` then run the same set);
  **(b)** add `[Trait("Category", "Integration")]` to `ProcessCommandExecutorTests` **and**
  `ChangedFileDetectorTests` (and to every future process/CLI/`git`/`dotnet`-spawning test —
  T12/T14/T15/T16), making the fast loop genuinely hermetic and the doc accurate as written. **Anders
  recommends (b)** — it preserves the deliberate fast/full split (else `build-test-full` is redundant),
  correctly categorizes tests that spawn external processes, and pays off as S4's integration tests
  land; T6's plain-`[Fact]` `ProcessCommandExecutorTests` is then a gap to fix retroactively, not a
  convention to propagate. This sets a **testing convention** governing future tasks and edits an
  agentic-loop skill file, so it is Mr. Das's call, not Anders's design lane. **If (b):** Dave adds a
  class-level `[Trait("Category", "Integration")]` to both test classes (no new `using` — `Xunit` is a
  global using), and the convention is recorded in `docs/decisions.md` (Ratified conventions); no
  `build-test.md` edit. **If (a):** replace `build-test.md`'s final paragraph with the no-tagging
  reality (forward-looking: tag integration tests as they land). **Resolution (Mr. Das):** **NO
  TRAITS** — closest to **(a)** (no tags added), but `build-test.md` is left untouched and the
  doc/reality reconciliation lives in `README.md`; **D-T8 is CLOSED**.

- **D-T13 — Module-root walk unbounded (B1) — ratification.** *(RESOLVED — Mr. Das approved.
  **SUPERSEDED at T17/S5:** departure #6 is superseded by departure #9 (Model B) — `ModuleRootResolver`
  is retired/renamed to the **bounded** `OwningProjectResolver`; `.sln` is no longer a marker. Body
  below retained for audit.)*
  `ModuleRootResolver` climbs **unbounded** to the filesystem root and falls back to the **start
  directory** when no `.sln`/`.csproj` marker is found, vs crap4java's workspace-bounded walk /
  `workspaceRoot` fallback. Recorded as **departure #6** in `docs/decisions.md`; T13 shipped as-is
  (`e4d1329`), no rework. Below the behavioral bar for realistic C# layouts.

- **D-T10a — behavioral XXE test vs. Java's white-box factory-flag test.** *(RESOLVED — ruled: port
  behaviorally; N10 landed.)* Java's `configuresSecureFactoryFeatures` inspected
  `DocumentBuilderFactory` flags; the C# port asserts the **observable** guarantee instead — a canary
  referenced via an external entity is never resolved (`DoesNotResolveExternalEntities`). Mechanism:
  `XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }`. Security is
  identical (resolver-null blocks all external fetches); `Ignore` keeps the DOCTYPE-tolerance test
  faithful. **Not a new behavioral departure** — see the T10 register in `docs/decisions.md`.
- **D-T10b — `NormalizeTypeName` keeps the backtick arity.** *(RESOLVED — keep arity.)* The T10
  normalizer is the mechanical inverse of T9's `TypeNameOf`: nested `/`+`+` → `.`, per-level `` `N ``
  arity **kept**, global namespace bare. Discharged by test 11 (D-table) + test 10 (real coverlet
  forms). The load-bearing frozen-reciprocal property is preserved byte-for-byte.

### S5/S6/S7 finale — design constraints (design-lane)

- **D-S5 (design — deferred to when S5 runs; options-only now — RESOLVED at T17):** *(Mr. Das ruled
  **option (b) — Model B**: bounded nearest-`.csproj` owning project + `<Project>.Tests`/`.UnitTests`
  transitive-ref test project + unit-only target-test filter — shipped as departures **#9/#10**; see
  `docs/decisions.md`. Options text below retained for audit; `ModuleRootResolver` →
  `OwningProjectResolver` + `TestProjectResolver`.)* S5 finalizes the
  file→module→test **resolution model** (`.sln` vs nearest-`.csproj` vs hybrid) **plus test-project
  resolution plus multi-report coverage aggregation** before the port is "done-done" and before the
  real-world slices
  (S6 eval / S7 dogfood). **Scope trim (post-W17):** module *grouping* is **no longer part of S5** —
  the resolve-once ruling (departure #7 in `docs/decisions.md`; W17 RESOLVED) removes crap4java §6
  multi-module grouping entirely, so S5 decides only how a target file resolves to its **single**
  module root and its test project. **Scope add (post-T12):** multi-report coverage **aggregation** is
  now **in S5** — the single-pick ruling (departure #8 in `docs/decisions.md`) accepts T12's
  ordinal-first single-pick `CoverageReportLocator` as the interim behavior, so S5/T17 also owns
  replacing it with a union across the per-test-project `coverage.cobertura.xml` reports so
  multi-test-project solutions stop under-reporting (grouping stays out per #7; aggregation comes in
  per #8). The port keeps driving on the **locked `.sln`-first model** (decisions.md
  "Module root" locked choice + departure #6; T13 shipped `e4d1329`) through S3→S4; **S5 is the only
  place the model is revisited/decided/reworked — do NOT change T13 or the resolution model before
  S5.** Reference for writing the options faithfully: `../mutate4csharp` README §"Module & Test
  Resolution" (READ-ONLY sibling — do not write there).
  - **Options Anders presents / Mr. Das rules on WHEN THE SLICE RUNS (not now):** **(a)** keep the
    locked `.sln`-first **resolution** model — T13 as shipped (crap4java §6 multi-module *grouping* is
    out of scope per departure #7); **(b)** adopt mutate4csharp's
    model — owning project = nearest `.csproj` above the target `.cs` (file name w/o extension =
    `<Project>`); test project = `<Project>.Tests.csproj` **or** `<Project>.UnitTests.csproj` whose
    project references (transitively) include `<Project>.csproj`, and only that project's tests run
    (fail fast if no owning `.csproj`/test project is found); **(c)** hybrid — `.sln`-based module
    resolution but borrow the `.Tests`/`.UnitTests` convention to select and run the right test project
    (multi-module *grouping* remains out of scope per departure #7). Then implement the chosen model:
    rework `ModuleRootResolver` (T13); shape T12/T14.
  - **Locked constraint on this slice (Mr. Das already ruled):** do **NOT** adopt mutate4csharp's
    unit-only `[Trait("type", …)]` filtering (which keeps `UnitTests`/`Unit`/untagged and excludes
    `IntegrationTests`) — crap4csharp keeps running **ALL** module tests (crap4java parity). This is
    **distinct** from the separate **D-T8** agentic-loop fast-loop tagging question (now RESOLVED —
    NO TRAITS), which
    governs **our own** dev-loop test suite (`[Trait("Category", "Integration")]` on process/CLI
    tests), **not** how the tool runs a *target's* tests.
  - **Engineering watch-item (honor during S4):** keep **T12 (`CoverageRunner`)** and **T14
    (fail-fast gate)** cleanly decoupled **behind the existing `ModuleRootResolver` abstraction**, so
    the later S5 model swap is cheap — localized to the resolver seam, not spread through T12/T14.
- **Label-stability note:** the **D-S6** and **R6** labels below are **retained unchanged** for
  reference stability (Mr. Das still refers to "D-S6/R6"), even though the dogfood slice/task
  renumbered **S6→S7 / T18→T19**; only their internal slice/task references are updated.
- **D-S6 (design — resolved):** Per guardrail #2, `../mutate4csharp` and `../dry4csharp` are READ-ONLY
  sibling repos; `crap4csharp` itself is write-scoped to this repo only. Because S7 runs
  `dotnet test --collect`, which writes build/coverage artifacts (`bin`/`obj`/`TestResults`), S7 MUST
  operate on COPIES of each target in a scratch/temp workspace OUTSIDE all source repos (e.g., under
  `%TEMP%`), never writing into the sibling repos or into crap4csharp's own tree. Dogfooding
  crap4csharp-on-crap4csharp likewise runs against a copy to avoid polluting the working tree. This
  constraint governs T19 execution.
- Also note the ordering: S7 runs only after everything through S6 is done and pushed (S7 depends on S6,
  transitively S4/S5).

# Design decisions — crap4csharp

Locked decisions for the C# port of `crap4java`. Source of truth alongside `docs/features/`.

## Product intent

- Analyze **C# projects** — the C# member of the `crap4*` family (`crap4clj` → `crap4java` →
  `crap4csharp`). Ecosystem: Roslyn (parse + complexity), Coverlet → Cobertura (coverage),
  `dotnet test` / MSBuild (driver).
- **Faithful 1:1 port** of `crap4java`: class decomposition, CRAP formula, CLI contract, report
  format, and exit codes preserved. Only the ecosystem adapters change.
- **Test fidelity:** every `crap4java` test gets a faithful C# counterpart with identical
  verification — except where an approved deliberate departure changes it.

## CRAP formula

Preserved verbatim from `crap4java`. For a method with cyclomatic complexity `CC` and coverage
fraction `coverage ∈ [0, 1]`:

- **Formula:** `CRAP = CC² · (1 − coverage)³ + CC`
- **Threshold:** a method is flagged **crappy** when `CRAP > 8.0`.

The formula and threshold are unchanged from `crap4java`; only `CC` is computed with the augmented
node set below (an approved departure), so absolute CRAP scores are not numerically comparable to
`crap4java` on code using the modern constructs.

## Locked choices

| Area | Decision | Notes |
|---|---|---|
| Analysis target | C# projects | Roslyn + Coverlet/Cobertura + `dotnet test` |
| Namespace | `Microsoft.Crap4CSharp` | `AssemblyName`/`RootNamespace` = `Microsoft.<Project>` |
| TFM | `net8.0` | SDKs 8/9/10 present; .NET 10 offers no conversion benefit |
| Parser | Roslyn (`Microsoft.CodeAnalysis.CSharp`) | analog of the JDK compiler tree API |
| Coverage | Coverlet → **Cobertura** (line counters) | JaCoCo `INSTRUCTION` has no exact analog — absolute numbers differ, algorithm identical |
| Test framework | **xUnit** | + `Microsoft.NET.Test.Sdk`, `coverlet.collector` |
| Assertions | **FluentAssertions 7.x**, pinned `[7.0.0,8.0.0)` | v8+ is commercial (Xceed); lock file enforces the pin |
| Coverage key | `TypeName#method#basename:line` — Roslyn enclosing-type **FQN** + method + source-file **basename** (T24, departure #12); `:line` stays LAST | C# allows many types per file **and** partial classes split one type across files, so overloads at overlapping min-lines collided; the FQN keys Cobertura, the basename segregates the per-file entries |
| Module root | nearest **`.sln`** (fallback `.csproj` → project root) | so `dotnet test` actually runs tests — **SUPERSEDED at T17 by departure #9 (Model B): the owning unit is the nearest `.csproj` (bounded); `.sln` is no longer a marker** |

## Ratified conventions (Mr. Das)

- **C1 — Single production assembly.** All production code lives in one assembly, `src/Crap4CSharp`
  (mirrors `crap4java`'s single module). The pure core must not depend on the ecosystem adapters
  (Roslyn/Cobertura/process/git); this "core has no ecosystem dependencies" rule is enforced by
  **review and folder/namespace discipline**, not a compile-time project boundary. No
  Core/Adapters/Exe split unless Mr. Das later rules otherwise.
- **C2 — No underscores in member names (CA1707).** Analyzers run `latest-all` with
  warnings-as-errors in Release, so CA1707 is fatal on **all** member names — **including xUnit test
  methods** (e.g. `HarnessDiscoversAndRuns`, not `Method_State_Expected`). Parity tests preserve
  `crap4java`'s **behavior, not its names**; PascalCase-renaming a ported test is not a fidelity
  break.
  - **C2 clarification (T7 review; Bhaskar-verified build behavior).** CA1707 targets only
    **externally-visible** identifiers (public/protected) — which includes public xUnit test methods —
    so **private fields are outside its scope and build clean with or without a leading underscore**.
    The `.editorconfig` moreover *configures* `_camelCase` for private fields
    (`dotnet_naming_rule.private_members_with_underscore`) and deliberately disables StyleCop `SA1309`
    (the anti-underscore rule), so the **intended, build-safe form for a private field is `_camelCase`**
    (e.g. T9 `ComplexityWalker`'s complexity counter → `_complexity`). That naming rule is surfaced by
    IDE1006, which is not enforced during `dotnet build` (a known Roslyn limitation), so it is never a
    Release error either way — but `_camelCase` is both the configured style and future-proof. Net: C2's
    "no underscores … on **all** member names" governs the **CA1707 surface** (externally-visible
    members + public test methods), **not** private fields; do not avoid a private field for fear of a
    CA1707 conflict.

- **D-T8 — No `[Trait]` categorization (RESOLVED — Mr. Das ruled NO TRAITS).** crap4csharp adds **no**
  `[Trait]` attributes to its own tests; **all** crap4csharp tests are treated as unit tests, so the
  tight agentic dev loop runs them **by default** (the T6/T8 process/git integration-style tests run
  as-is). Full policy detail — "unit unless explicitly marked integration" + full-suite-needs-approval
  — lives in `README.md`. Closes the open **D-T8** decision; no `.github/skills/build-test.md` edit.

## Frozen reciprocal contract — `TypeName` canonical form (T9 ⇄ T10 ⇄ T11)

**FROZEN (T9 review, Anders).** The per-method coverage-lookup key `MethodDescriptor.TypeName` (the
"Coverage key" locked choice, elaborated) has exactly **one** canonical spelling that **both**
producers must emit byte-for-byte, so T11's exact-match keys align. This is **load-bearing**: any drift
on either side silently breaks coverage attribution (affected methods fall through to `N/A`).

- **Form:** dotted-namespace FQN of the enclosing type, then `.`, then the containing-type chain joined
  by `.` (outermost → innermost). **Each** type level that declares N type parameters carries a
  per-level backtick arity suffix `` `N `` (arity 0 ⇒ no suffix). A type in the **global namespace**
  yields the **bare** type chain (no leading `.`, no namespace prefix).
- **Canonical examples:** `Demo.Sample`; `Demo.Outer.Inner`; ``Demo.Container`1``;
  ``Demo.Outer`1.Inner`2``; bare `Sample` (global namespace).
- **Producer 1 — `CSharpMethodParser.TypeNameOf` (T9, emit):** builds it from the syntax tree
  (`BaseNamespaceDeclarationSyntax` ancestor chain + `TypeDeclarationSyntax` ancestor chain, per level
  `TypeParameterList.Parameters.Count`). **Pinned** by `CSharpMethodParserTests` — all five forms above.
- **Producer 2 — `CoberturaCoverageParser.NormalizeTypeName` (T10, produce):** must normalize the
  coverlet `class name=` attribute to this **same** form — nested separators (`/` or `+`) → `.`,
  **keep** the backtick arity, bare-ify the global namespace — and must be **pinned against a real
  coverlet sample** (R3). See watch-item **W-T9a**.
- **Consumer — `CrapAnalyzer` (T11):** builds coverage-map / method-lookup keys from this form. Do not
  change one producer without the other. **T11 discharged (Anders T11 review, 🟢)** — the consumer
  matches these keys byte-for-byte; pinned end-to-end by
  `ResolvesCoverageThroughFrozenReciprocalKeyEndToEnd`. See the **T11 register** below.
- **T10 discharged (Anders T10 review, 🟢).** Producer 2 now emits this form byte-for-byte:
  `NormalizeTypeName` = `rawClassName.Replace('/', '.').Replace('+', '.')` (backtick arity **kept**),
  pinned by `NormalizeTypeNameProducesFrozenReciprocalForm` (test 11, D-table) and by the real
  coverlet strings in `MatchesRealCoverletSampleClassNames` (test 10). See the **T10 register** below.

## T10 register — `CoberturaCoverageParser` (S3 close-out; Anders T10 review, 🟢)

Resolved decisions, R3 closure, and watch-items from the final S3 task. `CoberturaCoverageParser`
is a faithful port of `JacocoCoverageParser` re-hosted on Coverlet→Cobertura per-method `<line>`
counters (departure #4). **No new behavioral departure beyond the already-approved set.**

- **D-T10a — behavioral XXE test replaces Java's white-box flag test (RESOLVED).** Java's
  `configuresSecureFactoryFeatures` inspected `DocumentBuilderFactory` feature flags; the C# port has
  no equivalent introspection seam, so the faithful counterpart is the **behavioral** test
  `DoesNotResolveExternalEntities` (N10): a canary file referenced through an internal-subset external
  entity via `file://` must never be read into the parse. Mechanism —
  `XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }` — gives the
  **observably identical** guarantee (no external DTD/entity/XInclude is ever fetched). `Ignore` (not
  `Prohibit`) is deliberate: `XmlResolver = null` already blocks every external fetch, and `Ignore`
  keeps the DOCTYPE-tolerance test (`ParsesXmlWithDoctypeWithoutRequiringLocalDtdFile`) faithful while
  a malicious `&xxe;` reference still fails safely as an undeclared entity → wrapped
  `InvalidOperationException`, no file read/network/substitution. This is a **security/fidelity
  mechanism swap, NOT a new behavioral departure** — no departure number is assigned.

- **D-T10b — `NormalizeTypeName` keeps the backtick generic arity (RESOLVED).** The normalizer is the
  mechanical inverse of T9's `CSharpMethodParser.TypeNameOf`: nested separators (`/` or `+`) → `.`,
  per-level `` `N `` arity **kept untouched**, dotted namespace already emitted by coverlet, global
  namespace already bare. Discharged by `NormalizeTypeNameProducesFrozenReciprocalForm` (test 11, the
  full D-table) and by the real-sample forms in `MatchesRealCoverletSampleClassNames` (test 10:
  ``Sample.Outer`1/Inner`2`` → ``Sample.Outer`1.Inner`2``, ``Sample.Container`1`` kept).

- **R3 — real-coverlet normalization pin CLOSED for T10.** Final `NormalizeTypeName` rule:
  `rawClassName.Replace('/', '.').Replace('+', '.')` — nested `/` **and** `+` → `.`, keep per-level
  `` `N `` arity, global-namespace chain left bare. Pinned against a **real** coverlet Cobertura sample
  in test 10 (verbatim `class @name`/`method @name`/`<line>` strings), closing R3's "pin against a real
  coverlet sample" for the plain-FQN, nested, generic and compiler-generated forms. **Empirical note:**
  real coverlet emits nested-type separators as `/` (not `+`); `NormalizeTypeName` handles both, so the
  `+` branch is the defensive path for older tooling / the alternate spelling in the frozen contract.

**Watch-items (T10 review — visibility for Mr. Das; none blocks T10):**

- **W-T10a — async/iterator coverage-attribution gap (instance of R3).** Coverlet attributes an
  `async`/iterator method's body coverage to the synthetic state machine (`Outer+<M>d__N` / its
  `MoveNext`), which T10 skips (W-T9b/W-T10c). Since T9 emits the real `M`, such methods have no
  matching coverage entry and resolve to per-method `N/A` in T11 — consistent with departure #1
  ("per-method `N/A` unchanged"), **not a new departure**. Visibility: async-heavy targets will show
  more `N/A` than the Java tool would.
- **W-T10b — `get_`/`set_`/`add_`/`remove_` prefix-skip false-positive (rule 3).** A user method
  literally named `get_Foo` (with an underscore) would be wrongly skipped → `N/A`. Negligible
  probability; sanctioned by W-T9b. Logged.
- **W-T10c — synthetic-`MoveNext` skip via containing class (ACCEPTED, ratified).** The skip predicate
  does **not** blanket-skip `MoveNext` by name; a synthetic `MoveNext` is caught by its angle-bracket
  state-machine class (rule 1), while a user-authored `IEnumerator.MoveNext` on a real class is kept and
  attributed (T9 emits it). This is the more-faithful reading of W-T9b's "skip *synthesized* members,"
  ratified as the accepted choice (not a departure); pinned both ways by
  `SkipsSyntheticStateMachineClassButKeepsRealMoveNext` (test 9).

## T11 register — `CrapAnalyzer` (S4 close-out; Anders T11 review, 🟢)

Resolved decisions and watch-items from the final analysis-composition task. `CrapAnalyzer` is a
faithful 1:1 port of `crap4java`'s `CrapAnalyzer` (`analyze`/`lookupCoverage`/`exactCoverage`/
`nearestCoverage`/`parseTrailingLine`) — the flat composition layer (Slice S4) that reads the changed
files itself and drives both parsers (T9 `CSharpMethodParser`, T10 `CoberturaCoverageParser`) itself,
exactly as the Java original does. **No new behavioral departure beyond the already-approved set #1–#7.**

- **Frozen reciprocal contract — CONSUMER end discharged (T11; key EXTENDED at T24).** `CrapAnalyzer`
  builds its exact/nearest lookup keys as
  `typeName + "#" + methodName + "#" + sourceFile + ":" + line` from `MethodDescriptor.TypeName`
  (T9) and `Path.GetFileName(file)` (T24, departure #12), and matches them byte-for-byte against T10's
  emitted map keys — the `#sourceFile` segment segregates per-file overloads of a partial class, and the
  trailing `":line"` is load-bearing (kept LAST so `ParseTrailingLine`'s last-`:` split and the
  nearest-line scan are unchanged; the earlier `":"` still stops `alpha` matching `alphaBeta`). Any drift
  on either producer silently collapses a method's coverage to `N/A`. Line formatting uses
  `InvariantCulture` (departure #3) so keys round-trip. Pinned end-to-end by
  `ResolvesCoverageThroughFrozenReciprocalKeyEndToEnd` (a nested generic ``Demo.Outer`1.Inner`2``
  resolving to a real 100%, not `N/A`) and by `SplitPartialClassOverloadsResolveToOwnFileCoverage`
  (T24). The T9⇄T10⇄T11 triad is now closed on all three ends.

- **D-T11a — `classNameFromSource` and the dead `projectRoot` param DROPPED (RESOLVED).** Java derived
  one `className` per file (`package` regex + filename); C# instead carries the per-method
  `MethodDescriptor.TypeName` (enclosing-type FQN, D-T9 / "coverage key = enclosing-type FQN"), so
  `classNameFromSource` has no C# analog and is dropped — its "no namespace ⇒ bare name" behaviour is
  already pinned by `CSharpMethodParserTests`' global-namespace form. Java's unused `projectRoot` param
  is dropped with it (behaviour-preserving). Java's `usesSimpleClassNameWhenSourceHasNoPackage` test has
  no C# counterpart (behaviour migrated to T9). **Not a new departure.**

- **D-T11b — nearest-line is the COMMON path in C# (visibility, not a change).** JaCoCo's
  `<method line>` equals the declaration line so Java's exact-match usually hits; T10 keys on
  `minChildLine` (first executable line, ≠ `StartLine`), so C# exact-match usually MISSES and the
  `exact→nearest→N/A` fallback resolves. The algorithm is faithful; only the branch hit-rate shifts.

**Watch-items (T11 review — visibility for Mr. Das; none blocks T11):**

- **W-T11a — tie-break relies on document-order enumeration (LOAD-BEARING invariant, not a departure).**
  `nearestCoverage`'s strict `<` makes the first entry in map-enumeration order win a distance tie. The
  production map is a `Dictionary<string, CoverageData>(StringComparer.Ordinal)` built by T10 in
  document order and **never mutated after build**, so .NET's insertion-order enumeration makes the
  tie-break **deterministic (document-order-first)** — the direct analog of Java's `LinkedHashMap`-pinned
  test, and strictly more deterministic than Java production's `HashMap`. Do **not** switch the map type,
  re-sort it, or remove keys, or this determinism breaks silently. Pinned by
  `NearestCoverageKeepsFirstEntryWhenDistancesTie`.
- **W-T11b — `ParseTrailingLine` (`int.TryParse` + `NumberStyles.Integer`) tolerates surrounding
  whitespace where Java `Integer.parseInt` would not — UNREACHABLE (T10 keys are pure ASCII digits).
  Negligible.**
- **W-T11c — a directory path in `changedFiles` is skipped by `File.Exists` (Java would
  `readString`-throw) — UNREACHABLE (dirs are expanded upstream, W12). Benign.**
- **W-T10a inheritance — async/iterator methods resolve to per-method `N/A` here (their coverage is
  attributed to synthetic state machines skipped by T9/T10). Consistent with departure #1; not a new
  departure. Expect more `N/A` rows on async-heavy targets.**

## T12 register — `CoverageRunner` + `CoverageReportLocator` (S4; Anders T12 review, 🟢)

Resolved decisions and load-bearing invariants from the coverage generate/locate task. `CoverageRunner`
is a faithful 1:1 port of `crap4java`'s `CoverageRunner` (delete-stale → run-once → throw-on-nonzero),
re-hosted on the `dotnet test --collect` pipeline; `CoverageReportLocator` is a C#-specific addition
with no Java counterpart (JaCoCo's report path was a fixed constant, so Java needed no locator).
**No new behavioral departure beyond the already-approved set #1–#8.**

- **D-T12b — the pre-resolved-`projectRoot` decoupling seam is LOAD-BEARING (do not break at S5/T17).**
  Both types take a **pre-resolved** `projectRoot` string and **never** call `ModuleRootResolver`; T14
  resolves the one module root once (departure #7) and injects the same string into
  `GenerateCoverage(root)` and `Locate(root)`. This is the seam the S5/T17 resolution-model swap
  (`.sln` vs `.csproj` vs hybrid) turns on: that swap must change **only** `ModuleRootResolver` —
  `CoverageRunner`/`CoverageReportLocator` stay untouched because they never learn *how* the root was
  found. Do not reintroduce a resolver call into either type.
  - **T17 amendment (Model B; ratified by Mr. Das, FLAG-9).** The S5/T17 swap did **not** stay confined
    to `ModuleRootResolver`: that type is retired/renamed to `OwningProjectResolver` and a new
    `TestProjectResolver` is added (departure #9), and `CoverageRunner.GenerateCoverage` gains a
    `testProject` token + explicit `coverageBaseDirectory` parameter (departures #9/#10). Only
    `CoverageReportLocator` stayed code-identical. Revised seam promise: **resolution + the runner's
    `dotnet test` target change; the locator and the `ResultsDirectoryName` reciprocal contract (D-T12e)
    are preserved.** The decoupling INTENT survives — coverage/locate still take a pre-resolved
    `_projectRoot` and never resolve on their own — but the "swap touches only `ModuleRootResolver`"
    letter is void.

- **D-T12e — `ResultsDirectoryName` is a frozen reciprocal contract (single source of truth).** The
  results-directory name `"coverage"` is declared **once** as `public const
  CoverageRunner.ResultsDirectoryName` and referenced by **both** the writer (`CoverageRunner`, which
  passes it to `--results-directory`) and the reader (`CoverageReportLocator`, which `Path.Combine`s it
  under `projectRoot`). Same lesson as the frozen `TypeName` contract: writer and reader must agree
  byte-for-byte or the locator silently finds nothing (a false fail-fast). Do not inline the literal
  `"coverage"` on either side and do not split it into two constants.

- **D-T12f — ordinal-first single-pick is a LOAD-BEARING determinism invariant.** **[SUPERSEDED at T22:
  `Locate` is replaced by `LocateAll` (surfaces ALL matching reports ordinal-sorted, no `FirstOrDefault`
  single-pick); >1 report now FAIL-FASTS (`multiple coverage reports`, exit 1 — our convention) instead
  of being silently single-picked. The timing-free / ordinal-sorted / pre-run-`coverage/`-delete facts
  below are retained as the list-ordering rationale for `LocateAll`. Body retained for audit.]**
  When more than one
  `coverage.cobertura.xml` exists, `Locate` returns the **ordinal-first** full path
  (`OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault()`) — OS-independent and timing-free (no
  mtime/`LastWriteTime`; `CoverageRunner` deletes `coverage/` before every run, so every found file is
  fresh). Do not switch to timestamp/most-recent selection (reintroduces timing sensitivity) or a
  non-ordinal sort. The **multi-test-project fidelity reduction** this single-pick exposes (sibling
  test-projects' coverage silently dropped → per-method `N/A`) and the **single-token `--collect`
  spelling** were both ruled by Mr. Das to the contract's locked defaults — recorded as **departure
  #8**; multi-report aggregation is deferred to S5/T17 and revisited if S7 dogfooding hits a
  multi-test-project target.

- **No new departure from D-T12a/c/d.** The generate/locate split (D-T12a) preserves Java's
  `CoverageRunner` shape and keeps the locator independently testable; resolve-once/run-once (D-T12c) is
  departure #7; the coverage-command failure throws with the byte-identical message
  `"Coverage command failed with exit N"` (D-T12d) — originally the `IllegalStateException` →
  `InvalidOperationException` analog (C1, as in `CoberturaCoverageParser`), but **retyped at T15
  (ruling A) to the domain `CoverageException`** (message unchanged) so `Program.Main` could catch it
  specifically (D-T15b) — **broadened at T20** to a top-level `catch (Exception)`; `CoverageException`
  is retained and now caught by the broad catch, same anchor (see the T20 register). The T12/T14 fail-fast boundary is unchanged: T12
  **throws** on command failure (faithful) and **returns `null`** when the report is absent (the new
  locate signal); every exit-code decision (departure #1) stays in T14.

## T14 register — `CliApplication` + fail-fast gate (S4 close-out; Anders T14 review, 🟢)

Resolved decisions and load-bearing invariants from the composition/orchestration layer.
`CliApplication` is a faithful 1:1 port of `crap4java`'s `CliApplication`/`Main`
(`execute`/`parseArguments`/`filesForMode`/`explicitFiles`/`maxCrap`/`thresholdExceeded`) with the
`moduleRootFor` + `groupByModuleRoot`/`analyzeByModule` module-loop removed (departure #7). It composes
already-shipped types only — no resolution/coverage/parse internals of its own — so the S5/T17
resolution-model swap touches **only** `ModuleRootResolver` (D-T12b) [**superseded at T17: the swap
reshaped `CoverageRunner` and split resolution into `OwningProjectResolver` + `TestProjectResolver`
(departures #9/#10) — see the amended D-T12b and the T17 register**]. **No new behavioral departure
beyond the already-approved set #1–#8.**

- **D-T14a — the exit-code table is the canonical CLI contract (T14 owns every exit).** `Execute`
  returns exactly: **0** (`--help`; or no files → `"No C# files to analyze."` on stdout; or max
  CRAP ≤ 8.0), **1** (parse error `ArgumentException` → `ex.Message` on stderr + usage on stdout; or a
  fail-fast trigger; or a propagated exception realized at T15), **2** (`MaxCrap(metrics) > 8.0`, strict
  → `"CRAP threshold exceeded: {max:F1} > 8.0"` on stderr, `InvariantCulture`). This realizes
  departure #1 (exit 1 for fail-fast) and pins the exit-2 threshold path departure #1 did not
  enumerate. Do not move an exit decision out of T14.

- **D-T14b — three fail-fast triggers, greppable stderr anchors (departure #1).** All module/run-level;
  the per-method `N/A` path (a method absent from a *populated* report) stays in `CrapAnalyzer`,
  **unchanged**. (1) `CoverageReportLocator.Locate` returns `null` → anchor **`No coverage report was
  produced`** → exit 1. (2) `CoberturaCoverageParser.Parse(report).Count == 0` → anchor **`contained no
  coverage data`** → exit 1 — trigger 2 must inspect **`coverageMap.Count`, not the metrics** (a
  populated-but-non-matching report also yields all-`N/A` metrics yet must NOT fail-fast). (3)
  `CoverageRunner.GenerateCoverage` throws `CoverageException` → **propagates** (anchor **`Coverage
  command failed with exit`**), realized as exit 1 via `Program.Main`'s top-level `catch (Exception)`
  (broadened in T20; same `"Coverage command failed with exit"` anchor). Mr. Das ruled the trigger 1/2 wording to these defaults; tests
  assert only the bold anchor substring, so wording may re-tune without touching test structure.

- **D-T14c — double-parse LOCKED (option A); the `Analyze(map)` overload is DEFERRED.** T14 calls
  `CoberturaCoverageParser.Parse(report)` for the trigger-2 empty check, then
  `CrapAnalyzer.Analyze(filesToAnalyze, report)` re-parses the same small XML — negligible cost
  (dominated by the `dotnet test` run) and **zero API change** to a discharged T11 type
  (fidelity-first). Option B — add `CrapAnalyzer.Analyze(IReadOnlyList<string>,
  IReadOnlyDictionary<string, CoverageData>)` to share one parse — modifies a discharged API and is
  **Mr. Das's call**; deferred, not required for T14.

- **D-T14d — the T14/T15 propagation boundary (LOAD-BEARING for T15).** `Execute` does **not** catch
  the coverage-runner / parser throw (faithful to Java `execute … throws Exception`); it propagates.
  Java relied on the JVM exiting 1 on an uncaught exception, but **.NET does not guarantee exit 1 on an
  unhandled exception**, so T15's `Program.Main` **must** catch and convert to exit 1. This boundary was
  planned at T14 as a **bare `catch`** (the "D2" mechanism; Java has no coverage wrapper), but
  **superseded at T15 by ruling A** (see the T15 register / D-T15b): T15 instead throws the domain
  `CoverageException` and catches it **specifically** (`catch (CoverageException) → return 1`), realizing
  the **identical** observable behavior (full `ex` to stderr + exit 1) while keeping the entry-point
  catch least-privilege / CA1031-clean with **no** suppression. The load-bearing invariant is unchanged:
  `Execute` keeps propagating and `Program.Main` MUST catch to guarantee exit 1. (T15 shipped;
  `Program.cs` is no longer a stub.) **T20 addendum** — `Program.Main` is re-broadened to a top-level
  `catch (Exception)` catch-all under a scoped `[Program.cs]` CA1031 `.editorconfig` relaxation
  (justification-commented; no `#pragma`/`[SuppressMessage]`); the load-bearing invariant (`Execute`
  propagates; `Program.Main` catches → exit 1) is **preserved and generalized to all exceptions**, not
  just `CoverageException` — full Java `main throws Exception` parity.

- **Below the departure bar (no departure number, no ruling needed).** `MaxCrap`/`ThresholdExceeded`/
  `Usage` are housed on `CliApplication` (T14), not on `Program` (T15) as Java put `maxCrap`/`usage` on
  `Main` — avoids a composition→entry forward dependency and keeps T14 self-contained/testable; pure
  organization, behavior/tests unchanged. Java's pre-format `metrics.sort` is omitted because both
  `CrapAnalyzer` (T11) and `ReportFormatter` (T4) already apply the identical stable "scored desc, N/A
  last" sort and `MaxCrap` is order-free (behavior-preserving). The single-line trigger-1 message
  (avoids SA1118) is byte-identical to the fail-fast contract.

## T15 register — `Program` entry point + spawn integration tests (S4; Anders T15 review, 🟢)

Resolved decisions and load-bearing invariants from the entry-point task. `Program.Main` faithfully
ports crap4java's `Main.main` shape — resolve the project root, wire the real console streams + a real
`ProcessCommandExecutor`, delegate to `CliApplication.Execute`, and convert an escaping coverage-command
failure to exit 1 — while composition and exit ownership stay on `CliApplication` (D-T14b), so `Program`
adds no logic of its own. **No new behavioral departure beyond the already-approved set #1–#8.**

- **D-T15a — `Program` is a thin entry point only (no `Run` seam).** `Main` composes and delegates in a
  single expression; there is **no** intermediate `Run`/composition-root method, and
  `MaxCrap`/`Usage`/`ThresholdExceeded` stay on `CliApplication` (D-T14b), not on `Program` (where Java
  put `maxCrap`/`usage` on `Main`). This avoids a composition→orchestration forward dependency and keeps
  the S5/T17 resolution-model swap localized to the resolution seam (D-T12b) — at T17 that seam became
  `OwningProjectResolver` + `TestProjectResolver` (departure #9), no longer the single
  `ModuleRootResolver`; either way `Program` never learns how the root is found.

- **D-T15b — `CoverageException` + the specific `catch (CoverageException) → return 1` REALIZES and
  DISCHARGES the D-T14d boundary (LOAD-BEARING on both ends).** `CoverageRunner.GenerateCoverage` throws
  the domain `public sealed class CoverageException` (three standard ctors, **no** serialization ctor —
  the modern net8.0 CA1032 form; the legacy ctor is obsolete under `SYSLIB0051`), message byte-identical
  (`"Coverage command failed with exit N"`); `CliApplication.Execute` **keeps propagating** it (never
  catches); `Program.Main` catches it **specifically** and returns 1, writing the full `ex` (type +
  message + stack via `Console.Error.WriteLine(ex)`, ruling B) to stderr. This is what guarantees exit 1
  on .NET (which does **not** guarantee it for an unhandled exception), so **both** ends are load-bearing:
  `Execute` must not start catching, and `Program.Main` must not stop catching. The typed catch is
  CA1031-clean with **no** `.editorconfig` entry / suppression, **superseding the T14-planned bare
  `catch`** (D-T14d). **Superseded for `Program.cs` by T20** — `Program.Main` now uses a top-level
  `catch (Exception) → Console.Error.WriteLine(ex) → return 1`, licensed by a scoped `[Program.cs]`
  CA1031 `.editorconfig` relaxation (justification-commented; **no** `#pragma`/`[SuppressMessage]`).
  `CoverageException` is **retained** — `CoverageRunner` still throws it with the byte-identical
  `"Coverage command failed with exit N"` anchor, now caught by the broad catch (no behavioral loss, no
  message change). The load-bearing invariant is unchanged and generalized: `Execute` keeps propagating;
  `Program.Main` now converts **every** escaping exception (not just `CoverageException`) to exit 1 —
  full parity with Java `main throws Exception`. Mr. Das's register wording, verbatim: "introduce `CoverageException` to enable a
  least-privilege specific catch (CA1031-clean, no suppression); observable behavior unchanged (stderr +
  exit 1), so not a behavioral departure — **supersedes D2**." (Here "D2" = the T14-planned bare-catch
  mechanism recorded in D-T14d, **not** the feature-doc deferral "D2 = O2 subprocess timeout".)

- **D-T15c — `projectRoot = Path.GetFullPath(".")` (LOAD-BEARING for downstream path combines).** Mirrors
  Java `Path.of(".").toAbsolutePath().normalize()`: an absolute, normalized current directory. Every
  downstream combine/resolve (at T17 `OwningProjectResolver.ResolveOwningProjects` /
  `TestProjectResolver.ResolveTestProject`, formerly `ModuleRootResolver.Resolve`; the coverage results
  dir; explicit-file resolution) assumes an absolute normalized root, so this exact spelling is
  load-bearing — do not pass a bare `"."`.

- **D-T15d — spawn resolution: `dotnet <Microsoft.Crap4CSharp.dll>` via `AppContext.BaseDirectory`.** The
  app assembly is `Microsoft.Crap4CSharp.dll` (`Common.targets`:
  `AssemblyName=Microsoft.$(MSBuildProjectName)`), located next to the test assembly through
  `AppContext.BaseDirectory` — present at run time because the app is a **ProjectReference** of the test
  project, so its DLL is copied into the test output dir. The tests launch it **framework-dependent** as
  `dotnet <dll>`, **not** the OS apphost (`Microsoft.Crap4CSharp.exe` on Windows,
  extension-less/possibly non-executable after xcopy on Linux), so both spawn tests run uniformly on
  every CI OS (incl. ubuntu).

- **D-T15e — the two spawn tests must never reach a coverage run (fast/deterministic under D-T8).** Both
  ported crap4java `MainTest.mainProcess*` cases pick arguments that short-circuit **before**
  `CliApplication` reaches `CoverageRunner.GenerateCoverage`: `--help` → usage / exit 0; `--changed` + a
  file arg → parse error (`ArgumentException`) → stderr / exit 1. No real `dotnet test` is ever spawned,
  so the tests are fast and deterministic even though D-T8 runs them by default. The four remaining Java
  `MainTest` cases (#1/#4/#5/#6) are covered in-process by `CliApplicationTests` (a D-T14b relocation, not
  a reduction) — no fidelity loss.
  - **Below-threshold consequence of the least-privilege catch (no departure number, no ruling needed).**
    The specific `catch (CoverageException)` narrows the converted-to-exit-1 surface to the coverage-
    **command** failure. A `CoberturaCoverageParser.Parse` `InvalidOperationException` (malformed XML,
    D-T10a) would **not** be converted to a clean exit 1 — but `CoverageReportLocator.Locate` only ever
    surfaces a **real coverlet** report (well-formed by construction), so this path is **untested and
    practically unreachable**. It is a deliberate below-threshold consequence of the typed catch, **not**
    a new behavioral departure (the reachable observable behavior — stderr + exit 1 on a coverage-command
    failure — is unchanged from the bare-catch plan). **Resolved by T20** — the broad top-level
    `catch (Exception)` in `Program.Main` **does** convert these paths (malformed-XML / parser / git /
    I/O throws) to exit 1; the narrowing consequence no longer exists.

## T20 register — exit-code parity (S6 close-out; Anders T20 review, 🟢)

Resolved decisions and load-bearing invariants from the entry-point exit-code hardening. T20 broadens
`Program.Main`'s catch from the specific `catch (CoverageException)` (T15 / ruling A) to a top-level
`catch (Exception) → Console.Error.WriteLine(ex) → return 1`, licensed by a scoped `[Program.cs]`
CA1031 `.editorconfig` relaxation. **Not a new behavioral departure — it RESTORES Java parity**
(`main throws Exception`: the JVM already exits non-zero on any escaping exception; the pre-T20 escape
of a non-`CoverageException` throw as a platform-specific unhandled-exception code was the deviation).
Supersedes ruling A / D-T15b **for `Program.cs` only** (Mr. Das). **No new behavioral departure beyond
the already-approved set #1–#8.**

- **D-T20a — CLOSES the S6 clean-room "fatal exit-code consistency" finding.** Pre-T20 `Program.Main`
  caught **only** `CoverageException`, so a `git` failure (under `--changed`), an I/O error, or a
  malformed-coverage-XML / parser throw **escaped** and surfaced as a platform-specific
  unhandled-exception exit code instead of the documented `1`. The broad `catch (Exception)` converts
  **every** escaping exception to exit 1 (full exception — type + message + stack — to stderr via
  `Console.Error.WriteLine(ex)`, ruling B). `CoverageException` is **retained** — `CoverageRunner` still
  throws it with the byte-identical `"Coverage command failed with exit N"` anchor, now caught by the
  broad catch (no behavioral loss, no message change).

- **D-T20b — scoped `[Program.cs]` CA1031 relaxation (LOAD-BEARING; ratified convention).** T20 appends
  a trailing `[Program.cs]` section to `.editorconfig` — the **first single-filename (per-file) section
  in the repo**; every prior section is a glob/extension pattern (`[*]`, `[*.md]`, `[*.{cs,vb}]`, …) and
  the C# analyzer settings it overrides live in the global `[*.{cs,vb}]` block. EditorConfig has no
  CSS-style specificity — later matching sections win by **order**, so the trailing per-file section
  deterministically overrides the global block for the single `src/Crap4CSharp/Program.cs` (top-level
  statements are disabled, so no generated/duplicate `Program.cs` exists). The section carries a
  justification comment and sets `dotnet_diagnostic.CA1031.severity = none`. **Ratified convention:**
  license a legitimate, localized analyzer violation via a narrowly-scoped, commented `.editorconfig`
  section — **never** `#pragma` / `[SuppressMessage]`. This is the sole sanctioned catch-all; no other
  file is affected.

- **D-T20c — the exit-code matrix is reaffirmed (D-T14a still owns the 0/1/2 table).** `Program.Main`'s
  `try` returns `CliApplication.Execute(args)` **verbatim**; the `catch (Exception)` intercepts **only
  thrown** exceptions. Two invariants: **(I1)** no deliberate exit code is clobbered — every NORMAL
  RETURN (0 / 1 fail-fast / **2 threshold-exceeded**) flows through the `try` and is returned unchanged;
  **threshold-2 stays a normal return, never swallowed to 1**. **(I2)** every genuinely-thrown /
  propagated failure → 1 (pre-T20 only the `CoverageException` throw converted; now all THROWN paths —
  git / I/O / parser / coverage-command — convert). Pinned by the new spawn test
  `MainProcessExitsOneWhenGitFailsForChanged` (`--changed` in a hermetic non-git temp cwd →
  `ChangedFileDetector` throws `InvalidOperationException("git status failed: …")` → catch-all → exit 1;
  git isolation via `GIT_CEILING_DIRECTORIES` set once in the shared `RunEntryPointAsync` helper). The
  `Execute`-level exit-2 pin (`CliApplicationTests` #12 `ReturnsTwoWhenCrapThresholdExceeded`) is
  unchanged.

- **D-T20d — NOT a new departure (T20 adds no departure number).** T20 **restores** Java's
  `main throws Exception` "any failure → non-zero" contract rather than departing from it, so **T20**
  introduces **no** new departure number and makes **no** edit to departure #1 or its exit table
  (D-T14a). *(Historical note: at T20 the ledger stood at #1–#8. Departures **#9** (Model B resolution)
  and **#10** (unit-only target-test filter) were added by the later-executed **T17/S5** slice — a
  separate task — so the original "no departure #9 / #1–#8 untouched" wording refers to **T20's**
  contribution, not the final ledger; see the departures list and the T17 register.)* A future reader
  should **not** expect a departure entry for T20 — its reconciliation lives in the amended T14/T15
  registers (D-T14b / D-T14d / D-T15b / D-T15e) and this T20 register. **D-T15a preserved** —
  `Program` stays a thin, seam-less entry point (no `Run` method); the catch-all parity path is exercised
  end-to-end through the real entry point by the spawn test, needing no injection seam.

## T17 register — Module & test resolution finalization (Model B); S5 close-out (Anders T17 review, 🟢)

Resolved decisions and load-bearing invariants from the S5 resolution-finalization slice (executed after
S6/T20). Mr. Das ruled **Model B**: a bounded nearest-`.csproj` owning-project resolver plus a
`<Project>.Tests`/`.UnitTests` transitive-`ProjectReference` test-project resolver, running the ONE
resolved test project's **unit** tests. Adds departures **#9** (Model B resolution) and **#10**
(unit-only target-test filter); **supersedes #6**, **retires #8**, **keeps/reaffirms #7**. See those
departure entries for the behavioral contract; this register records the seam reshape, the rulings, and
the invariants.

- **D-T17a — the resolver split is a genuine SRP seam (Model B).** `OwningProjectResolver` (pure bounded
  FS walk → owning `.csproj` file paths) and `TestProjectResolver` (XML/graph parse of `ProjectReference`
  closures) are two focused static resolvers, orchestrated by `CliApplication.Execute` (which owns the
  resolve-once *policy* and every exit, D-T14a). Rejected: a single mega-resolver returning a record
  (hides two different concerns behind one API; the graph logic is harder to unit-test in isolation).

- **D-T17b — span policy = FAIL-FAST exit 1 (Mr. Das ruling; DECISION 1).** When the analyzed files
  resolve to **>1** distinct owning project, `Execute` fail-fasts with anchor **`span multiple
  projects`** (exit 1) — NOT an ordinal-first pick. Model B runs ONE test project per run; silently
  picking one owner would re-introduce the cross-project misattribution (project-B files scored against
  project-A coverage → all-`N/A` → inflated CRAP → possibly spurious exit 2) that departure #8 was
  retired to remove. The normal single-project baseline (R2/R6) yields exactly one owner in all three CLI
  modes, so this only bites multi-project trees, which the user must narrow.

- **D-T17c — append-only numbering (Mr. Das ruling; DECISION 2).** Departure **#9** is APPENDED; #6 is
  annotated **SUPERSEDED by #9**, #8 annotated **RETIRED/CLOSED by #9**, #7 **KEPT/reaffirmed** — all
  in-place bodies retained for audit, none rewritten. The prior T20 register's "no departure #9 / #1–#8
  untouched" wording is scoped to **T20's** contribution; #9/#10 are this slice's additions (D-T20d
  annotated accordingly).

- **D-T17d — unit-only target-test filter (departure #10; reverses the earlier stance).** The resolved
  target test project runs under `--filter "type!=IntegrationTests"` (exclusion form; untagged INCLUDED
  because VSTest treats an absent `type` as `!=` any value). `CoverageRunner.UnitTestFilter`
  (`private const`, never `internal`) is the single adjustable source of truth; narrowing to
  `{UnitTests, absent}` is the one-line append `&type!=Unit`. Mirrors mutate4csharp **minus** its
  mutation-cycle-only `Category!=no-mutate` clause (no crap4csharp analog — coverage runs once). Our
  fail-fasts are exit **1**, not mutate4csharp's **2**. Proven behaviorally by
  `CoverageFilterBehaviorTests` (real nested `dotnet test`), token-pinned by `CoverageRunnerTests`.

- **D-T17e — malformed-EXISTING `.csproj` treated as zero references (FLAG-1 resolution; Anders
  in-lane).** `TestProjectResolver.DirectProjectReferences` wraps `XDocument.Load` in
  `catch (XmlException) → return []`, so an existing-but-malformed `.csproj` in a name-matching
  candidate's transitive closure contributes zero references instead of throwing (`File.Exists` already
  guards the MISSING branch). This makes the type's own "missing/unparseable … skip, never throw" comment
  TRUE and matches the ratified non-throwing marker-probe discipline (`OwningProjectResolver`,
  `CoverageReportLocator`, `SourceFileFinder`); because `OrderBy` forces `ReferencesTransitively` over
  every name-matching candidate, this prevents a stray malformed candidate from aborting otherwise-
  successful speculative resolution. Scope: **`XmlException` only** (CA1031-clean, no suppression — the
  `[Program.cs]` CA1031 relaxation D-T20b does NOT extend here); genuine FS faults (`IOException`) still
  propagate to the T20 catch-all (exit 1). Pinned by
  `TreatsMalformedExistingCandidateAsZeroReferencesAndResolvesValidSibling`.

- **D-T17f — fail-fast triggers grow 2 → 4 (+1 span); new greppable anchors (departure #1; extends
  D-T14b).** `Execute` now fail-fasts on **`No owning .csproj`**, **`span multiple projects`** (DECISION
  1), and **`No test project`** — all BEFORE any coverage run (assert the runner is not invoked) — then
  the two existing post-run triggers **`No coverage report was produced`** / **`contained no coverage
  data`**. The exit table (D-T14a) is structurally unchanged (0/1/2); resolution adds only new exit-1
  reasons. Tests assert the bold anchor substring, so wording may re-tune freely.

- **D-T17g — D-T12b/D-T14/D-T15a seam promise amended (FLAG-9 ratified).** The "S5/T17 swap touches only
  `ModuleRootResolver`" promise is **broken by design**: resolution split into
  `OwningProjectResolver` + `TestProjectResolver`, and `CoverageRunner.GenerateCoverage` gained a
  `testProject` token + `coverageBaseDirectory`. Only `CoverageReportLocator` stayed code-identical; the
  D-T12e `ResultsDirectoryName` reciprocal contract and coverage-base = `_projectRoot` are preserved. The
  decoupling INTENT survives (coverage/locate take a pre-resolved root and never resolve on their own).

## T21 register — Async/iterator coverage attribution; S8 close-out (Anders T21 review, 🟢)

Resolved decisions and invariants for the S8 hardening slice (executed after S7 dogfood, which
empirically confirmed the blind spot on `../mutate4csharp`: three tested state-machine methods —
`CoberturaLineCoverageParser.ResolveAbsolutePaths` (iterator), `ProcessRunnerSupport.DrainAsync` and
`TimedProcessRun.DrainAsync` (async) — all reported `N/A` and escaped the exit-2 gate). Adds departure
**#11**; touches no other departure. See departure #11 for the behavioral contract; this register records
the design rulings.

- **D-T21a — attribute the `MoveNext` method ONLY, not the whole state-machine class (Anders design
  call; in-lane).** Coverlet records the user's async/iterator body's real executable lines on the state
  machine's `MoveNext`; the sibling synthetic members (`.ctor`, `SetStateMachine`, `IDisposable.Dispose`,
  `get_Current`, `Reset`, `GetEnumerator`) either carry no `<line>` or carry synthetic non-user lines
  that would pollute the covered/missed counts. Aggregating all methods in the class was REJECTED
  (pollution + double-count risk); reading only `MoveNext` is the faithful signal for R3. Minor accepted
  under-count: an iterator's `try/finally`/`using` teardown that coverlet places on `Dispose` is not
  folded in — negligible and consistent with "score the body."

- **D-T21b — class-level detection routes state machines AWAY from `IsCompilerGeneratedMethod` (surgical
  predicate flow; provably preserves R2/R3/R4 + lambda skip).** `Parse` classifies each `<class>` first:
  a nested last segment `<Method>d__N` (non-empty `Method`, `>` immediately followed by `d__`+digits) is
  handled by a new state-machine branch (read `MoveNext` -> attribute); EVERY other class still flows into
  the UNCHANGED `ReadClassMethods` -> `IsCompilerGeneratedMethod`, so Rule 1 still wholesale-skips the
  remaining angle-bracket classes (lambda display `<>c` / `<>c__DisplayClassN`), and Rules 2/3/4 (`b__`/
  `g__` display methods, accessors, ctors) are untouched. The empty-bracket check is what keeps lambda
  display classes skipped (finding #3 deferred). `IsCompilerGeneratedMethod`'s Rule-1 comment is updated
  to note state machines are now diverted upstream.

- **D-T21c — disambiguation via the existing exact->nearest lookup (no CrapAnalyzer change).** The source
  method's `StartLine` (declaration line, `m.GetLocation()`) never equals the MoveNext body min-line, so
  the attributed key resolves via `NearestCoverage`'s ordinal prefix scan — the SAME mechanism that
  already resolves regular overloads (W-T11a). Overloaded async/iterator methods emit multiple
  `EnclosingType#M:line` keys (distinct MoveNext min-lines) and pick the nearest to each declaration.
  Nested/generic enclosing types keep backtick arity via `NormalizeTypeName` (D-T10b). Residual: the
  pre-existing overloaded-by-adjacent-line ambiguity is unchanged (NOT worsened), and explicit-interface
  async/iterator methods (coverlet may mangle `<IFace.M>d__N`) may still miss -> `N/A` as today (no
  regression) — neither needs a Mr. Das decision.

- **D-T21d — two existing wholesale-skip tests RESHAPED; async + iterator attribution tests ADDED.**
  `SkipsSyntheticStateMachineClassButKeepsRealMoveNext` and `MatchesRealCoverletSampleClassNames` pinned
  the OLD "state machine skipped wholesale" behavior; both are reshaped to assert the state machine's
  MoveNext coverage is now attributed to the source-method key while synthetic siblings, accessors,
  ctors and lambda `b__`/display classes stay skipped. New parser + `CrapAnalyzer` tests pin a REAL
  coverlet `<M>d__N` sample (mirroring `DrainAsync`/`ResolveAbsolutePaths`): the source method reports
  REAL coverage (not `N/A`) and the correct CRAP (async CC 2 @ 70% -> 2.108; iterator CC 3 @ 70% ->
  3.243). A `<>c__DisplayClassN` regression guard asserts lambdas stay skipped.

- **D-T21e — finding #3 (lambdas/local functions) explicitly DEFERRED.** Lambda bodies (`<M>b__N`, hosted
  on `<>c`/`<>c__DisplayClassN`) and local functions (`<M>g__...|N`) still resolve to skipped coverage; a
  captured lambda whose lines coverlet inlines onto the outer method are already counted, but a
  display-class-hosted lambda's own coverage is not attributed. Out of scope for S8 (Mr. Das ruled
  surgical: `d__N` only). Flagged for a future slice if Mr. Das schedules it.

- **D-T21e — RESOLVED at T23 (finding #3 CLOSED; append-only).** T23's **departure #13** generalizes the
  T21 attribution to lambdas (`<M>b__N`), local functions (`<M>g__L|N`) and async-locals, so the
  finding-#3 gap DEFERRED above is now closed: a display-class-hosted lambda's / local function's own
  coverage IS attributed and UNIONed into its source method — its earlier SKIP behavior INVERTS to
  attribution. Departure #11's `d__N` async/iterator behavior is UNCHANGED; #13 only ADDS the other
  member shapes (no edit to #11's prose). See the **T23 register** (D-T23a–e) and **departure #13**.

## T22 register — locator surfaces all reports; multi-report refusal folds into departure #1

- **D-T22a — the `multiple coverage reports` refusal REALIZES/EXTENDS departure #1 (no new #12, no
  renumber of #1–#11).** A multi-TFM build that emits >1 `coverage.cobertura.xml` is a **fail-fast**,
  not a silent single-pick — mirroring the T17 `span multiple projects` precedent (departure #9: Mr.
  Das ruled FAIL-FAST, not ordinal-pick, to avoid re-introducing the cross-project misattribution
  departure #8 was retired to remove). This adds **no** new departure number and makes **no** edit to
  departures #1–#11 or the exit table (D-T14a); it folds under **departure #1**'s fail-fast family,
  whose triggers grow **5 → 6** — pre-run `No owning .csproj` / `span multiple projects` / `No test
  project`, post-run `No coverage report was produced` / `contained no coverage data`, and now the new
  post-run `multiple coverage reports`.
  - **T24 update (append-only; does NOT amend D-T22a's ruling above).** Departure **#12** IS introduced
    later (T24 — per-file coverage-key basename). This does **not** contradict "no new #12" above: that
    clause correctly declined to number the *multiple-reports refusal* (which stays folded under #1).
    #12 is an unrelated C#-specific coverage-key collision fix; it neither renumbers #1–#11 nor touches
    the fail-fast family. See the **T24 register** and departure **#12**.
- **D-T22b — `Locate` → `LocateAll` (locator surfaces ALL matches; CLI owns the exit).**
  `CoverageReportLocator.Locate` (a single ordinal-first `FirstOrDefault` pick) is replaced by
  `LocateAll`, which returns **every** discovered `coverage.cobertura.xml` **ordinal-sorted**
  (`StringComparer.Ordinal`, OS-independent per departure #3) with **no single-pick**. The
  select-or-refuse policy moves up to `CliApplication`, which owns every exit per **D-T14a**: 0 → the
  existing `No coverage report was produced` path, exactly 1 → proceed to parse, >1 → fail-fast.
  Consistent with departure #9 (the resolver surfaces the set; `Execute` owns the exit).
- **D-T22c — new greppable anchor `multiple coverage reports`, exit 1 (our convention).** When
  `LocateAll` returns >1 path, `CliApplication` writes the bold-anchor substring **`multiple coverage
  reports`** to stderr and exits **1** — our convention, NOT mutate4csharp's `2` (mirrors departures
  #1/#9). Fired after the coverage run, during report location. Tests assert the anchor substring, so
  surrounding wording may re-tune freely.

## T24 register — coverage-key collision; per-file basename segment (S9; Anders T24 review, 🟢)

Resolved decisions and watch-items from the final S9 attribution task. T24 is a **C#-specific**
enrichment with **no Java analog**: the frozen reciprocal key gains a source-file **basename** segment
so overloads (and, post-T23, compiler-generated members) of a **partial class split across files** stop
borrowing each other's coverage via the nearest-line lookup. Adds **departure #12** (the first new
departure number since #11); `NormalizeTypeName` and the frozen `TypeName` form are **unchanged**.

- **D-T24a — key format RULED by Mr. Das: `TypeName#method#basename:line`.** The key gains ONE new
  segment — `Path.GetFileName(<class filename>)` — inserted **between** `#method` and `:line`. Ruled
  over the signature-based alternative (`TypeName#method(sig):line`): the basename is what coverlet
  already emits per `<class filename=…>` and is the exact dimension a split partial class varies,
  whereas method signatures would also have to be threaded from Roslyn and normalized to coverlet's
  spelling (a second frozen contract). `:line` (the MIN `@number`) **stays LAST** so `ParseTrailingLine`'s
  last-`:` split and the reciprocal nearest-line scan are byte-for-byte unchanged.
- **D-T24b — `NormalizeTypeName` and the frozen `TypeName` form are UNCHANGED (contract preserved).**
  The basename is a **new, separate** key segment, **not** part of `TypeName`, so the T9⇄T10⇄T11 frozen
  reciprocal `TypeName` contract (and its D-table pins) is untouched. `NormalizeTypeName` keeps emitting
  the same dotted-FQN-plus-arity form; only the surrounding key grows a `#basename` segment.
- **D-T24c — BOTH producers + the consumer thread the basename identically.** Producer 1
  `ReadClassMethods` and the T21 state-machine path `ReadStateMachineMoveNext` each read
  `Path.GetFileName(classElement filename)` **once per `<class>`** and emit `…#method#basename:line`; the
  consumer `CrapAnalyzer` computes `Path.GetFileName(file)` **once per source file** (every method parsed
  from that file shares it) and threads it through `LookupCoverage`/`ExactCoverage`/`NearestCoverage`.
  `Path.GetFileName` normalizes both `/`- and `\`-separated coverlet paths, so producer and consumer agree
  OS-independently (departure #3).
- **D-T24d — a missing/empty `filename` yields an unmatchable key (fail-safe to N/A, never
  mis-attribute).** If coverlet emits no `filename`, the producer basename is `""` → a `Type#method#:line`
  key that the consumer (which always has a real `Path.GetFileName(file)`) can never match → the method
  stays per-method `N/A` (departure #1 family), strictly SAFER than the pre-T24 silent cross-file borrow.
  No new exit-code behavior.
- **D-T24e — the T21 state-machine key ALSO gains the basename (kept in lockstep).** Departure #11's
  attribution key becomes `NormalizeTypeName(enclosing)#Method#basename:minMoveNextLine` in the same
  shape, so async/iterator attribution keeps matching post-T24. Departure #11's prose describes the
  pre-basename key and is read as superseded by this register (no #11 text edit — its behavior is
  unchanged; only the key literal grew a segment).
- **D-T24f — reciprocal-key parity suite RESHAPED; split-partial-class collision proof ADDED.** The
  FROZEN T10/T11 key-parity tests are re-pinned to the three-segment `TypeName#method#basename:line` form
  (exact + nearest); `SplitPartialClassOverloadsResolveToOwnFileCoverage` proves two `Demo.Widget.Render`
  overloads in `A.cs`/`B.cs` at overlapping min-lines resolve to their OWN file's coverage
  (A → 100%/CRAP 1.0, B → 0%/CRAP 6.0) instead of borrowing. Bhaskar re-verifies the entire attribution
  suite green.
- **D-T24g — residual same-basename-different-directory collision ACCEPTED (documented, not fixed).**
  The key carries the **basename only**, not the directory, so two partial-class files with the same
  basename in different directories (`Foo/Widget.cs` + `Bar/Widget.cs`) can still collide. Judged rare;
  fixing it would require threading a project-relative path coverlet does not always emit consistently.
  Logged as a watch-item; revisit only if a real target hits it.

## T23 register — compiler-generated member coverage attribution (S9; Anders T23 review, 🟢)

Resolved decisions and residuals for the final S9 attribution task, which GENERALIZES T21. T23
attributes EVERY compiler-generated member — lambdas (`<M>b__N`), local functions (`<M>g__L|N`) and
async-locals (`<<M>g__...>d__N`) — to its enclosing SOURCE method and UNIONs it into that method's
coverage, closing finding #3 (never-invoked lambdas/local functions previously reported `N/A` and
escaped the exit-2 gate). Adds **departure #13**; departure #11's `d__N` async/iterator behavior is
UNCHANGED and its A2 direct-emit path (`ReadStateMachineMoveNext`) is byte-for-byte preserved. See
departure #13 for the behavioral contract; this register records the design rulings.

- **D-T23a — one demangler + A→B→C class-level precedence (surgical; generalizes D-T21b).** A new
  `SyntheticMemberNameRegex` (`^<([^<>]+)>[bg]__`) extracts the source method from a compiler-generated
  MEMBER name — `[^<>]+` is the first angle-bracket group, which a user method never contains. `Parse`
  routes each `<class>` by precedence: **A** a state machine (`TryParseStateMachine`), split into **A1**
  async-local (the inner name ITSELF demangles via the member regex → COLLECT its `MoveNext` against the
  outer method) or **A2** plain async/iterator (a bare inner `M` → the UNCHANGED T21
  `ReadStateMachineMoveNext` DIRECT-EMIT); **B** a lambda display class (`TryGetDisplayClassEnclosing` —
  last `/`|`+` segment starts with `<>c`) → COLLECT each `<M>b__`/`<M>g__` member; **C** a real class →
  `ReadClassMethods`, which now COLLECTS real-class-hosted `b__`/`g__` members before its skip predicate.
  Precedence is unambiguous: a plain `d__` has `inner="M"` (A2), an async-local `d__` has
  `inner="<M>g__L|N"` (A1), and a `<>c` never carries a `>d__N` tail so it can only reach B.

- **D-T23b — phase-2 nearest-body MERGE is a faithful disjoint-line UNION; same-file overloads preserved
  (C6).** Members are COLLECTED in phase 1 (document order) as `SyntheticContribution` carriers, then
  `MergeSyntheticContributions` folds each into the NEAREST existing `Type#SourceMethod#basename:*`
  entry (closest trailing line by `Math.Abs`, strict `<`, first-in-enumeration wins ties) — MIRRORING
  `CrapAnalyzer.NearestCoverage` / W-T11a — else CREATES one at the member's MinLine. Summing
  `(missed, covered)` is a set UNION, not a double-count: every physical source line is owned by exactly
  ONE coverlet `<method>`, so body and synthetic lines are DISJOINT. Because the anchor is nearest-line,
  a synthetic folds into ITS OWN overload's body span, so distinct same-file overloads are NEVER blended
  (C6 preserved). The inner scan is read-only and the map is mutated AFTER it, so the map is never
  enumerated while mutated. The producer keeps a producer-local `ParseTrailingLine` (it must NOT depend
  on the consumer `CrapAnalyzer`), mirroring the consumer's last-`:` semantics.

- **D-T23c — safety property: `filename` mismatch → safe `N/A`, never a false pass (D-T24d family).**
  Each member key carries the display-class / state-machine `filename` basename (T24). If that differs
  from the enclosing method's own source file, the UNIONed key never matches the consumer's
  `Type#method#Path.GetFileName(file):` prefix, so the method stays per-method `N/A` — a SAFE
  under-report even when the synthetic member is fully covered. Pinned by
  `DisplayClassWithMismatchedFilenameYieldsNAnotFalsePass`. Attribution can only ADD real coverage to the
  CORRECT method or fall to `N/A`; it can never fabricate a false pass.

- **D-T23d — skip-guard INVERSION (ordering change only; `IsCompilerGeneratedMethod` UNCHANGED).**
  `ReadClassMethods` now demangles a `b__`/`g__` member and COLLECTS it BEFORE the
  `IsCompilerGeneratedMethod` skip predicate, so a recognized member is UNIONed into its source method
  instead of dropped wholesale. The predicate body is untouched: Rule 1 (angle-bracket CLASS) is now
  effectively dead on the real-class path (every angle-bracket class is routed to A or B upstream), and
  Rule 2 (angle-bracket METHOD) now only catches a residual UNRECOGNIZED synthetic (neither `b__` nor
  `g__`). This is what closes finding #3: a never-invoked local function `<Compute>g__Validate|0_0` @ 0%
  now UNIONs onto `Compute` (CC 1 → CRAP 2.0) instead of being skipped to `N/A`.

- **D-T23e — two residuals, both safe-by-construction to `N/A` (no Mr. Das decision needed).**
  (1) **Nested-synthetic enclosing** — a lambda/local function nested inside another synthetic, whose
  split enclosing still carries a `<>c...` segment, demangles to a `NormalizeTypeName` that never matches
  a real Roslyn `TypeName`, so its key is unmatchable → safe `N/A`, never a mis-attribution.
  (2) **Inherited D-T24g** — T23 keys carry the basename only, so the same-basename-different-directory
  partial-class collision T24 accepted applies here too; judged rare, logged as a watch-item, revisit
  only if a real target hits it.

## Deliberate departures from crap4java (approved by Mr. Das)

1. **Fail fast** — when a module produces no coverage / runs no tests, exit non-zero (`1`) with a
   greppable stderr message, instead of the Java warn + `N/A` + exit 0. Applies at the **module/run**
   level only; **per-method `N/A` is unchanged** (a method absent from a populated report still yields
   an `N/A` row).
2. **Richer cyclomatic complexity** — augmented Roslyn node set (below). CC is therefore **not
   numerically comparable** to `crap4java` on code using the modern constructs.
3. **Determinism/idiom** — nullable reference types enabled; `InvariantCulture` + explicit `"\n"` in
   the report; `StringComparer.Ordinal` sort in `SourceFileFinder` **in place of** Java's
   platform-dependent `Comparator.naturalOrder()` over `Path`, and the same `StringComparer.Ordinal`
   sort in `ChangedFileDetector` **in place of** Java's `Path::compareTo` (both case-insensitive on
   Windows, case-sensitive on Unix), so source-file discovery and changed-file order are identical
   across OSes. Below the behavioral-departure bar (the final report is keyed/sorted downstream, not by
   discovery order); logged here only as the determinism counterpart to the `InvariantCulture`/`"\n"`
   choices.
4. **Line-based coverage field names** — `CoverageData`'s counter fields are renamed from crap4java's
   `missedInstructions`/`coveredInstructions` to `MissedLines`/`CoveredLines` (and the record's doc
   comment reframed to match). This port's coverage adapter is Coverlet → **Cobertura line** counters,
   not JaCoCo `INSTRUCTION` counters, so "instruction" terminology has no referent here. This is the
   **naming counterpart** to the already-approved coverage-granularity change (risk **R1**; see the
   Coverage row under "## Locked choices") — **not** a new behavioral departure: the `CoveragePercent`
   algorithm is byte-identical modulo the rename, and observable metrics/CRAP outputs are unaffected.
5. **Exclude C# build output** — `SourceFileFinder` omits any file under a path segment named exactly
   `bin` or `obj` (case-sensitive, segment-exact, at **any** depth beneath `src`; `Robin`/`object` are
   kept). Java's Maven `src` tree never holds build output (that lives under `target/`), so the Java
   finder needed no such filter; in C#, `bin`/`obj` are the analog of `target/` and contain
   compiler-generated `.cs` (`GlobalUsings.g.cs`, `AssemblyInfo.cs`, `.AssemblyAttributes.cs`,
   source-generator output). This filter is **load-bearing**: crap4csharp itself runs `dotnet test`
   (T12), which populates `bin`/`obj`, so without it generated methods would enter the analysis set and
   skew CRAP. This is a **departure that affects the analysis set** (unlike #3), yet it is faithful to
   Java's *intent* (analyze human-authored `src`). It deliberately does **not** exclude checked-in
   generated files (`*.g.cs`/`*.Designer.cs`) that live under normal source folders — Java analyzed
   generated `.java` under `src` too, so broadening the filter would break parity.
6. **Module-root walk unbounded (B1)** — **[SUPERSEDED at T17 by departure #9 (Model B): the unbounded
   `.sln`-first directory walk is replaced by a BOUNDED nearest-`.csproj` owning-project resolver
   (`OwningProjectResolver`) that never climbs above the invocation root; `.sln` is no longer a marker.
   Body retained for audit.]** `ModuleRootResolver.Resolve(startDirectory)` takes a single
   start path (no `workspaceRoot`), climbs **unbounded** to the filesystem root, and falls back to the
   **starting directory** when no `.sln`/`.csproj` marker is found — in place of crap4java's
   `moduleRootFor(workspaceRoot, file)`, which bounds the climb to `workspaceRoot` and falls back to
   `workspaceRoot`. Faithful to Java's *intent* (find the nearest module root at/above the start); the
   bound is dropped because the C# CLI carries no separate Maven-style `workspaceRoot`, real C# trees
   carry a `.sln`/`.csproj` so the walk terminates early, and termination at the filesystem root is
   guaranteed either way. Marker precedence: nearest `.sln` → nearest `.csproj` → start dir. (Ruled by
   Mr. Das; T13 shipped as `e4d1329`, no rework.)

7. **Resolve-once (single module root per run)** — `crap4csharp` resolves **one** module root per run
   via the existing `ModuleRootResolver` and runs coverage **once** at that root, in place of crap4java
   §6's per-module grouping (`groupByModuleRoot`/`analyzeByModule` — "group by module … run coverage
   once per module group"). T11/T12/T14 therefore resolve once and run once — **no** module-group loop
   in `CliApplication`/`CrapAnalyzer`, and multi-`.sln` (multi-module) targets collapse to the single
   resolved root: a knowing fidelity reduction from crap4java §6. Rationale: multi-module analysis is
   uncommon, the C# baseline already assumes a single resolvable `.sln` (R2/R6), and resolve-once keeps
   the pipeline materially simpler. **Test-parity consequence:** crap4java's module-grouping tests
   **adapt to resolve-once (assert a single resolve + single coverage run) or drop** — Bhaskar/Dave
   apply this at T11/T12/T14; no group-loop tests are ported. (Ruled by Mr. Das; supersedes watch-item
   **W17**.)

8. **Single-pick coverage report (ordinal-first)** — **[RETIRED/CLOSED at T17 by departure #9 (Model B):
   one owning project → one test project → exactly one `coverage.cobertura.xml`, so the multi-report
   ordinal-first single-pick and its DEFERRED aggregation are no longer needed (not implemented, not
   deferred); at T22 `CoverageReportLocator.Locate` was replaced by `LocateAll`, which surfaces ALL
   matching `coverage.cobertura.xml` paths (ordinal-sorted, no single-pick), and `CliApplication`
   fail-fasts on more than one (multi-TFM) — so there is no `FirstOrDefault` single-pick left to be
   "harmless" about. Body retained for audit.]** `CoverageReportLocator`
   (T12) deterministically
   selects **one** `coverage.cobertura.xml` via an **ordinal-first single pick** over the discovered
   report paths, and hands that single file to `CoberturaCoverageParser.Parse` (T10). Root-cause
   mismatch: coverlet emits **one report per test project** (`dotnet test --collect` drops a
   `coverage.cobertura.xml` per test project under `TestResults/`), whereas JaCoCo emits a **single
   per-module aggregate** — there is no coverlet analog of that aggregate, so the port picks one report
   rather than reading a ready-made union. **Consequence (under-reporting):** on a multi-test-project
   solution, methods whose coverage lives in the **non-picked** reports have no matching entry in the
   chosen report and resolve to per-method `N/A` (T11) — consistent with departure #1's "per-method
   `N/A` unchanged" — which makes a genuinely-covered method score as if **0-covered** and thereby
   **inflates its CRAP**; every test project except the picked one is under-reported. This is the
   coverage-side counterpart of **departure #7** (resolve-once): one root → one coverage run → now **one
   report**, and it is faithful to the common single-test-project baseline the port already requires
   (R2/R6 — one test project referencing `coverlet.collector`, one resolvable `.sln`). The single
   deterministic pick — **no** configuration flags and **no** report-merging branch — is deliberately
   the same shape as T10's single-path `Parse`. **Mr. Das-approved; multi-report coverage AGGREGATION
   (unioning the per-test-project reports) is DEFERRED to S5/T17**, whose scope now also owns report
   aggregation so multi-test-project solutions stop under-reporting. (Ruled by Mr. Das.)

9. **Module & test resolution (Model B)** — replaces the retired departure #6 walk with two bounded,
   deterministic stages, both scoped to the invocation root `_projectRoot` and never climbing above it.
   **(i) Owning project** = the nearest `.csproj` **file** at or above each analyzed `.cs`, found by
   `OwningProjectResolver` (renamed/reshaped from `ModuleRootResolver`): a **bounded** upward walk that
   stops at the invocation root, fixing the S6 unbounded ancestor climb. `.sln` is **no longer a
   marker** — the owning unit in the .NET ecosystem is a `.csproj`. The distinct owning set across the
   analyzed files is reduced ordinal-first, and **departure #7 (resolve-once) is KEPT/reaffirmed**: a
   run resolves exactly ONE owning project. **(ii) Test project** = `<Project>.Tests.csproj` **or**
   `<Project>.UnitTests.csproj` whose `ProjectReference`s **transitively** include the owning project,
   found by the new `TestProjectResolver` (recursive `.csproj` scan under the root, `bin`/`obj` excluded
   per departure #5, cycle-safe transitive walk, ordinal-first tie-break, non-throwing on
   missing/malformed `.csproj`). Coverage then runs **once** against that ONE test project, producing
   exactly ONE `coverage.cobertura.xml`. **New fail-fasts (exit 1 — our convention, NOT mutate4csharp's
   2; realizes departure #1), all fired BEFORE any coverage run:** `No owning .csproj` (no owner within
   bounds); `span multiple projects` (analyzed files resolve to >1 owning project — **Mr. Das ruled
   FAIL-FAST, not ordinal-pick**, to avoid re-introducing the silent cross-project misattribution
   departure #8 was retired to remove); `No test project` (no matching `.Tests`/`.UnitTests`
   transitively referencing the owner). **Load-bearing:** the `.Tests`/`.UnitTests` **naming-convention
   dependency** and **fail-fast-on-absence** are required for a target to be analyzable. **Supersedes
   departure #6** (unbounded `.sln`-first walk) and **retires/closes departure #8** (multi-report
   ordinal-first single-pick + deferred aggregation — one test project now means one report, so
   aggregation is neither implemented nor deferred). Ecosystem adaptation of crap4java §6 (Maven
   module-root `pom.xml` walk) plus mutate4csharp's owning-project + `.Tests` model; the knowing
   departure from crap4java parity on WHICH tests run is departure #10. (Ruled by Mr. Das: Model B;
   append #9; span → fail-fast; #6 superseded; #8 retired; #7 kept.)

10. **Unit-only test selection on analyzed targets** — the resolved target test project runs as
    `dotnet test <TestProject> --collect:"XPlat Code Coverage" --filter "type!=IntegrationTests"
    --results-directory coverage`, so **only the target's UNIT tests** produce coverage. A target test
    is a unit test unless it is tagged `[Trait("type", "IntegrationTests")]`; tests tagged `type` =
    `UnitTests` or `Unit`, **and untagged tests**, all run. The **exclusion form is mandatory and
    load-bearing:** VSTest treats an **absent** `type` property as satisfying `!=` any value, so
    **untagged target tests are INCLUDED** (an inclusion form such as `type=UnitTests` would WRONGLY
    drop untagged tests). The single adjustable source of truth is `private const
    CoverageRunner.UnitTestFilter` (least-privilege — never `internal`, golden rule #9); narrowing
    "unit" to strictly `{UnitTests, absent}` is the one-line, still-exclusion-form append
    `"type!=IntegrationTests&type!=Unit"` (untagged stays included). This **mirrors mutate4csharp**
    **minus** its mutation-cycle-only `Category!=no-mutate` clause — which excludes tests that
    recursively start `dotnet`/coverage per mutant run and has **no crap4csharp analog** (coverage runs
    once, no mutant loop), hence **out of scope**. **Knowing departure from crap4java parity:** crap4java
    ran **ALL** of a resolved module's tests (`mvn test`, no unit/integration split); #10 changes WHICH
    target tests execute (hence which coverage is produced), not crap4csharp's own analysis logic, and
    is faithful to intent (exercise the code unit under analysis with its unit tests). A pathological
    target whose entire suite is `type=IntegrationTests` yields no coverage → the existing report
    fail-fasts fire (`No coverage report was produced` / `contained no coverage data`, exit 1) — **no
    new exit code**. (Ruled by Mr. Das: reverses the earlier "all tests / no `--filter`" stance; carry
    ONLY the `type!=IntegrationTests` clause.)

11. **Async/iterator coverage attribution (C#-specific)** — coverlet records a source async/iterator
    method's executable lines under its compiler-generated state-machine nested type
    `Outer/<Method>d__N` (on that type's `MoveNext`), NOT on the source method's own lines. crap4csharp
    now DETECTS a state-machine class — a nested last segment `<Method>d__N` with a **non-empty**
    `Method` (both `async` and `yield` iterators use the `d__` shape; the empty-bracket `<>c` /
    `<>c__DisplayClassN` lambda display classes are deliberately NOT matched) — demangles it to the
    enclosing type + `Method`, and **attributes the state machine's `MoveNext` line coverage** (MoveNext
    only; the synthetic `.ctor`/`SetStateMachine`/`Dispose` siblings carry no user source and are
    ignored) to the key `NormalizeTypeName(enclosingType) + "#" + Method + ":" + minMoveNextLine`.
    `CrapAnalyzer`'s existing exact->nearest-line lookup then resolves the source method (declared a few
    lines above its body) to this key with **no analyzer change**; overloaded async/iterator methods
    (multiple `<M>d__N`) disambiguate by nearest MoveNext min-line, exactly as regular overloads do.
    **Observable behavior:** async and iterator methods that ARE tested now report their REAL coverage
    and CRAP instead of `N/A`, so they participate in the exit-2 threshold gate (previously they always
    escaped it). Analog of departure #2 (a C#-ecosystem enrichment of the metric's inputs, faithful to
    crap4java's *intent* of scoring the human-authored method). Implements requirement-intent R3.
    **Scope is surgical:** ONLY `d__N` state machines (async + iterators). Lambdas (`<M>b__N`) and local
    functions (`<M>g__...|N`) remain compiler-generated-skipped — their coverage-consistency question is
    a known related gap, DEFERRED (finding #3). (Ruled by Mr. Das: FIX; attribute `<M>d__N` -> source
    async/iterator method; do NOT fold in lambdas/local functions.)

12. **Per-file coverage-key basename (C#-specific; no Java analog)** — the frozen reciprocal coverage
    key gains a source-file **basename** segment: `TypeName#method#Path.GetFileName(file):line` (the
    `:line` MIN-`@number` stays LAST for the reciprocal nearest-line lookup). A C# **partial class split
    across files** can declare overloads — and, post-T23, compiler-generated members — at overlapping
    min-lines; coverlet emits one `<class>` per file, so the pre-T24 `TypeName#method:line` key collided
    and the overloads borrowed each other's coverage through the nearest-line scan. The basename
    segregates the per-file entries. `NormalizeTypeName` and the frozen `TypeName` form are **unchanged**
    (the basename is a new, separate segment, not part of `TypeName`); both producers (`ReadClassMethods`
    + the T21 `ReadStateMachineMoveNext`) and the consumer (`CrapAnalyzer`) thread it identically, and a
    missing/empty `filename` yields an unmatchable `Type#method#:line` key → per-method `N/A`, never a
    mis-attribution. **Observable behavior:** overloads of a split partial class now report their OWN
    file's coverage instead of borrowing a sibling's. A C#-ecosystem correctness fix with no crap4java
    counterpart (Java has no partial classes). See the **T24 register** (D-T24a–g). (Ruled by Mr. Das:
    key format `TypeName#method#basename:line`.)

13. **Compiler-generated member coverage attribution (C#-specific)** — generalizes departure #11
    (which attributes only async/iterator `<Method>d__N` state machines) to **every** compiler-generated
    member the C# compiler lowers off a source method: lambdas (`<M>b__N`, hosted on the `<>c` static
    cache / `<>c__DisplayClassN` capturing closure), local functions (`<M>g__L|N`, hosted on the real
    class or, when captured, a display class), and async-locals (`<<M>g__...>d__N` / `<<M>b__...>d__N` —
    an async local function/lambda lowered onto its own state machine). crap4csharp DEMANGLES the source
    method `M` from each member name (`^<([^<>]+)>[bg]__`; a user method never contains angle brackets),
    ATTRIBUTES the member's own `<line>` coverage to
    `NormalizeTypeName(enclosingType) + "#" + M + "#" + basename + ":" + line`, and **UNIONs** it into
    that source method's coverage. Every physical source line belongs to exactly ONE coverlet `<method>`,
    so the body's lines and the synthetic member's lines are DISJOINT and summing `(missed, covered)` is
    a set union, never a double-count. Attribution is nearest-body (the same ordinal min-line scan
    `CrapAnalyzer` already runs for overloads), so a member folds into ITS OWN same-file overload and
    distinct overloads are never blended. A member `filename` that does not match the source method's
    file yields an unmatchable key → the method stays per-method `N/A` (departure #1 family, D-T24d) — a
    SAFE under-report, never a false pass. **Observable behavior:** never-invoked lambdas and local
    functions now report their REAL (possibly 0%) coverage and CRAP instead of `N/A`, so they participate
    in the exit-2 threshold gate (closes finding #3's false pass — a never-invoked local function now
    scores CRAP 2.0 at CC 1 / 0% rather than escaping it). Analog of departure #2 (a C#-ecosystem
    enrichment of the metric's inputs, faithful to crap4java's *intent* of scoring the human-authored
    method). Supersedes departure #11's now-stale "lambdas/local functions remain skipped — finding #3
    deferred" clause; #11's `d__N` async/iterator attribution is UNCHANGED — #13 only ADDS the other
    member shapes. See the **T23 register** (D-T23a–e). (Ruled by Mr. Das: attribute ALL
    compiler-generated members to `NormalizeTypeName(enclosing)#SourceMethod#basename:line`; nearest-body
    union; safe-`N/A` on filename mismatch.)

## Cyclomatic complexity — authoritative node set

Base `CC = 1`. Walk the method `Body`/`ExpressionBody`; **descend into lambdas + local functions**;
**prune at nested type/enum declarations**. `+1` for each occurrence of:

- **Faithful (ported from crap4java):** `IfStatement`; `For`; `ForEach` (+ `ForEachVariable`);
  `While`; `Do`; `CatchClause`; `ConditionalExpression` (`?:`); `CaseSwitchLabel`;
  `CasePatternSwitchLabel`; `DefaultSwitchLabel`; `&&`; `||`.
- **Modern additions (approved):** `SwitchExpressionArm`; `??` (`CoalesceExpression`); `??=`
  (`CoalesceAssignmentExpression`); pattern `and` (`AndPattern`); pattern `or` (`OrPattern`);
  pattern `not` (`UnaryPattern`); `CatchFilterClause`; **every `when` guard** (`WhenClause` — all-when).
- **Not counted:** bare `else`; `try`/`finally`; jump statements (`return`/`break`/`continue`/`goto`/
  `throw`); `?.`/`?[]`; `is`-pattern without a combinator; bitwise `&`/`|`/`^`.

## Idiomatic policy

- **Adopt-now baseline (I-series):** nullable enable; `record` value types; `InvariantCulture` +
  `"\n"`; `XDocument` in the coverage adapter; async stdout drain; `IReadOnlyList` returns.
- **Greenlit follow-ons (after the faithful baseline is green):** O1 — `ExitCode` enum; O2 —
  subprocess timeout + cancellation.
- **Deferred (Mr. Das to decide later):** O3 — typed coverage lookup (reshapes parity tests); O4 —
  fraction-coverage (spec §10 wording); O5 — globbing discovery; O6 — `[Theory]` consolidation.
- **Declined:** `System.CommandLine` (breaks CLI contract), off-the-shelf CC metrics (breaks
  oracles), DI container, micro-optimizations.

## Test-parity note (fail-fast)

The single knowing parity break: ~4 `CliApplication`/`Program` coverage-path tests change from
"warn + `N/A` + exit 0" to fail-fast; **+2** new fail-fast tests. All other assertions are identical.

Separately, **departure #7 (resolve-once)** adapts crap4java's module-grouping tests to a single
resolve + single coverage run (or drops them) — no module-group-loop tests are ported (T11/T12/T14).

**T17/S5 (Model B, departures #9/#10).** `ModuleRootResolverTests` is renamed/reshaped to
`OwningProjectResolverTests` (bounded nearest-`.csproj`; the `.sln`-precedence cases are DROPPED — `.sln`
is no longer a marker); a new `TestProjectResolverTests` covers the `.Tests`/`.UnitTests` naming gate,
transitive/cycle-safe `ProjectReference` reachability, `bin`/`obj` + bounded-scope exclusion, and the
non-throwing missing/**malformed-existing** `.csproj` discipline (FLAG-1); `CliApplicationTests` gains
on-disk owning+test `.csproj` fixtures plus **+3** absence tests (`No owning .csproj`, `No test project`,
`span multiple projects`). `CoverageRunnerTests` is retargeted to the
`GenerateCoverage(testProject, coverageBaseDirectory)` signature and pins the unit-only `--filter` **token
pair**; the new `CoverageFilterBehaviorTests` spawns a REAL `dotnet test --filter "type!=IntegrationTests"`
to prove untagged RUNS + `type=IntegrationTests` EXCLUDED (departure #10). crap4java's module-grouping /
all-tests parity tests remain **ADAPTED** (resolve-once) or **DROPPED** — none assert running every module
test after #10.

## Environment adaptations (from nucleus)

- **Dropped as N/A for a CLI:** web-app liveness watch / `run-app` / `dev.ps1` / `session-startup`,
  dev-cert fix, bicep lint, dev secrets, business NuGet packages (MediatR/AutoMapper/etc.).
- **Kept & adapted:** `.editorconfig`; analyzers (NetAnalyzers, StyleCop, BannedApiAnalyzers);
  warnings-as-errors in Release; `global.json`; `nuget.config` (nuget.org only); the agentic loop
  files; `meta-design` + feature template; `retrospective` + `build-test` skills.
- **New:** GitHub Actions CI (nucleus used Azure DevOps).

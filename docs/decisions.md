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
| Coverage key | Roslyn enclosing-type **FQN** per method | C# allows many types per file; filename keys mis-match Cobertura |
| Module root | nearest **`.sln`** (fallback `.csproj` → project root) | so `dotnet test` actually runs tests |

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

- **Frozen reciprocal contract — CONSUMER end discharged (T11).** `CrapAnalyzer` builds its exact/
  nearest lookup keys as `typeName + "#" + methodName + ":" + line` from `MethodDescriptor.TypeName`
  (T9) and matches them byte-for-byte against T10's emitted map keys — the trailing `":"` is
  load-bearing (stops `alpha` matching `alphaBeta`). Any drift on either producer silently collapses a
  method's coverage to `N/A`. Line formatting uses `InvariantCulture` (departure #3) so keys round-trip.
  Pinned end-to-end by `ResolvesCoverageThroughFrozenReciprocalKeyEndToEnd` (a nested generic
  ``Demo.Outer`1.Inner`2`` resolving to a real 100%, not `N/A`). The T9⇄T10⇄T11 triad is now closed on
  all three ends.

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

- **D-T12e — `ResultsDirectoryName` is a frozen reciprocal contract (single source of truth).** The
  results-directory name `"coverage"` is declared **once** as `public const
  CoverageRunner.ResultsDirectoryName` and referenced by **both** the writer (`CoverageRunner`, which
  passes it to `--results-directory`) and the reader (`CoverageReportLocator`, which `Path.Combine`s it
  under `projectRoot`). Same lesson as the frozen `TypeName` contract: writer and reader must agree
  byte-for-byte or the locator silently finds nothing (a false fail-fast). Do not inline the literal
  `"coverage"` on either side and do not split it into two constants.

- **D-T12f — ordinal-first single-pick is a LOAD-BEARING determinism invariant.** When more than one
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
  departure #7; `InvalidOperationException` with the byte-identical message
  `"Coverage command failed with exit N"` (D-T12d) is the already-ratified `IllegalStateException`
  analog (C1, as in `CoberturaCoverageParser`). The T12/T14 fail-fast boundary is unchanged: T12
  **throws** on command failure (faithful) and **returns `null`** when the report is absent (the new
  locate signal); every exit-code decision (departure #1) stays in T14.

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
6. **Module-root walk unbounded (B1)** — `ModuleRootResolver.Resolve(startDirectory)` takes a single
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

8. **Single-pick coverage report (ordinal-first)** — `CoverageReportLocator` (T12) deterministically
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

## Environment adaptations (from nucleus)

- **Dropped as N/A for a CLI:** web-app liveness watch / `run-app` / `dev.ps1` / `session-startup`,
  dev-cert fix, bicep lint, dev secrets, business NuGet packages (MediatR/AutoMapper/etc.).
- **Kept & adapted:** `.editorconfig`; analyzers (NetAnalyzers, StyleCop, BannedApiAnalyzers);
  warnings-as-errors in Release; `global.json`; `nuget.config` (nuget.org only); the agentic loop
  files; `meta-design` + feature template; `retrospective` + `build-test` skills.
- **New:** GitHub Actions CI (nucleus used Azure DevOps).

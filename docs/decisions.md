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
  change one producer without the other.

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

## Environment adaptations (from nucleus)

- **Dropped as N/A for a CLI:** web-app liveness watch / `run-app` / `dev.ps1` / `session-startup`,
  dev-cert fix, bicep lint, dev secrets, business NuGet packages (MediatR/AutoMapper/etc.).
- **Kept & adapted:** `.editorconfig`; analyzers (NetAnalyzers, StyleCop, BannedApiAnalyzers);
  warnings-as-errors in Release; `global.json`; `nuget.config` (nuget.org only); the agentic loop
  files; `meta-design` + feature template; `retrospective` + `build-test` skills.
- **New:** GitHub Actions CI (nucleus used Azure DevOps).

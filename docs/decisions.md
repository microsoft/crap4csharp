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

## Deliberate departures from crap4java (approved by Mr. Das)

1. **Fail fast** — when a module produces no coverage / runs no tests, exit non-zero (`1`) with a
   greppable stderr message, instead of the Java warn + `N/A` + exit 0. Applies at the **module/run**
   level only; **per-method `N/A` is unchanged** (a method absent from a populated report still yields
   an `N/A` row).
2. **Richer cyclomatic complexity** — augmented Roslyn node set (below). CC is therefore **not
   numerically comparable** to `crap4java` on code using the modern constructs.
3. **Determinism/idiom** — nullable reference types enabled; `InvariantCulture` + explicit `"\n"` in
   the report.

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

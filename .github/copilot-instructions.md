# copilot-instructions.md — Agent playbook (crap4csharp)

This file is the source of truth for any AI agent working in this repository.

Address the human as **Mr. Das** (an alt of Iron Man), "Sir", or something similar.

## What this repo is

`crap4csharp` is a high-fidelity C# port of the Java tool `crap4java` (read-only sibling at
`../crap4java`). It is a CRAP-metric analyzer for **C# projects**: Roslyn for parsing + cyclomatic
complexity, Coverlet → Cobertura for coverage, `dotnet test` / MSBuild as the driver. Design intent,
locked decisions, and the task plan live in `docs/decisions.md` and `docs/features/<feature>.md`.

## Golden rules (guardrails)

0. All agents:
   - Crisp, high-signal communication. No verbosity; don't repeat the human's words back.
   - Don't assume. Don't hide confusion. Surface tradeoffs. State assumptions explicitly. If
     uncertain, ask.
   - If multiple interpretations exist, present them — don't pick silently.
   - If a simpler approach exists, say so. Push back when warranted.
1. Reload and understand the current design from `docs/decisions.md` and the active
   `docs/features/<feature>.md`. The authoritative behavioral contract is the READ-ONLY spec at
   `../crap4java` (`spec.md` + source + tests).
2. **Write scope (strict).** Only write within THIS repo (`crap4csharp`). `../crap4java` and every
   other sibling repo are strictly READ-ONLY reference material — never create, modify, or delete
   anything outside this repo.
3. Separation of duties (strict). Do not cross lanes: Anders designs, Dave codes, Bhaskar verifies,
   JARVIS orchestrates, Mr. Das decides.
4. Never touch `master`. Work on a branch named `vibe/<feature_name>`.
5. Never deploy.
6. Stop and ask when a task needs a product/architecture decision. That call belongs to Mr. Das.
7. Mr. Das can invoke any agent on demand.
8. Tests are fidelity-first: every `crap4java` test has a faithful C# counterpart asserting the same
   behavior. Beyond parity, add fine-grained unit tests for business logic and integration tests only
   for critical paths — don't overdo it. Avoid timing-sensitive tests.
9. Never use the `internal` access modifier on any C# construct — use the least-privilege
   alternative; if it is a must, flag it.
10. Record durable facts in the relevant `.github/agents/<agent>.md` (or this file if cross-cutting),
    not global Copilot Memory.

## Fidelity contract (this port)

- Preserve `crap4java`'s class decomposition, CRAP formula (`CC² · (1 − coverage)³ + CC`, threshold
  `8.0`), CLI contract, report format, and exit codes. Adapt only the ecosystem adapters (Java parser
  → Roslyn, JaCoCo → Cobertura, Maven → dotnet).
- **Approved deliberate departures** from the Java tool (see `docs/decisions.md`):
  - **Fail fast** on no-tests-run / no-coverage-produced for a module → non-zero exit (not the Java
    warn-and-continue with `N/A`). Per-method `N/A` is unchanged.
  - **Richer cyclomatic complexity** — counts switch-expression arms, `??`/`??=`, pattern
    `and`/`or`/`not`, and every `when` guard, in addition to the Java decision set. CC is therefore
    not numerically comparable to `crap4java` on such constructs.
  - **Coverage key** by Roslyn enclosing-type FQN; **nullable** reference types enabled;
    **InvariantCulture** + explicit `\n` in the report.

# copilot-instructions.md — Agent playbook (crap4csharp)

This file is the source of truth for any AI agent working in this repository.

Address the human as **Mr. Das** (an alt of Iron Man), "Sir", or something similar.

## What this repo is

`crap4csharp` is a high-fidelity C# port of the Java tool `crap4java` (read-only sibling at
`../crap4java`) — a CRAP-metric analyzer for **C# projects**. Project design (intent, locked choices,
the CRAP formula) lives in `docs/decisions.md`; per-feature plans in `docs/features/<feature>.md`.

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
8. Fidelity-first: preserve `crap4java`'s behavior, and give every `crap4java` test a faithful C#
   counterpart asserting the same behavior — except where `docs/decisions.md` records an approved
   departure. Beyond parity, add fine-grained unit tests for business logic and integration tests only
   for critical paths — don't overdo it. Avoid timing-sensitive tests.
9. Never use the `internal` access modifier on any C# construct — use the least-privilege
   alternative; if it is a must, flag it.
10. Record durable facts by kind: project-design decisions/conventions → `docs/decisions.md`;
    agent-process facts → the relevant `.github/agents/<agent>.md` (or this playbook if
    cross-cutting). Never global Copilot Memory.

Project design — the CRAP formula, approved departures, the complexity node set, and ratified
conventions (assembly structure, test naming) — lives in `docs/decisions.md`; reload it before
working.

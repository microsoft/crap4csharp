---
name: retrospective
description: Periodic cross-feature governance retrospective. Every ~5 completed features, distill durable learnings from docs/agents/skills/commits into all-agent guardrails + per-agent notes, then apply a minimal governance update. Count-based, not time-based; JARVIS reminds the human when it's due.
---

A recurring, minimal-footprint review that turns accumulated delivery experience into durable
guardrails. Event/count-based, **not** time-based.

## When

After a feature completes, JARVIS compares the current feature-doc count to the `features=N` value on
the last `## Log` line below; when it has grown by **≥ 5**, JARVIS **reminds Mr. Das** to run this
skill before the next feature.

Feature-doc count (excludes the template):

    (Get-ChildItem docs/features/*.md | ? { $_.Name -ne 'TASK_FILE_TEMPLATE.md' }).Count

## Sources (read-only)

`docs/features/*.md` (especially post-review / post-test-fix notes), `docs/decisions.md`,
`.github/copilot-instructions.md`, `.github/agents/*.md`, `.github/skills/*`, and `git log` / commit
diffs since the last Log entry.

## Produce (Anders)

Distill cross-cutting, durable lessons (skip feature specifics; verify claims against docs/commits)
into two parts, prioritising high-signal, recurring issues:

- **(A) All-agent guardrails** — candidate golden-rule additions/refinements for `copilot-instructions.md`.
- **(B) Per-agent learnings** — short sections for Anders / Dave / Bhaskar / JARVIS.

Main thing: don't overdo this.

## Then (minimal governance update)

1. Architect proposes exact redlines. Keep edits minimal.
2. **Mr. Das approves** any guardrail/golden-rule change (guardrails are the human's call).
3. Coder applies the approved set: promote strong learnings to golden rules in
   `copilot-instructions.md`, add per-agent notes to the agent files, and fix stale cross-references.
   Cite guardrails by **concept/name**, not positional number (numbers rot on insert/reorder).

## Log (append one line per run)

    - YYYY-MM-DD · features=N · <1-line summary of what changed>

- 2026-07-26 · features=0 · Created the retrospective process (ported from nucleus); not yet run.

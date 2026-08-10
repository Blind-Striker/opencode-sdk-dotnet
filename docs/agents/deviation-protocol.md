# Deviation Protocol

Date: 2026-08-10

> *"No plan survives first contact with the enemy."* — Helmuth von Moltke

What to do when implementation reality contradicts a spec, a plan, or a sealed decision.
This is the process rendering of the repo's engineering default — fail loudly rather than
guess; silent fallbacks only as explicitly recorded tolerances (`AGENTS.md`). An executing
agent never silently codes around a contradiction, and never silently "fixes" a sealed
design back toward convention.

## The ladder

Classify the deviation before acting. When in doubt between two levels, take the higher.

### Level 0 — Implementation detail

The spec is silent, or the question sits below spec resolution (a private helper's name,
test internals, a local refactor). **Decide and move on.** Note it in the PR description
only if a reviewer might wonder.

### Level 1 — Recorded fallback

The spec's mechanism fails, but the spec or an ADR records a fallback or reversal trigger
for exactly this case (e.g. the generator spec §3.3 two-condition entry fallback).
**Execute the recorded fallback.** Pre-authorized — no stop needed, but the slice PR must
state that the trigger fired, and the documents the fallback touches are corrected in the
same change. Add a research-log entry when the evidence is worth keeping.

### Level 2 — Spec correction

Reality contradicts a sealed spec claim and no fallback is recorded: upstream behaves
differently, an API does not exist, a verified count is wrong, a design detail cannot be
built as written. **Stop the affected task** (neighboring tasks may continue if
untouched). Capture the evidence — commands, output, source references. Propose the
correction to the maintainer; on approval, the spec/plan is corrected in place and the
task resumes. Findings worth keeping go to the research log.

### Level 3 — Locked-decision challenge

New evidence contradicts an `AGENTS.md` locked decision or an ADR. **Stop the slice.**
Bring the evidence to the maintainer; the decision is re-litigated in a research/grill
pass — from mechanisms and sources, per the working agreements. The outcome is either a
superseding/amended ADR or a recorded confirmation. Locked decisions are reopened by
evidence, never by convenience.

## Rules that apply at every level

- **Escalation path:** a subagent reports the contradiction to its orchestrating session;
  the session classifies the level and, at level 2+, stops and involves the maintainer.
  Subagents never self-resolve level 2+.
- **A deviation without a trace is a defect**, even when the deviation itself was right.
- **The docs pass is part of the deviation, not follow-up work:** whichever document
  carried the contradicted claim is corrected in the same change (Documentation Hygiene).

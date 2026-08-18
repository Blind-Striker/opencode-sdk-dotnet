# Domain Docs

Date: 2026-08-18

How the engineering skills should consume this repo's domain documentation when exploring the codebase.

## Before exploring, read these

- **`CONTEXT.md`** at the repo root — the domain glossary.
- **`docs/architecture/`** — read the current canon for the area you are about to work in.
- **`docs/adr/`** — read ADRs that touch the area you're about to work in.

Note: `external/opencode` carries upstream's own `CONTEXT.md`; ours lives at the repo root — no
clash.

## Use the glossary's vocabulary

When your output names a domain concept (in an issue title, a refactor proposal, a hypothesis, a
test name), use the term as defined in `CONTEXT.md`. Don't drift to synonyms the glossary
explicitly avoids.

If the concept you need isn't in the glossary yet, that's a signal — either you're inventing
language the project doesn't use (reconsider) or there's a real gap (note it for the next
domain-modeling session).

## Flag ADR conflicts

If your output contradicts an existing ADR, surface it explicitly rather than silently
overriding:

> _Contradicts ADR-0013 (the pinned OpenAPI document is the sole protocol input) — but worth
> reopening because…_

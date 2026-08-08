# Architecture Decision Records

Date: 2026-08-08

Decision records for this repository. Each ADR is a standalone file named `NNNN-slug.md`,
numbered sequentially — scan the directory for the highest number and increment. No index is
maintained here; the directory listing is the index.

Layering: `AGENTS.md` carries the normative decision statements (the operating contract) and
links here; an ADR is the canonical record of one decision — context, the decision, the why,
and reversal triggers where they exist; dated evidence stays in `docs/research/` and is cited,
not restated.

## When to write an ADR

All three must hold:

1. **Hard to reverse** — changing course later carries real cost.
2. **Surprising without context** — a future reader would wonder "why did they do it this way?"
3. **A real trade-off** — genuine alternatives existed and one was chosen for specific reasons.

If any leg is missing, skip the ADR: conventions and process rules belong in `AGENTS.md` (or
`docs/agents/`), dated findings in `docs/research/`.

## Format

    # {Short title of the decision}

    Date: YYYY-MM-DD

    {1-3 sentences: context, decision, why.}

That is the whole required format — an ADR can be a single paragraph. The value is in recording
*that* a decision was made and *why*, not in filling out sections. Optional sections, only when
they add real value:

- **Status** frontmatter (`proposed | accepted | deprecated | superseded by ADR-NNNN`) — when
  a decision is revisited.
- **Considered Options** — when the rejected alternatives are worth remembering.
- **Consequences** — when non-obvious downstream effects need calling out.

# Generate both API surfaces of the pinned 1.x spec

Date: 2026-08-08

The pinned v1.18.15 spec carries two surfaces: 127 legacy operations and 61 transitional
`v2.*`-prefixed operations. We generate both — superseding the earlier v2-only decision —
because the modern block does not yet cover the product's full capability (upstream's own TUI
still runs 91 legacy vs 18 modern call sites) and the MCP-server goal needs all of it today.
Public naming strips the `v2.` prefix and never bakes "V2" into type or client names — the
modern names are upstream's newest naming layer, so there is no rename wave to absorb
(`docs/research/10-v2-to-2.0-operation-mapping.md` corrected doc 09's direction: the "2.0
branch" spec is an April-2026 ancestor of today's root surface, not the next major). The
legacy area is deleted at a major release of ours when upstream ships its next major; that
major's shape and date are unverified — the direction rests on live signals (TUI migration
to the modern surface, in-code deprecation markers, the protocol-package extraction).
Evidence: `docs/research/09-upstream-v1v2.md`, `docs/research/10-v2-to-2.0-operation-mapping.md`.

## Consequences

- **Name collisions are resolved by structural separation.** With the prefix stripped, 16 of
  61 modern operation names collide with legacy ones (`session.get`, `session.prompt`,
  `event.subscribe`, all `pty.*`, …). The modern surface takes the unmarked names; the legacy
  surface lives behind an explicitly legacy-marked sub-surface (exact API shape decided in the
  API design session). At our 2.0-absorbing major the legacy area is deleted wholesale without
  touching modern names.
- **Legacy testing is consumer-driven, not uniformly best-effort.** Deep integration testing
  targets the modern surface plus every legacy operation the MCP server consumes — a set
  derived mechanically from the in-repo MCP project's SDK calls (ADR-0006). The remaining
  legacy surface is best-effort.

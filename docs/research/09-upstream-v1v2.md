# Upstream v1/v2: product version vs HTTP API surface

Date: 2026-08-08

> Codegen-spike follow-up. Question: what exactly is "v2" — a product version, an API surface,
> or both — and does building against the `v2.*` surface in our pinned v1.18.15 spec carry
> forward to opencode 2.0? Sources: the submodule read-only at v1.18.15, remote branch `2.0`
> of `sst/opencode` (via raw.githubusercontent.com, retrieved 2026-08-08), GitHub Releases
> API, npm registry dist-tags.

## The two version axes

**Product version:** opencode 1.18.15 is the current stable (tagged from `dev`, npm `latest`).
opencode 2.0 is the next major, developed on a separate `2.0` branch and shipped as
timestamped beta builds (npm `beta: 0.0.0-beta-202608081103`, daily cadence; no `v2*` git
tags yet; monorepo package.json versions are stale — CI stamps real ones).

**API surface:** every opencode serves an HTTP API described by its `openapi.json`. The
v1.18.15 document carries TWO surfaces — 127 legacy un-prefixed operations at root paths plus
61 `v2.*`-prefixed operations mounted under `/api/*` (3 under `/experimental/*`). The 2.0
document carries ONE surface and it is neither of those labels:

| | v1.18.15 (pinned) | 2.0 branch |
|---|---|---|
| OpenAPI | 3.1.0 | 3.1.1 |
| Paths / schemas | 162 / 472 | 94 / 163 |
| Operations | 188 (127 legacy + 61 `v2.*`) | **112, zero `v2.*` prefixes** |
| Route mount | legacy at root + v2 under `/api/*` | everything at root (`/global/*`, `/session/*`, …) |

2.0 operationIds are plain dotted names (`global.health`, `session.get`, `tui.*`, `mcp.*`,
`pty.*`, `provider.*`, `project.*`, `experimental.*`). Top path groups: session 27,
experimental 16, tui 13, mcp 8, global 7, pty 6.

## The bridge: names break, the family carries

- Only **15 of 61** v1.18 `v2.*` operationIds (prefix stripped) exist verbatim in 2.0;
  **97 of 112** 2.0 operations are not in the v1.18 v2-set. Renames are systematic
  (`v2.health.get` → `global.health`); some ops are dropped or reshaped.
- Same contract family underneath: v1.18.15 already contains the 2.0-shaped Effect HttpApi
  (`packages/opencode/src/server/routes/instance/httpapi/groups/`, 20 groups incl. `global`,
  `project`, `sync`, `workspace`); the openapi `v2.*` block is a transitional **projection**
  of that contract mounted under `/api` with a `v2.` prefix.
- Dialect shift the generator must handle: 2.0 keeps the discriminator-free `anyOf` dialect
  (62×, 0 discriminator; `Part` union shape-identical, same 12 variants), but literal markers
  moved from single-value `enum` (513× at v1.18) to **`const` (138×)**, and event schemas got
  dotted names (`Event.session.diff`) that need C# identifier mangling. Still 0 type-arrays.

## Legacy-surface deprecation signals

No formal public statement (opencode.ai/docs has no 2.0/migration page; the 2.0 README says
nothing beyond "Desktop App (BETA)"). The evidence is in code and release notes: a
`deprecated: true` in `httpapi/groups/session.ts`; "Keep deprecated `api.command` working for
v1 plugins; **remove in v2**" (`src/plugin/tui/runtime.ts`); v1.18.12 release note "Skipped
legacy config reads against v2 servers" (PR #40211) — and the 2.0 spec itself, which drops
the legacy operations outright.

## What upstream's own clients use at v1.18.15

The published `@opencode-ai/sdk` ships BOTH generations (`src/gen` legacy client +
`src/v2/gen` hey-api client). The TUI is mid-migration: **91** legacy `sdk.client.*` call
sites vs **18** `sdk.client.v2.*` (`packages/tui/src`, counted 2026-08-08); desktop fixes in
release notes already exercise v2 servers. The practical consequence: at 1.18.x the modern
surface does not yet cover the product's full capability — the legacy surface still carries
most of the TUI.

## Consequences for this SDK (decisions recorded in `AGENTS.md`)

1. Both surfaces of the pinned 1.x spec are generated: the MCP-server goal needs today's full
   capability, and the modern block alone does not provide it. Deep integration testing
   targets the modern surface; legacy is best-effort. (2026-08-13: ADR-0005 revised in place
   — the SDK targets the v2 protocol surface only and the legacy surface is never built;
   current v2-line state: `15-opencode-v2-platform.md`.)
2. Public naming must not bake in the transitional `v2.` prefix — it does not exist in 2.0;
   the 2.0 rename wave is absorbed at a major release (evolve/deprecate on the evidence then).
3. Generator: parse `const` alongside single-value `enum`, and mangle dotted schema names.

## UNVERIFIED / open

- Whether `pty.connect`-style WebSocket endpoints appear in the 2.0 spec (pty group has 6
  ops; not inspected individually).
- Whether a migration guide exists outside the repo (blog/Discord) — none found in
  docs/README; GitHub issue/discussion search failed this session (API 422), so maintainer
  statements there are unchecked. (2026-08-13: official v2 documentation now exists at
  opencode.ai/v2/docs.)
- The exact 61 → 112 operation rename mapping (spot-checked only; a full side-by-side diff
  belongs to the API design session).
- How long 1.x releases will keep publishing the `v2.*` `/api` projection.

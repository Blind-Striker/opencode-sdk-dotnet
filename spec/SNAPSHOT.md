# OpenAPI Snapshot

Date: 2026-08-13

`openapi.json` is a pinned copy of the upstream opencode OpenAPI 3.1 document — the v2
protocol surface (ADR-0005). The SDK is built against this snapshot, never against a live
branch; the `v2` branch moves daily and refreshes are deliberate (milestone boundaries).

| Fact | Value |
|---|---|
| Upstream file | `packages/protocol/openapi.json` |
| Upstream branch | `v2` (active successor line; no release tags yet) |
| Commit | `a6a712a3ac72248c9b2f2f883e752e6e18ef8c40` |
| Upstream product channel | `opencode2` — npm `@opencode-ai/cli@next` (pre-release) |

Platform evidence for the v2 line: `docs/research/15-opencode-v2-platform.md`.

## Refresh procedure

1. Pick the target `v2`-branch commit and fetch `packages/protocol/openapi.json` at exactly
   that commit (read-only `git fetch` in the `external/opencode` submodule, or
   `raw.githubusercontent.com` pinned to the full commit SHA — never a branch ref).
2. Copy it over `spec/openapi.json`.
3. Update the table above (commit, channel) and the `Date:` line.
4. Run `generate`; resolve what it reports (wall admits, curation rows) and review the
   regenerated diff.

A dedicated spec-refresh tool is planned — see repo tooling in `docs/ROADMAP.md`.

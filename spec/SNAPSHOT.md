# OpenAPI Snapshot

Date: 2026-08-13

`openapi.json` is the accepted snapshot of the upstream opencode OpenAPI 3.1 document — the v2
protocol surface (ADR-0005). The SDK is built against this snapshot, never against a live
branch; the `v2` branch moves daily. Refresh policy is receipt-governed (ADR-0020): a refresh
consumes an exact commit, normally with an empty patch list, and temporary Restore patches may
repair upstream projection loss under review receipts. The manual procedure below remains
current until the `refresh-spec` synchronizer lands.

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
3. Move the `external/opencode` submodule pointer to the same commit in the same change
   (`git -C external/opencode fetch origin v2 && git -C external/opencode checkout
   <commit>`, then stage the gitlink) — the submodule checkout and this snapshot never
   diverge, so a fresh `git submodule update --init` always lands on the pinned commit.
4. Update the table above (commit, channel) and the `Date:` line.
5. Run `generate`; resolve what it reports (wall admits, curation rows) and review the
   regenerated diff.

A dedicated spec-refresh tool is planned — see repo tooling in `docs/ROADMAP.md`.

# Target the v2 protocol surface only

Date: 2026-08-13

opencode's active successor line (branch `v2`) serves exactly one HTTP surface — the protocol
contract behind the `v2.*`-prefixed operationIds — and has deleted the legacy root block
wholesale: `packages/opencode` no longer exists there, the server's handlers mirror the 30
protocol groups 1:1, the TUI runs on the protocol-derived client, and the product ships a
v1→v2 session-history migration. This SDK therefore generates the v2 protocol surface only;
the 1.x legacy surface is never built, and the generation target moves to the `v2` branch's
`packages/protocol/openapi.json`, pinned as a snapshot under `spec/` (the retarget executes
as the first task of the M1 callable-client arc). Public naming strips the `v2.` operationId
prefix and never bakes "V2" into type or client names (unchanged). This revises the
2026-08-08 both-surfaces decision in place — itself a revision of an earlier v2-only
position; the difference now is evidence, not taste: the both-surfaces premise ("the modern
block does not cover today's capability; the MCP-server goal needs all of it today") expired
when the v2 surface absorbed the capability gap (`mcp`, `config`, `vcs`, `project`, `shell`,
… are protocol groups now) and upstream's investment, distribution channels, and migration
tooling all converged on v2. Evidence: `docs/research/15-opencode-v2-platform.md`, research
log session 17; prior dated evidence: docs 09/10.

## Consequences

- The legacy hub, the legacy-marked sub-surface, the 16 stripped-name collisions, and
  consumer-driven legacy testing all disappear; milestone M5 shrinks to completing the
  generation profile over the single surface.
- Until upstream's v2 line reaches general availability the SDK targets a pre-release
  server (`opencode2`; npm `@opencode-ai/cli@next`) — accepted: the M-series timeline runs
  alongside upstream's stabilization, and the pinned-snapshot + fail-closed refresh
  machinery exists for exactly this churn.
- The spec pin is a snapshot of a moving branch: refreshes stay deliberate (M2 boundary and
  later), never HEAD-tracking.

# Assessment: the unofficial `opencode-mcp` server

Date: 2026-08-08

> Dated evidence and decision history, not current policy. Follow current canon through
> `AGENTS.md`.
>
> Research snapshot, 2026-08-08. `opencode-mcp@1.11.0` (npm), repo
> `AlaeddineMessadi/opencode-mcp`, MIT, ~110 stars. Paths relative to the
> `external/opencode-mcp` submodule.
>
> **Verdict: early but functional — not production-ready.** Fine for interactive local
> use pinned to an exact version; treat as a cautionary reference implementation for
> our own MCP server, not a foundation.

## What it is

A single-maintainer stdio MCP server bridging any MCP client (Claude Code, Cursor,
etc.) to opencode's headless server. Advertises **80 tools, 10 resources, 6 prompts** —
verified real, not aspirational (counts cross-check exactly between `docs/` and `src/`).

## Architecture

- Stdio MCP server, thin translation layer, no persistent state
  (`src/index.ts`: one `McpServer`, 11 `register*Tools` groups, `StdioServerTransport`).
- **Server lifecycle:** `ensureServer()` (`src/server-manager.ts`) probes
  `{baseUrl}/global/health`; if nothing answers, auto-starts opencode via the official
  SDK's `createOpencodeServer()` (child process spawn of `opencode serve`). Concurrent
  startups are coalesced per baseUrl. Disable with `OPENCODE_AUTO_SERVE=false`.
- **Request dispatch:** does *not* use the SDK properly — `src/client.ts:128` grabs the
  SDK's private HTTP client (`(this.api as any)._client`) and hand-dispatches
  GET/POST/PATCH/DELETE with its own retry loop, plus a hand-rolled SSE reader.
- **Multi-project:** forwards a validated absolute path as `x-opencode-directory` on
  every request.
- Uniform handler shape: zod schema → client call(s) → markdown formatting →
  `toolResult()` / `toolError()`.

## Maturity signals

**Positive:**

- Docs match implementation exactly (1,171 lines across 7 docs files; all 80 tools
  individually documented).
- ~4,600 test lines vs ~5,000 source lines; tests exercise handler behavior (assert on
  captured request bodies), not just registration.
- Above-average security hygiene: 3-layer secret redaction, CRLF/NUL header-injection
  guard, realpath-based symlink-escape check with a thought-through deny-list.
- zod validation on every tool; consistent MCP error semantics (81 `toolError` sites);
  error diagnosis mapping 8 failure classes to actionable tips.
- Genuinely good Keep-a-Changelog CHANGELOG with root-cause narratives.
- Rich MCP `instructions` block (tool taxonomy, troubleshooting) — most servers ship none.

**Negative (what blocks production use):**

- **Zero CI.** No `.github/` at all; the good test suite is never enforced.
- **The entire HTTP transport test suite is disabled** (`tests/client.test.ts:458`
  `describe.skip`, orphaned by the 1.11.0 SDK migration) — retry, auth headers, 429
  handling, directory-header propagation are all untested.
- **Depends on an SDK private field** (`_client`) behind a floating `^1.14.46` range —
  a patch release renaming it breaks all 80 tools, with no test to catch it.
- **Non-idempotent request replay:** the retry loop replays POST/PATCH/PUT/DELETE on
  transient errors; the fix (PR #17) sits unmerged.
- `zod` is an undeclared dependency (resolves transitively; breaks under pnpm/strict
  layouts).
- Tool annotations (`readOnlyHint`/`destructiveHint`) cover only 24/80 tools despite
  docs claiming "every tool" — clients can't tell `message_send` mutates state.
- Remote-server support is functionally broken (issue #14: a local `existsSync` check
  inside directory normalization contradicts documented remote support) — open, stalled.
- Response shapes are guessed defensively (`p.text ?? p.content`, probing 3 possible
  status keys) despite depending on the *typed* SDK; nine `as any` sites.
- Stale: built in a ~3.5-month burst (34 commits, 14 releases, 2026-02 → 2026-05-20),
  then ~2.5 months of silence; 2 open issues, 3 open PRs, bus factor 1.
- Assorted rot: coverage script can't run (missing dependency), version string
  triple-duplicated, changelog gaps, docs stale about the 1.11.0 lifecycle change, a
  shipped env var (`OPENCODE_SERVE_ARGS`) silently removed within ~10 days.

## Lessons for our MCP server

1. **Own the HTTP layer** — build the MCP server on our own SDK so there is no private
   API to reach into. This is the single biggest structural fix.
2. **Idempotency-aware retry** from day one: retry GETs; never blind-replay writes.
3. **Annotations as a build-time invariant** — if we claim every tool is annotated,
   enforce it with a test.
4. **CI from the first commit**; type-check tests; measured coverage.
5. Ideas worth stealing: health-probe + coalesced auto-start; `x-opencode-directory`
   fan-out; secret redaction before returning tool output; the rich `instructions`
   block; docs generated/verified against the actual tool registry.

# opencode architecture and the official SDK

> Research snapshot, 2026-08-08. opencode / `@opencode-ai/sdk` at **1.18.15**.
> Paths are relative to the `external/opencode` submodule.

## opencode is a client/server product

Running `opencode` starts a TUI **and** a local HTTP server; the TUI is a client of
that server. `opencode serve` starts the same API headless. From the docs
(`packages/web/src/content/docs/server.mdx`):

> "When you run `opencode` it starts a TUI and a server. Where the TUI is the client
> that talks to the server. The server exposes an OpenAPI 3.1 spec endpoint. This
> endpoint is also used to generate an SDK."

Runtime endpoints: `GET /doc` and `GET /openapi.json` serve the spec; auth is HTTP
basic via `OPENCODE_SERVER_PASSWORD`.

## The SDK generation pipeline (current, shipping)

```
Effect HttpApi definitions (code-first TypeScript)
  packages/protocol/src/groups/*.ts                     (shared endpoint contracts)
  packages/opencode/src/server/routes/instance/httpapi/ (instance/root groups, api.ts)
        │
        ▼  OpenApi.fromApi(PublicApi) + legacy-compat transform
  .../httpapi/public.ts        (~600-line matchLegacyOpenApi: normalizes Effect's
                                output for codegen — strips anyOf-null wrappers,
                                hand-annotates SSE responses, fixes query types)
        │
        ▼  `opencode generate` CLI  (packages/opencode/src/cli/cmd/generate.ts)
  packages/sdk/openapi.json    ★ committed artifact — OpenAPI 3.1.0, ~1 MB,
                                 162 paths, 472 schemas
        │
        ▼  @hey-api/openapi-ts  (NOT Stainless, NOT openapi-generator)
  packages/sdk/js/src/gen/**      (v1)
  packages/sdk/js/src/v2/gen/**   (v2 — the /api/* routes)
        │
        ▼  thin hand-written wrapper
  packages/sdk/js/src/client.ts / server.ts / index.ts
```

Maintenance properties worth copying:

- **Generated output is committed and CI-enforced.** `.github/workflows/generate.yml`
  regenerates on every push to `dev` and auto-commits `chore: generate`.
- **Post-generation patches are assertion-guarded** (`packages/sdk/js/script/build.ts`):
  every `.replace()` throws if it no longer matches, so upstream codegen changes fail
  loudly instead of silently drifting.
- **Contract exercise tests:** `test:httpapi` runs an exerciser with
  `--fail-on-missing --fail-on-skip` — every declared endpoint must be hit.

## The SDK is client + optional process launcher (separable)

- `createOpencodeClient()` — the pure typed HTTP client. Point it at any running
  server URL.
- `createOpencodeServer()` (`packages/sdk/js/src/server.ts`) — a convenience helper
  that **spawns `opencode serve --hostname=… --port=…` as a child process** via
  cross-spawn, scrapes stdout for `"opencode server listening on http://…"`, and
  returns `{ url, close() }`. It is *not* in-process; communication is normal HTTP
  over localhost.
- `createOpencode()` — both, returning `{ client, server }`.

Our .NET SDK should mirror this separation: pure client first, launcher helper as an
optional extra.

## API surface areas (from the spec / generated client)

v1 namespaces: `global, project, pty, config, tool, instance, path, vcs, session,
command, provider, find, file, app, mcp, lsp, formatter, tui, auth, event`.
v2 adds: `experimental, worktree, question, permission, part, sync, v2`.

Highlights:

- **Sessions** (largest area): list/create/get/update/delete, fork, abort, share,
  summarize, revert; v2 adds switchAgent, switchModel, compact, wait, context, history.
- **Prompting:** `POST /session/{id}/message` (sync + async variants), shell/command
  execution, structured JSON output via `format: { type: "json_schema", schema }`.
- **Events (SSE):** `GET /event`, `GET /global/event`, `GET /api/event`,
  `GET /api/session/{id}/event` — all `text/event-stream`.
- **Files & search:** read/list/status, text/file/symbol search; v2 `/api/fs/*`.
- **VCS:** status, diff (incl. raw), apply, init.
- **Config / providers / models / auth**, **permissions & questions** (reply/reject),
  **MCP management** (add/connect/disconnect + OAuth flow), **agents/skills/commands**.
- **PTY:** 15 ops; `pty.connect` is a **WebSocket upgrade** — upstream's own generic
  codegen excludes it (`omitEndpoints` in `packages/client/src/contract.ts`).
- **TUI remote control:** 13 ops (append/submit prompt, toasts, open dialogs) — this is
  how IDE plugins drive a running TUI.
- **Projects / worktrees / sync / experimental control-plane** (multi-instance bits are
  experimental).

## v1 vs v2 in one spec

Both surfaces live in the same `openapi.json`: v1 under `/session/...` etc., v2 under
`/api/...` with operation IDs prefixed `v2.` and distinct tags (`session` vs
`sessions`). Upstream's stability commitments (see doc 02) are phrased about the **v2**
routes.

## Every front-end goes through this API

Confirmed consumers of `@opencode-ai/sdk`: the TUI itself
(`packages/tui/src/context/sdk.tsx` imports `@opencode-ai/sdk/v2`), web UI
(`packages/app`), CLI, core, plugin API, Slack integration, enterprise. The VS Code
extension calls the same server with raw `fetch`. **The SDK is the single seam** —
which is why a .NET client generated from the same spec gets feature parity with the
TUI.

## Watch-outs for .NET codegen

1. **SSE endpoints** are hand-annotated `text/event-stream` — they need streaming
   handling, not plain deserialization.
2. **`pty.connect` / `v2.pty.connect`** are WebSocket upgrades; upstream excludes them
   from HTTP codegen — we probably should too.
3. **v1/v2 split** — decide which surface(s) to ship (see GOAL.md).
4. Multi-project targeting is done with an **`x-opencode-directory` header** on every
   request (this is how the unofficial MCP server fans out across projects).

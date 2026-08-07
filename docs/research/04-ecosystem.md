# Ecosystem: SDKs in other languages, and the protocol map (MCP vs ACP)

> Research snapshot, 2026-08-08.

## Official SDKs outside JS/TS: nominally present, practically abandoned

The opencode monorepo ships only the JS/TS SDK (`@opencode-ai/sdk`, 1.18.15 — see
doc 01). Separate repos under the `anomalyco` org hold Stainless-generated SDKs:

| Repo | Package | Last real commit | State |
|---|---|---|---|
| `anomalyco/opencode-sdk-python` | PyPI `opencode-ai` | **2025-08-27** (0.1.0-alpha.36) | Frozen ~1 year, still alpha |
| `anomalyco/opencode-sdk-go` | `github.com/sst/opencode-sdk-go` | **2025-12-18** (v0.19.2) | Frozen ~8 months |
| `anomalyco/opencode-sdk-js` | — | — | Superseded by the in-repo SDK |

All three have bot-only commit histories (`stainless-app[bot]`) — no human maintenance.
Meanwhile the product SDK is at 1.18.15 with the whole v2 surface, so Python/Go lag the
current API badly. Historical note: the Go SDK existed because the old TUI was written
in Go and consumed it; when the TUI moved to TypeScript/SolidJS the Go SDK lost its
only dogfooder and died. Nothing in the current monorepo references these repos, and
no Stainless configuration remains in it.

Community SDKs found (GitHub search): Rust (`longcipher/opencode-sdk-rs`), Elixir
(`UtkarshUsername/opencode-sdk-elixir`), PHP/Saloon (`artisan-build/opencode-sdk`),
Laravel, several small Python clients. None has significant traction.

**Conclusion: no .NET SDK exists, official or community. In practice the only
first-class, current SDK is JS/TS. This is the gap this project fills.**

Also worth noting: `sdks/` in the monorepo is misleadingly named — it contains only the
VS Code extension, which doesn't even use the SDK (raw `fetch` against the server).

## Protocol map: MCP vs ACP (and where opencode sits)

Two protocols with similar acronym energy but **opposite directions**:

- **MCP (Model Context Protocol)** — between an *agent* and *tool/data providers*.
  The agent is the consumer; MCP servers add capabilities (filesystem, GitHub,
  databases…). Roughly: *the agent's hands.*
- **ACP (Agent Client Protocol**, originated at Zed) — between an *editor* and a
  *coding agent*. Roles invert: the agent is the service provider; the editor (Zed,
  Neovim, Emacs…) launches it as a subprocess and talks JSON-RPC over stdio — opens
  sessions, sends prompts, renders streaming updates and permission requests in its
  own UI. Roughly: *LSP for agents — the agent's face.*

opencode sits on both sides:

- **MCP client:** consumes MCP servers as tools (full management API incl. OAuth).
- **ACP agent:** implements the agent side — `@agentclientprotocol/sdk@0.21.0`
  dependency (`packages/opencode/package.json:57`), an `opencode acp` CLI command, and
  a full implementation in `packages/opencode/src/acp/service.ts` (initialize,
  authenticate, new/load/fork/resume session, prompt, setSessionModel/Mode, cancel…).

**Relevance to this project:** ACP is a third integration surface into opencode, not an
alternative to the HTTP API our SDK targets. It doesn't change the roadmap; it's just
part of the map. (Our own MCP server — see doc 05 — makes opencode reachable from the
MCP side; ACP already makes it reachable from editors.)

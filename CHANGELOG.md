# opencode v2 .NET SDK Change Log

This document outlines the changes, updates, and important notes for the opencode v2 SDK for .NET.
Each released version links straight to its GitHub Release tag.

## [Unreleased]

Nothing yet. Nightly builds of `master` are on
[GitHub Packages](README.md#nightly-builds-github-packages) as
`0.8.0-nightly.{yyyyMMdd}.{shortSha}`.

## [0.8.0-preview.1] - 2026-09-09

The first published release. Everything below describes the surface as it ships; there is no
migration to perform, because no earlier version was ever published.

### ✨ New features

- **`OpenCodeAI.Sdk` — the typed client.** **135 of the 140 operations** in the pinned OpenAPI
  snapshot are callable across **28 client families**: sessions, PTYs, persistent PTYs, shells,
  events, MCP servers, integrations, projects, worktrees, workspaces, providers, language models,
  agents, skills, commands, forms, permissions, credentials, plugins, RPC, references, VCS,
  websearch, file system, generation, server, debug, and experimental. Every operation carries a
  generated request type and a generated response envelope; bound handles (`SessionClient`,
  `PtyClient`, `PersistentPtyClient`) partially apply a resource id over the shared pipeline.
- **Unions dispatch by tag, and tolerate what they do not know.** A discriminated union decodes by
  the literal tag the document declares. The live event stream additionally carries one
  prefix-tagged arm, so every `rpc.*` event dispatches to `EventRpc` — tried after the literal tags
  and before the carrier. Anything matching neither lands in that union's `Unknown*` variant with
  its tag and raw payload, so a server newer than the pinned snapshot widens an enumeration rather
  than breaking it.
- **A standalone server launcher.** `OpenCodeServer.StartAsync()` starts, monitors, and stops a
  private `opencode2 serve` child — generated lease credential, stdin-EOF ownership, bounded tree
  termination — and `CreateClient()` hands back a client already bound to it. An optional
  `OpenCodeServerOutput` collector, supplied through `OpenCodeServerOptions.Output`, retains a
  bounded tail of the child's stdout and stderr for pull snapshots that report truncation, and it
  stays readable after a failed start; `ProcessId` stays readable after disposal for the same
  diagnostic use. Real-process lifecycle acceptance runs on Windows, Linux, and macOS.
- **Server-sent event streaming.** `EventsClient.SubscribeAsync` follows the global bus and
  `SessionClient.GetLogAsync` follows one session's log, both as `IAsyncEnumerable<T>` of typed
  frames over the same transport, decoration, and status walls as one-shot calls. A body cut
  mid-event is reported rather than dispatched, and the contract's mid-stream failure channel
  surfaces as a typed exception instead of being discarded.
- **PTY and persistent-PTY terminal sessions.** `PtySession` and `PersistentPtySession` are
  hand-written WebSocket doors over a shared, family-neutral socket core: read frames, write input,
  dispose to close. The persistent family adds attach/handoff/snapshot semantics, byte-typed
  output and checkpoints, and the framed input protocol with viewport tracking.
- **Terminal connection lifetime a caller can drive.** A connection-owned receiver assembles
  frames into an internal queue independently of the caller's enumeration, so canceling a read ends
  only that wait: the same healthy connection can be read again, or written to, afterwards. Both
  connect options gained an init-only `SendTimeout` (default 30 seconds) bounding each input or
  resize send end to end, including the wait for the send gate. Connection termination delivers the
  frames it already holds before reporting its outcome, while disposal abandons unread frames and
  joins the connection's own work. A persistent PTY keeps its locally requested viewport coherent
  with the input it labels, rather than letting an inbound resize report overwrite it. See
  [the terminals guide](docs/guide/terminals.md#-cancellation-deadlines-and-disposal).
- **Dependency injection through `OpenCodeAI.Sdk.Extensions`.** `AddOpenCode(Action<…>)` or
  `AddOpenCode(IConfiguration)` registers one singleton client owning its transport for the
  container's lifetime, plus every sub-client resolved from that same instance — inject
  `SessionsClient` or `EventsClient` directly.
- **A typed error model.** Calls throw typed exceptions by default; `OpenCodeRequestOptions.NoThrow`
  returns the failure as data on the same envelope (`IsError`, `Error`) for the cases where a
  non-2xx is a normal answer. Transport failures map to their own exception family.
- **Cursor-based asynchronous pagination.** The two cursor-carrying list envelopes — session list
  and message list — expose the wire cursor directly, and `SessionClient.EnumerateMessagesAsync`
  follows it to exhaustion as an `IAsyncEnumerable<T>`.
- **Source-generated JSON.** `System.Text.Json` source generation throughout, with no reflection
  fallback anywhere in the serialization path. Both packages declare `IsAotCompatible` on
  `net10.0`; the one reflective seam, `AddOpenCode(IConfiguration)`, is annotated
  `[RequiresDynamicCode]` / `[RequiresUnreferencedCode]` so a trimmed or AOT build is warned rather
  than surprised.

### 🛠️ General

- **Target frameworks:** `netstandard2.0`, `net472`, `net8.0`, `net9.0`, `net10.0` — for both
  packages. The suite runs on `net472` on Windows, real-process launcher tests included, and on
  `net8.0`/`net9.0`/`net10.0` on all three OSes. `netstandard2.0` is a consumption target rather
  than a test target — it has no runtime to execute on, and the `net472` leg is what exercises its
  compile surface.
- **Protocol identity:** built against an accepted OpenAPI snapshot of upstream's `v2` branch, not
  a live branch. The exact commit, its digest, and the receipt-governed refresh procedure live in
  [`spec/SNAPSHOT.md`](spec/SNAPSHOT.md).
- **Generated output is committed and reviewed as source**, locked by a public-API baseline and
  verified by regeneration, so a protocol refresh arrives as a readable diff.
- **Test suite:** 5,138 tests green on Windows — the fullest leg, and the only one that adds the
  `net472` assemblies. Linux and macOS run the same suite on the three modern targets.

### 📋 Important Notes

- **Unofficial.** This project is not affiliated with or endorsed by the opencode team.
- **Three operations are declined by decision, not by omission.** `v2.config.get` and
  `v2.experimental.migration.v1.status` hit an undiscriminated object-union wall; `v2.fs.read` is
  a framework wildcard route with no OpenAPI path template to bind. Admitting any of them would
  mean inventing a contract upstream does not declare. See
  [API Coverage](README.md#-api-coverage).
- **Two operations are transport-owned, not missing.** `v2.pty.connect` and
  `v2.persistentPty.connect` are WebSocket upgrades, so they are served by the hand-written
  `PtySession` / `PersistentPtySession` doors rather than by generated code — fully usable, just
  not generated.
- **Pre-1.0 API.** The public surface is locked by a reviewed baseline, but it may still move
  before `1.0.0`. Breaking changes will be called out here with impact and migration path.

[0.8.0-preview.1]: https://github.com/Blind-Striker/opencode-sdk-dotnet/releases/tag/v0.8.0-preview.1

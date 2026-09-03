# opencode .NET SDK Change Log

This document outlines the changes, updates, and important notes for the opencode SDK for .NET.
Each released version links straight to its GitHub Release tag.

## [Unreleased]

> **The first release.** `0.1.0` has not shipped to NuGet.org yet — nightly builds of everything
> below are on [GitHub Packages](README.md#nightly-builds-github-packages) today, versioned
> `0.1.0-nightly.{yyyyMMdd}.{shortSha}` from every code push to `master`.

### ✨ New features

- **`OpenCode.Sdk` — the typed client.** **135 of the 140 operations** in the pinned OpenAPI
  snapshot are callable across **28 client families**: sessions, PTYs, persistent PTYs, shells,
  events, MCP servers, integrations, projects, worktrees, workspaces, providers, language models,
  agents, skills, commands, forms, permissions, credentials, plugins, RPC, references, VCS,
  websearch, file system, generation, server, debug, and experimental. Every operation carries a
  generated request type and a generated response envelope; bound handles (`SessionClient`,
  `PtyClient`, `PersistentPtyClient`) partially apply a resource id over the shared pipeline.
- **Refreshed to upstream `48f2466`.** The pinned snapshot moved, and the surface moved with it.
  The live event stream gained its first prefix-tagged arm: every `rpc.*` event dispatches to
  `EventRpc`, tried after the declared literal tags and before the unknown carrier, and
  `UnknownEvent` refuses an `rpc.*` tag rather than absorbing it. Four operations arrived —
  `v2.plugin.awaitActivation`, `v2.plugin.check`, and `v2.plugin.update` as
  `PluginsClient.PostAwaitActivationAsync`, `PostCheckAsync`, and `PostUpdateAsync`, and
  `v2.rpc.call` as `PostCallAsync` on the new `RpcClient` family. Upstream also removed and
  reshaped the plugin types, which nightly consumers will meet as compile breaks: the
  `plugin.added` event arm is gone, and `PluginAdded` and `PluginAddedData` with it; `IPluginInfo`
  and its `PluginInfoActive`, `PluginInfoFailed`, and `UnknownPluginInfo` variants are replaced by
  the `PluginInfo` record, whose `State` is the new `IPluginState` union (`PluginStateActive`,
  `PluginStateFailed`, `UnknownPluginState`) — match on the state where you matched on the info
  variant; `PluginSourcePackage.Package` is now `Target`, joined by `Version`, `Outdated`, and
  `Updating`; and `PluginListResponse.Plugins` is an `IReadOnlyList<PluginInfo>`.
- **A standalone server launcher.** `OpenCodeServer.StartAsync()` starts, monitors, and stops a
  private `opencode serve` child — generated lease credential, stdin-EOF ownership, bounded tree
  termination — and `CreateClient()` hands back a client already bound to it. Real-process
  lifecycle acceptance runs on Windows, Linux, and macOS.
- **Server-sent event streaming.** `EventsClient.SubscribeAsync` follows the global bus and
  `SessionClient.GetLogAsync` follows one session's log, both as `IAsyncEnumerable<T>` of typed
  frames over the same transport, decoration, and status walls as one-shot calls. A body cut
  mid-event is reported rather than dispatched, and the contract's mid-stream failure channel
  surfaces as a typed exception instead of being discarded.
- **PTY and persistent-PTY terminal sessions.** `PtySession` and `PersistentPtySession` are
  hand-written WebSocket doors over a shared, family-neutral socket core: read frames, write input,
  dispose to close. The persistent family adds attach/handoff/snapshot semantics, byte-typed
  output and checkpoints, and the framed input protocol with viewport tracking.
- **Dependency injection through `OpenCode.Sdk.Extensions`.** `AddOpenCode(Action<…>)` or
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
- **Test suite:** 4,418 tests green on Windows — the fullest leg, and the only one that adds the
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

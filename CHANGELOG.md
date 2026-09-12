# opencode .NET SDK Change Log

This document outlines the changes, updates, and important notes for the opencode SDK for .NET.
Each released version links straight to its GitHub Release tag.

## [Unreleased]

Nothing yet. Nightly builds of `master` are on
[GitHub Packages](README.md#nightly-builds-github-packages) as
`0.8.0-nightly.{yyyyMMdd}.{shortSha}`.

## [0.8.0-preview.2] - 2026-09-12

The second preview, built against upstream release tag `v2.0.2`. The breaking changes come
first, each with what to change; nothing else needs action to upgrade from `0.8.0-preview.1`.

### 💥 Breaking changes

- **A request property the API document declares both optional and nullable is now
  `Optional<T?>`** instead of `T?` — 39 properties across 21 operations, one of them the shared
  `ToolFileContent.Name`. The wrapper is what tells "leave this alone" apart from "send an
  explicit null": an unassigned member is absent and is not written at all, `= null` and
  `Optional<T?>.Null` both write JSON null, and a value assigns through an implicit conversion, so
  `Title = "Fix the build"` keeps compiling unchanged. Reading one back goes through `IsSet` and
  `Value`, and `= default` returns a member to absent. Response-only schemas are untouched;
  `ToolFileContent` is the one schema shared both ways, so its `Name` carries the wrapper on the
  read side too and is read through `.Value`. The new [Requests](docs/guide/requests.md) guide page
  covers it.
- **The accepted snapshot moved to upstream release tag `v2.0.2`**
  (`ea5ae2329569e4fbf063be451480b58e29de6816`), which published as `@opencode/cli@2.0.2`. Install
  that release (`npm install -g @opencode/cli@2.0.2`); the `@opencode-ai/cli` scope that the
  previous README named is frozen at an August build that predates the worktree route shape and the
  whole persistent-PTY family this SDK generates.
- **`v2.session.messageUpdate` is gone** because upstream removed the message content mutation API
  ([anomalyco/opencode#48043](https://github.com/anomalyco/opencode/pull/48043)).
  `SessionClient.PatchMessageUpdateAsync`, `SessionMessageUpdatePatchRequest`,
  `SessionMessageUpdatePatchResponse`, and the route constants are removed, and with them the
  `ISessionMessageAssistant` union and its `UnknownSessionMessageAssistant` carrier, which only that
  request body referenced. Assistant content records keep `ISessionMessageAssistantContent`.
- **`session.message.content.updated` left the live event union.** `SessionMessageContentUpdated`
  no longer implements `IEvent`; it remains a durable session-log item.

### ✨ New features

- **A preference can be cleared, not just overwritten.** `PatchUpdatePreferencesAsync` sends an
  explicit JSON null for `Shell` or `Websearch` when you assign `Optional<T?>.Null` (or plain
  `null`), which is how upstream deletes the key from the preferences document; omitting the member
  leaves the stored value alone.
- **The config family is on the client.** The `v2.0.2` snapshot added four operations and all four
  are generated and covered: `OpenCodeClient.Config` carries `GetPreferencesAsync`,
  `GetShellsAsync`, and `PatchUpdatePreferencesAsync` (global preferences read, the host shell
  catalog, and the preferences patch), and `SessionClient.PutPermissionRulesAsync` replaces a
  session's permission ruleset. `ConfigPreferences.Websearch` and
  `ConfigUpdatePreferencesPatchRequest.Websearch` are structural unions over `false` and a
  `ConfigWebSearchInfo` provider. Coverage is 138 of 143 pinned operations, up from 135 of 140 in 0.8.0-preview.1.

- **Sessions carry their permission ruleset, and changing it raises an event.**
  `SessionCreateRequest`, `SessionInfo`, and `SessionCreatedData` gained `Permissions`, and the new
  durable event `session.permissions.updated` materializes as `SessionPermissionsUpdated` on
  `IEvent`, `ISessionEventDurable`, and `ISessionLogItem`, carrying the session id and the new
  ruleset.

- **One door submits a command line on both terminal families.** `PtySession.SubmitAsync(string)`
  and `PersistentPtySession.SubmitAsync(string)` send the line plus the carriage return a
  terminal's Enter key sends, through the same serialized send path as the matching `WriteAsync` —
  a UTF-8 text message for a normal PTY, the framed input message carrying the viewport for a
  persistent one. The argument is exactly one line: a `\r` or an `\n` inside it is refused with
  `ArgumentException`, an empty line is a bare Enter, and nothing is trimmed. `WriteAsync` is
  unchanged and stays the raw door — partial input, control sequences, and bytes a terminal
  emulator produced.

- **A union interface now carries what all its members share.** A marked union whose members all
  declare a property in the same shape promises that property on its interface, so a generic
  consumer reads it without a `switch` over concrete types. The landmark case is the session log:
  `ISessionEventDurable` gained `Id`, `Created`, `Metadata`, `Location`, and `Durable`, and the 43
  per-event envelope records now implement one new `IDurableEnvelope` carrying `AggregateID`,
  `Seq`, and `Version` — so `item.Durable?.Seq` replaces a partial type switch. `IEvent`,
  `IFormField`, `IMcp`, `IReferenceSource`, `ISessionMessageInfo`, `ISessionMessageCompaction`,
  `ISessionInboxInfo`, `ISessionInboxItem`, `ISessionForkBoundary`, `IIntegrationAttemptStatus`,
  and `IIntegrationCommandAttemptStatus` gained members the same way, with `IMcpTimeout`,
  `ISessionMessageCompactionTime`, `IIntegrationAttemptStatusTime`, and
  `IIntegrationCommandAttemptStatusTime` as further carriers. Members are declared nullable because
  a union's `Unknown*` carrier preserves a raw payload and materializes none of them; the concrete
  records keep their existing non-nullable properties, the wire shape is unchanged, and no type was
  removed. Implementing one of these interfaces outside the SDK now requires the new members.

- **Session listing enumerates too.** `SessionsClient.EnumerateSessionsAsync` joins
  `SessionClient.EnumerateMessagesAsync` as an automatic cursor walk. A list query now binds the
  shared `ListRequest` spine whenever it *includes* `limit`, `order`, and `cursor` — extra filters
  no longer disqualify it — so `SessionListRequest` derives from `ListRequest` and gains the
  companion. Every filter of the first request rides each continuation unchanged; only the
  first-page-only `order` is dropped and the opaque cursor replaced.
- **Every automatic walk can be read as pages.** `Enumerate*Async` now returns
  `CursorSequence<TPage, TItem>`, whose `Pages` property yields each generated response envelope —
  `Status`, `Cursor`, `RawBody` and all — instead of only the items. There is no new page type and
  no page-size knob: a page is the response the server returned for one request. The item door and
  the page door are independent walks of the same recipe, so enumerating both sends both sets of
  requests.

### 🐛 Fixes

- **The docs no longer claim an anonymous opencode server exists, and a 401 now says so.** The
  guide and the shipped XML on `OpenCodeClientOptions.Password` told readers that `null` was the
  right value "for a server started without authentication". No such server can be started: the
  `opencode` CLI always runs its server with a password — the one set through `OPENCODE_PASSWORD`,
  or one it generates and prints as `server password <pw>` — and it rejects an uncredentialed
  request above the API layer, with an empty body and no typed error to read. Every statement of
  that claim is corrected, and when a call answers 401 while the client was built with `Password`
  left `null`, the `OpenCodeApiException` message now adds one sentence naming the missing
  credential and where to get one. The sentence appears for 401 only, and only when no password was
  configured; a rejected password keeps the plain message, and the `NoThrow` envelope is unchanged.
  No public member changed.
- **`OpenCodeServer.StartAsync()` now works on Windows with an npm-installed CLI.** npm writes shim
  files (`opencode`, `opencode.cmd`, `opencode.ps1`) and keeps the real binary inside
  `node_modules`, while `Process.Start` appends only `.exe` and ignores `PATHEXT` — so the shipped
  default `Command` failed on every npm-installed Windows machine with a bare "Failed to start the
  server command 'opencode'" and no stderr. The launcher now resolves `Command[0]` the way a shell
  does before spawning anything, so the `.cmd` shim is found and started. Nothing changes on Linux
  or macOS, where npm's bin entry is a link to the binary and the default already worked.

### 🔧 Changes

- **`Command[0]` is resolved before the process is created.** A path (rooted, or carrying a
  directory separator) is used as written; a bare name is searched through the `PATH` directories
  in order, skipping empty entries and resolving relative ones against the current directory. On
  Windows a bare name with no extension is tried with each `PATHEXT` extension in `PATHEXT` order
  (falling back to `.COM;.EXE;.BAT;.CMD`), and a name that already carries an extension is tried as
  written; on Unix the name itself is searched. A bare name that matches nothing now fails with an
  `OpenCodeServerException` that names the command, the number of directories searched, and the
  extensions tried, instead of the bare spawn error it used to surface.
- **A resolved Windows `.cmd`/`.bat` shim is launched through the system `cmd.exe`** (`/d /s /c`,
  every token quoted) rather than through `CreateProcess`'s implicit batch handling. Two consequences:
  `ProcessId` then reports the `cmd.exe` host rather than the server process — ask the server for
  `health.Health.Pid` when you need that one, and note that disposal's whole-tree kill still covers
  everything underneath — and a leading argument of yours containing `&`, `|`, `<`, `>`, `^`, `%`,
  `!`, `"`, CR, or LF is refused with `OpenCodeServerException` before anything starts, because
  `cmd.exe` re-parses the line (the fail-closed answer to BatBadBut / CVE-2024-24576). The SDK's own
  `--stdio --port 0` are unaffected, and non-batch targets are launched exactly as before.
- **The terminals guide no longer contradicts itself about Enter.** Its normal-PTY example wrote
  `"echo hello\r"` while its persistent-PTY example wrote `"echo hello\n"`. Both now run their
  command through `SubmitAsync`, and the terminator rule is stated once: Enter is `\r`, `\n` is a
  line feed that the Windows console host does not accept as a submit, and the SDK never rewrites
  what `WriteAsync` is given.

- **Snapshot additions.** `MessageListRequest.Type` filters a message list by message type
  (`MessageListRequestType`) and rides every continuation unchanged; `ModelInfo.Websocket` and
  `ProviderInfo.Websocket` are new optional flags; compaction ended and failed data, and the
  compaction message records, carry optional `Cost` and `Tokens`.
- `SessionClient.EnumerateMessagesAsync` returns `CursorSequence<MessageListResponse,
  ISessionMessageInfo>` instead of `IAsyncEnumerable<ISessionMessageInfo>`. `await foreach` over it
  is unchanged, and code that stored the result in a variable typed `IAsyncEnumerable<T>` still
  compiles, because `CursorSequence<TPage, TItem>` implements `IAsyncEnumerable<TItem>`. Code that
  declared the variable with `var` and then assigned an `IAsyncEnumerable<T>` to the same variable
  is the one shape that needs its type written out.
- `SessionListRequest` no longer declares `Limit`, `Order`, and `Cursor` itself; it inherits all
  three from `ListRequest`. Reading and initializing them is unchanged.

### 📚 Documentation

- **Durable replay now says which servers can actually do it.** The streaming guide and the
  README's known issues state that the distributed `opencode` CLI starts its server without event
  persistence and exposes no switch for it, so a replay from a CLI-started server answers with the
  `log.synced` marker alone — a contract-valid success with no history in it. Observed on
  `@opencode/cli@2.0.2`. A new live test on the ordinary pinned CLI profile asserts the
  marker-only answer and is the reversal trigger: when it fails, upstream began
  persisting by default and those statements change with it.
- **"Choosing a model" is a new section in the getting-started guide.** One compiled recipe —
  await plugin activation, read the provider and model catalogs, then place
  `new ModelRef { ProviderId = model.ProviderId, Id = model.Id }` on session creation — plus the
  three rules that surround it: health is process liveness and not catalog readiness, a session
  reference carries `ModelInfo.Id` and never `ModelInfo.ModelId`, and neither create nor prompt
  validates the reference. The same recipe runs in the in-repo sandbox's `--standalone` leg.
- **Session export documents what `Sanitize` does.** A sanitized export replaces message text with
  `[redacted:text:<id>]`-shaped placeholders while ids, types, order, and count survive, so it is
  for sharing a conversation's shape and never for comparing transcripts.
- **A refused worktree removal has a paragraph in the errors guide.** A 400 `WorktreeError` removed
  nothing — the directory and its inventory row both remain — and `ForceRequired` false means git
  ran and failed for a reason `Force` cannot fix, such as the Windows permission denial observed on
  the then-pinned `@opencode/cli@0.0.0-beta-19242` while another process held the directory.

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

[0.8.0-preview.2]: https://github.com/Blind-Striker/opencode-sdk-dotnet/releases/tag/v0.8.0-preview.2
[0.8.0-preview.1]: https://github.com/Blind-Striker/opencode-sdk-dotnet/releases/tag/v0.8.0-preview.1

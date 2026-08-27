# Typed Per-Call Location + Hand-Written PTY Family Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use deniz-process:subagent-driven-development
> (recommended) or deniz-process:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

Revision 2 (2026-08-27): reworked after two independent reviews (Opus, Fable — both REWORK).
The reconciliation record at the end lists what changed and why.

**Goal:** Build the per-request header channel twice over: typed per-call location on
`OpenCodeRequestOptions`, and the hand-written normal-PTY family (ADR-0021) — `PtysClient`,
`PtyClient`, the internally-applied `x-opencode-ticket` token door, and the `PtySession`
WebSocket working object. **Maintainer decision 2026-08-27: `PtySession` is inside this arc.**

**Architecture:** Per-call location rides `PipelineMessage` into `RequestDecorationPolicy`,
which merges it member-by-member over the ambient snapshot (per-call wins, null inherits, no
clearing) and injects the two location headers uniformly with the server's encoding asymmetry.
The generator gains a curation-declared internal-raw emission mode whose operations may carry
document-declared header parameters as internal method parameters — the "runtime channel" the
wire-shape wall is waiting for. The pty group flips to internal-raw and the public family is
hand-written over the generated internal artifacts. `PtySession` wraps a `ClientWebSocket`
behind a named internal seam and speaks upstream's observed frame protocol.

**Tech Stack:** .NET multi-TFM (net472/netstandard2.0/net8/net9/net10), TUnit on MTP,
`System.Net.WebSockets.ClientWebSocket`, the repository generator (`tools/OpenCode.Sdk.Tools`).

**Spec:** `docs/superpowers/specs/2026-08-26-continuous-protocol-coverage-program-design.md`
(§3.3, §5, §10, §13), ADR-0021, ADR-0019, ADR-0018, ADR-0007; facts in research log Q148/Q150
and research doc 21.

## Global Constraints

- Completion gate per task (from `docs/engineering/quality-gates.md`): slopwatch, Release
  build, `dotnet format whitespace`/`style --verify-no-changes`, `dotnet test`; generator tasks
  add the tool `--help` smoke and `generate --verify`.
- Generated output changes only through the generator; never hand-edit generated files.
- `external/opencode` is read-only evidence. The ticket constant `"1"` exists only in upstream
  implementation source: it may inform hand-written runtime code (knowledge source:
  `upstream-observed`) but must never enter curation or generated output (ADR-0013, ADR-0021).
  The header *name* `x-opencode-ticket` is document-declared and may appear in generated code.
- No arbitrary public header facility (§13). The declared-header channel built here is
  internal-only and carries exclusively header parameters the pinned document declares on
  internal-raw operations. No persistentPty WebSocket door in this arc (design §3.4).
- Product code uses `ConfigureAwait(false)`; tests are exempt. File-scoped namespaces, doc
  comments on public surface. Public clients and working objects follow the repository mocking
  convention (ADR-0019 / Azure guidelines): protected parameterless constructor, virtual
  members, `MockSeam` guards — this overrides sealed-by-default for `PtySession`.
- PublicApi baseline (`tests/OpenCode.Sdk.Tests/PublicApiBaselineTests.cs`) updates ride the
  task that changes the surface; this arc's deltas are additive except the generated→
  hand-written PTY swap, which must be signature-identical for existing members.
- Naming trap for tests: the location query member is `workspace`; the `session.create`/
  `import` body member is `workspaceID` (Q148).
- Documentation moves in the same commit as the code it describes — including the ROADMAP
  profile counts and the connect-token deselection sentence, which Task 3 changes.
- Commits need no AI trailers; never push without an explicit ask.
- §10 sequencing inside the arc: the WS session task (Task 5) must not start before the token
  door (Task 3) and the connect-operation fingerprint (Task 4) are landed.

## Protocol facts the tasks argue from (pin `803ead32`, source-verified twice, 2026-08-27)

- `POST /api/pty/{ptyID}/connect-token` answers 403 unless the request carries
  `x-opencode-ticket: 1` and an allowed origin; success mints `{ ticket: <uuid>,
  expires_in: <seconds, default 60> }` scoped to `{ptyID, resolved directory, resolved
  workspaceID}` (`packages/server/src/handlers/pty.ts`, `packages/core/src/pty/ticket.ts`).
  The pinned document declares the header as an optional header parameter on the operation.
- `GET /api/pty/{ptyID}/connect` upgrades to WebSocket. **Auth (settled from source, both
  reviews):** the API-wide Basic middleware (`api.ts:190`, `middleware/authorization.ts:51-55`)
  skips credentials only when the URL carries a non-empty `ticket` query
  (`hasPtyConnectTicketURL`); a ticket-less upgrade with `Authorization: Basic …` is the
  designed non-browser path. The handler validates a ticket only when present. A missing pty
  answers plain HTTP **404 before upgrading**; bad ticket/origin 403; bad Basic 401.
- Query on connect: `location[directory]`, `location[workspace]`, `cursor`, `ticket`.
  **Cursor semantics (corrected):** *omitted* replays the full retained buffer; `-1` attaches
  live-only (no replay); `n ≥ 0` resumes from absolute cursor `n`. The server accepts only
  JavaScript safe integers ≥ -1 and silently coerces anything else to *omitted* (= full
  replay); it never errors on cursor.
- Frame protocol (`packages/core/src/pty/protocol.ts`): server→client output rides text
  frames (upstream emits JS strings); the sole binary frame is the control frame — a `0x00`
  byte followed by UTF-8 JSON `{"cursor": n}` — sent once after replay. Replay is chunked at
  64Ki **UTF-16 code units** (up to ~192 KiB UTF-8; a chunk boundary can split a surrogate
  pair, so output decoding must be replacement-based, never fatal). Normal close is code
  1000 — sent *bare* on process end (the exit code is only reachable via `GetPtyAsync`).
  Close 4404 means "session not found"/"session exited"; an existing-but-exited pty upgrades
  101 first and then closes 4404, so the failure surfaces on the first read, not on connect.
  Client→server frames are UTF-8 text (binary tolerated; invalid UTF-8 dropped server-side).
- Location resolution is per-member: directory `query > percent-decoded header > cwd`;
  workspace `query > raw header > unset` (Q148). Session-scoped routes ignore location
  inputs entirely — the injected headers are a documented no-op there. The ticket's mint and
  consume scopes must resolve identically or consume fails.

---

### Task 1: Typed per-call location

**Files:**
- Modify: `src/OpenCode.Sdk/OpenCodeRequestOptions.cs`
- Modify: `src/OpenCode.Sdk/Internal/PipelineMessage.cs`
- Modify: `src/OpenCode.Sdk/Internal/Pipeline.cs` (`ExecuteCoreAsync` → `CreateMessage`)
- Modify: `src/OpenCode.Sdk/Internal/RequestDecorationPolicy.cs`
- Modify: `docs/architecture/client-runtime.md` (new "Location" subsection under
  Construction; state merge semantics, encoding asymmetry, session-route no-op)
- Test: `tests/OpenCode.Sdk.Tests/RequestDecorationPolicyTests.cs` (new; use
  `tests/Shared/RecordingHttpHandler.cs` + `RecordedRequest.cs`)
- Test: `tests/OpenCode.Sdk.Tests/OpenCodeRequestOptionsTests.cs` (extend)
- Modify: PublicApi baseline (additive: `OpenCodeRequestOptions.Location`)

**Interfaces:**
- Consumes: existing `LocationSelector` (blank-refusing `Directory`/`Workspace`),
  `IOpenCodeClientOptions.Location` ambient snapshot.
- Produces: `public LocationSelector? Location { get; init; }` on `OpenCodeRequestOptions`;
  `internal LocationSelector? PerCallLocation { get; init; }` on `PipelineMessage` —
  construction-written like `Request`/`BufferBody` (`init`, not `internal set`; ADR-0018
  reserves settable members for pipeline-written state), doc comment naming writer
  (`Pipeline`) and reader (`RequestDecorationPolicy`).

- [ ] **Step 1: Write the failing tests** (through a real `Pipeline` with
  `RecordingHttpHandler`, asserting recorded headers; construct clients via the internal
  `(HttpClient, options)` friend seam):

```csharp
[Test]
public async Task PerCallLocationOverridesAmbientMemberByMember()
{
    // Ambient: directory=/amb/dir, workspace=amb-ws. Per-call: directory=/per/dir only.
    // Expect x-opencode-directory=%2Fper%2Fdir (per-call wins, percent-encoded),
    // x-opencode-workspace=amb-ws (unset per-call member inherits ambient).
}

[Test]
public async Task PerCallLocationCannotClearAnAmbientMember()
{
    // LocationSelector refuses blank members, so the only spelling of "clear" is null —
    // and null inherits. Assert both headers still carry ambient values when the per-call
    // selector is present but both members are null.
}

[Test]
public async Task PerCallDirectoryIsPercentEncodedAndWorkspaceRidesRaw()
{
    // directory "/tmp/päth ü" → Uri.EscapeDataString form; workspace "wsp_123" verbatim.
}

[Test]
public async Task AbsentLocationsSendNoLocationHeaders() { /* neither header present */ }

[Test]
public async Task PerCallLocationWithoutAmbientSendsOnlySetMembers() { /* directory only */ }
```

- [ ] **Step 2: Run the new tests; verify they fail** (no `Location` member yet).
- [ ] **Step 3: Implement.** `OpenCodeRequestOptions` gains the member; `Pipeline
  .ExecuteCoreAsync` passes `options?.Location` into `CreateMessage`, which stamps
  `PipelineMessage.PerCallLocation`; `RequestDecorationPolicy` keeps its precomputed ambient
  snapshot as the fast path and, when `PerCallLocation` is non-null, resolves per member:

```csharp
private void Decorate(HttpRequestMessage request, LocationSelector? perCall)
{
    // ... authorization unchanged ...
    var escapedDirectory = perCall?.Directory is { } directory
        ? Uri.EscapeDataString(directory)
        : _escapedDirectory;
    var workspace = perCall?.Workspace ?? _workspace;

    if (escapedDirectory is not null)
    {
        _ = request.Headers.TryAddWithoutValidation("x-opencode-directory", escapedDirectory);
    }

    if (workspace is not null)
    {
        _ = request.Headers.TryAddWithoutValidation("x-opencode-workspace", workspace);
    }
    // ... user agent unchanged ...
}
```

  Keep the existing knowledge-source comment and extend it with the merge rule ("per-call
  wins, null inherits, no per-call clearing; uniform injection — session routes ignore it
  server-side"). Streams remain options-free (ADR-0007): no stream signature changes.
- [ ] **Step 4: Run the tests; verify green. Run the full completion gate.**
- [ ] **Step 5: Commit** (`feat(sdk): carry typed per-call location onto the wire`).

### Task 2: Generator internal-raw emission mode + the header-parameter admit (no flip)

The wire-shape wall (`OperationWireShapeWall.cs:57-62`) refuses every selected operation with
a header parameter "until the location/PTY arc gives headers an owner". This task is that
owner. No group flips yet, so committed generated output is unchanged and the commit is green.

**Files:**
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Binding/Models/GroupCuration.cs` (+ emission
  mode), `CurationLoader.cs`, `CurationValidator.cs` (refuse unknown emission values; refuse
  `internalRaw` on a group with no selected operations), plus the tool JSON context if the
  curation shape is source-generated
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Binding/OperationWireShapeWall.cs` — admit a
  header parameter **only** when the owning group's emission is `internalRaw`; the refusal
  stands verbatim for public groups
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Binding/Models/ClientPlan.cs` /
  `OperationPlanBinder.cs` (plan carries the mode and the declared header parameters)
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Emission/ClientEmitter.cs` /
  `OperationMethodEmitter.cs`
- Test: `tests/OpenCode.Sdk.Tools.Tests` (extend the existing binder/emitter suites beside
  their current fixtures)

**Interfaces:**
- Produces: curation group row field `"emission": "internalRaw"` (default absent = public).
  Under `internalRaw`:
  - family clients emit as `internal sealed class <Name>RawClient` (e.g. `PtysRawClient`,
    `PtyRawClient`) with internal constructors and internal methods, no mocking constructor,
    no public doors;
  - **the root client is unchanged**: `OpenCodeClient` keeps emitting
    `public virtual PtysClient Ptys` constructing `new PtysClient(_pipeline)` against the
    *public family name* — after Task 3 that resolves to the hand-written class, whose
    `internal PtysClient(Pipeline)` constructor is therefore a pinned contract;
  - a document-declared header parameter on an operation emits as an ordinary trailing
    *internal* method parameter (`string? xOpencodeTicket = null`, name derived from the
    document), flowing into the declared-headers channel below. Values never appear in
    generated code (ADR-0013);
  - routes, query shapers, request/response wire models, envelopes, serializer metadata,
    response adapters, and status verdicts emit exactly as today.
- Produces (generator tests only assert emitted source text; the *runtime* channel these
  emissions call lands in Task 3, in the same slice as its first consumer, so no dead code
  ships in this task).

- [ ] **Step 1: Write failing generator tests**: a fixture group with `"emission":
  "internalRaw"` binds to a plan flagged internal-raw; the emitted client source contains
  `internal sealed class` + `internal` methods and no `protected` mocking constructor; a
  header-parameter operation in an `internalRaw` group binds (wall admits) and emits the
  header as an internal method parameter; the same operation in a public group is still
  refused with the existing message; the validator refuses `"emission": "bogus"`; the root
  client emitted for an internal-raw group still exposes the public family accessor.
- [ ] **Step 2: Run; verify they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify no committed output changes**: `generate --verify` still current
  (no group uses the mode yet). Run the full gate + tool smoke.
- [ ] **Step 5: Commit** (`feat(tools): add the internal-raw emission mode and give header
  parameters their runtime channel`).

### Task 3: The PTY family flip — declared-headers runtime channel + hand-written family + token door

One bounded vertical slice (§10): runtime channel, generator flip, profile additions, and the
hand-written family land in a single commit so every commit builds.

**Files:**
- Create: `src/OpenCode.Sdk/Internal/DeclaredHeader.cs` (small readonly name/value record)
- Modify: `src/OpenCode.Sdk/Internal/PipelineMessage.cs` (+`DeclaredHeaders`),
  `src/OpenCode.Sdk/Internal/Pipeline.cs` (internal `ExecuteAsync` overloads threading it),
  `src/OpenCode.Sdk/Internal/RequestDecorationPolicy.cs` (apply message-declared headers
  uniformly — the policy never learns a family or header name)
- Modify: `tools/curation.json` (pty group gains `"emission": "internalRaw"`; reason text
  cites ADR-0021), `tools/generation-profile.txt` (+`v2.pty.list`, +`v2.pty.connect.token`)
- Regenerate: pty internal raw clients + new wire models (list envelope, connect-token
  envelope carrying `{ticket, expires_in}`, and their query-shaping request records);
  generated public `Ptys/PtysClient.cs`, `Ptys/PtyClient.cs` leave the manifest
- Create (hand-written): `src/OpenCode.Sdk/Ptys/PtysClient.cs`, `src/OpenCode.Sdk/Ptys/PtyClient.cs`
- Test: `tests/OpenCode.Sdk.Tests/Ptys/` (extend)
- Modify: PublicApi baseline; `docs/architecture/client-runtime.md` (PTY family ownership
  subsection relaying ADR-0021); `docs/ROADMAP.md` (profile counts and the connect-token
  deselection sentence — they change in *this* commit, not Task 6)

**Interfaces:**
- Consumes: Task 2's emission mode and emitted header parameters; existing `MockSeam`,
  `OpenCodeRoutes.Ptys`, response adapters.
- Produces: `internal readonly record struct DeclaredHeader(string Name, string Value);`
  `PipelineMessage.DeclaredHeaders` (`IReadOnlyList<DeclaredHeader>?`, construction-written
  `init`, writer `Pipeline`, reader `RequestDecorationPolicy`, applied via
  `TryAddWithoutValidation`). This is not a general header facility: only generated
  internal-raw methods and hand-written family doors inside the assembly can reach it, and
  only document-declared parameters feed it.
- Produces (public, all `virtual` with the protected mocking constructor, exactly the
  current generated shape plus — adopt the regenerated request-record names if they differ):

```csharp
public class PtysClient
{
    public virtual PtyClient GetPtyClient(string ptyId);                       // unchanged
    public virtual Task<PtyCreateResponse> CreatePtyAsync(...);                // unchanged
    public virtual Task<PtyListResponse> ListPtysAsync(
        PtyListRequest? request = null,
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default);
}

public class PtyClient
{
    public virtual Task<PtyResponse> GetPtyAsync(...);                         // unchanged
    public virtual Task<PtyUpdatePutResponse> PutUpdateAsync(...);             // unchanged
    public virtual Task<PtyRemoveResponse> RemovePtyAsync(...);                // unchanged
    public virtual Task<PtyConnectTokenResponse> CreateConnectTokenAsync(
        PtyConnectTokenRequest? request = null,
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default);
}
```

  Both new methods carry the query-shaping request parameter their siblings have: both
  operations declare the location query, which is the *highest-precedence* channel (§5) and
  the one that fixes the ticket's mint scope.
- The token constant lives only in the hand-written door:

```csharp
// Knowledge source: upstream-observed — the server's connect-token handler requires this
// exact value; it exists only in upstream implementation source (ADR-0013/0021), so it
// lives here in hand-written runtime code and never in curation or generated output.
private const string PtyTicketSentinel = "1";
```

  `CreateConnectTokenAsync` delegates to the generated raw method passing
  `xOpencodeTicket: PtyTicketSentinel`.

- [ ] **Step 1: Write the failing tests** (RecordingHttpHandler): `CreateConnectTokenAsync`
  sends `x-opencode-ticket: 1` on `POST /api/pty/{id}/connect-token`, carries the location
  query when the request shapes it, and materializes the ticket envelope; no other family
  method sends the header; `ListPtysAsync` hits `GET /api/pty` with its location query;
  existing get/update/remove/create request shapes unchanged (route, body, ambient+per-call
  location headers); mock seams still overridable.
- [ ] **Step 2: Run; verify they fail.**
- [ ] **Step 3: Implement in this order (ordering is a hazard, not a preference):**
  1. runtime channel (`DeclaredHeader`, message member, `Pipeline` overloads, policy read);
  2. flip curation/profile and run `generate` — the generation writer deletes the stale
     generated `Ptys/PtysClient.cs`/`PtyClient.cs` (manifest-tracked, provenance-guarded),
     freeing the paths;
  3. only then author the hand-written family at the freed paths, preserving the current
     XML docs and argument guards (dot-segment refusal) verbatim. Writing them first makes
     `generate` throw on files lacking the provenance header.
- [ ] **Step 4: Full gate + tool smoke + `generate --verify`. Review the PublicApi diff:
  the swap must read as pure addition.**
- [ ] **Step 5: Commit** (`feat(sdk): hand-write the normal PTY family over internal raw
  generation (ADR-0021)`).

### Task 4: Exclusion fingerprint for `v2.pty.connect`

**Files:**
- Modify: `tools/curation.json` — new top-level `transportOwned` section: one row naming the
  operation, its `subtreeSha256`, and a reason
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Binding/CurationLoader.cs`,
  `CurationValidator.cs`, the tool JSON context, and the models beside `GroupCuration.cs`
- Modify: the generation path that already sees every ingested operation — the WebSocket
  classification lives in
  `tools/OpenCode.Sdk.Tools/Generator/Ingestion/Walls/OperationExtensionPolicy.cs`; the
  fingerprint check hooks where the ingested (not selected) operation is available, since
  `v2.pty.connect` never enters the selection list
- Test: `tests/OpenCode.Sdk.Tools.Tests` (fingerprint mismatch fails `generate --verify`
  with a message naming the operation; a matching fingerprint passes)

**Interfaces:**
- Produces: a committed SHA-256 over the canonicalized operation subtree of
  `v2.pty.connect` — method, path, every parameter (`location[directory]`,
  `location[workspace]`, `cursor`, `ticket`), the `x-websocket` marker, and declared
  responses — checked during generation, so a refresh that reshapes the operation the
  hand-written door depends on fails loudly. The connect route is hand-built in Task 5, so
  path/query drift is otherwise invisible to compilation.

- [ ] **Step 1: Failing test** — mutate a fixture copy of the operation subtree; expect a
  reasoned refusal naming the operation and the changed member.
- [ ] **Step 2: Verify failure. Step 3: Implement. Step 4: Full gate + tool smoke +
  `generate --verify`. Step 5: Commit** (`feat(tools): fingerprint the transport-owned PTY
  connect operation`).

### Task 5: `PtySession` — the WebSocket working object

**Files:**
- Create: `src/OpenCode.Sdk/Ptys/PtySession.cs`, `src/OpenCode.Sdk/Ptys/PtyConnectOptions.cs`,
  `src/OpenCode.Sdk/Ptys/PtyFrame.cs` (+ `PtyOutputFrame`, `PtyCursorFrame`)
- Create: `src/OpenCode.Sdk/Internal/IPtyWebSocket.cs`,
  `src/OpenCode.Sdk/Internal/ClientPtyWebSocket.cs` (adapter over `ClientWebSocket`),
  `src/OpenCode.Sdk/Internal/ConnectionSnapshot.cs`
- Modify: `src/OpenCode.Sdk/Internal/Pipeline.cs` — expose an internal, construction-time
  `ConnectionSnapshot` (endpoint base, `AuthenticationHeaderValue?`, ambient
  `LocationSelector?`): today the endpoint is private and the credential lives only inside
  `RequestDecorationPolicy`, so nothing the session door needs is reachable
- Modify: `src/OpenCode.Sdk/Ptys/PtyClient.cs` (+`ConnectAsync`)
- Test: `tests/OpenCode.Sdk.Tests/Ptys/PtySessionTests.cs` (deterministic, over a scripted
  `IPtyWebSocket` fake)
- Modify: PublicApi baseline (additive); `docs/architecture/client-runtime.md` (PTY session
  subsection **including the transport divergence**: the WS door builds its own
  `ClientWebSocket` — a caller-supplied `HttpClient`, its proxy, and its handler chain do
  not apply); `docs/architecture/platform-and-packaging.md` (net472 requires the OS
  WebSocket stack, Windows 8+; confirm `ClientWebSocket` resolves on netstandard2.0 without
  a new package reference — if it does not, that is a dependency-rule decision to surface,
  not silently take); `CONTEXT.md` if `PtySession`/frame vocabulary is new

**Interfaces:**
- Consumes: Task 3's family and token door, Task 4's fingerprint, `LocationSelector`,
  `ConnectionSnapshot`.
- Produces:

```csharp
public sealed record PtyConnectOptions
{
    /// <summary>Gets the replay position: null replays the full retained buffer, -1 attaches
    /// live-only, and a value ≥ 0 resumes from that absolute output cursor.</summary>
    public long? Cursor { get; init; }   // guard: -1 ≤ value ≤ 9_007_199_254_740_991 (JS safe integer)

    /// <summary>Gets the per-call location; unset members inherit the ambient location. The
    /// connect scope must agree with the scope the token door resolved.</summary>
    public LocationSelector? Location { get; init; }
}

public abstract class PtyFrame { private protected PtyFrame() { } }
public sealed class PtyOutputFrame : PtyFrame { public string Text { get; } }
public sealed class PtyCursorFrame : PtyFrame { public long Cursor { get; } }

public class PtyClient
{
    public virtual Task<PtySession> ConnectAsync(
        PtyConnectOptions? options = null, CancellationToken cancellationToken = default);
}

public class PtySession : IAsyncDisposable
{
    protected PtySession() { }   // mocking seam, ADR-0019 convention; members MockSeam-guarded
    public virtual IAsyncEnumerable<PtyFrame> ReadAsync(CancellationToken cancellationToken = default);
    public virtual Task WriteAsync(string input, CancellationToken cancellationToken = default);
    public virtual ValueTask DisposeAsync();
}
```

**Design rules (argue from the protocol facts above):**
- **Auth:** the upgrade request carries the Basic credential via
  `ClientWebSocket.Options.SetRequestHeader("Authorization", ...)` — the designed
  non-browser path (settled statically by both reviews; the auth middleware exempts only
  ticket-carrying URLs). The SDK never mints tickets for its own connections — a
  single-use 60-second credential in a URL that reaches logs is strictly worse than the
  Basic header it already holds. Tickets stay reachable through the public token door for
  browser handoff. The live ticket-less-upgrade observation moves to Task 6 as recorded
  evidence, not a gate.
- **URL:** endpoint base with `http→ws`/`https→wss`, path `/api/pty/{id}/connect`, query
  built with the existing query machinery: the *merged* location (per-call
  `PtyConnectOptions.Location` over ambient, member-by-member, same sealed semantics) as
  `location[directory]`/`location[workspace]`, plus `cursor` when set. Per-call location
  exists here from day one: a pty created under a per-call location override would
  otherwise be unreachable through an ambient-only door.
- **Failed upgrade:** a missing pty answers HTTP 404 *before* upgrading; 401/403 likewise
  pre-upgrade. `ClientWebSocket.ConnectAsync` surfaces these as `WebSocketException`. On
  modern TFMs set `Options.CollectHttpResponseDetails = true` and map by status — 404 →
  `OpenCodeTransportException` naming the pty and status; 401/403 → `OpenCodeTransportException`
  naming the auth cause; other non-101 → generic transport failure. On net472 the status is
  unavailable: wrap the `WebSocketException` in `OpenCodeTransportException` with the
  connect context. A failed upgrade has no response spine, so the transport plane is the
  honest channel (stated decision; an envelope-shaped 404 would bypass ADR-0007's machinery).
- **Read loop:** single active enumeration — a second concurrent `ReadAsync` enumeration is
  refused with `InvalidOperationException` (fragment reassembly cannot be shared). Assemble
  fragmented messages to completion; a text message yields `PtyOutputFrame` decoded as
  **replacement-based** UTF-8 (a replay chunk boundary can split a surrogate pair — never
  fatal-decode output); a binary message whose first byte is `0x00` parses the remainder as
  JSON `{"cursor": n}` → `PtyCursorFrame`, and *that* parse failing is a protocol failure
  (`OpenCodeTransportException`); a binary message not starting `0x00` decodes as output.
  Close 1000 ends enumeration normally (the exit code is not on the wire — readers call
  `GetPtyAsync`); close 4404 throws `OpenCodeTransportException` naming the reason — note an
  exited pty upgrades cleanly and 4404 arrives on the first read; any other close/abort maps
  through the existing failure classification (caller cancellation stays
  `OperationCanceledException`; use `[EnumeratorCancellation]`).
- **Write:** UTF-8 text frames; sends serialized behind a `SemaphoreSlim` (`ClientWebSocket`
  allows one outstanding send); refuse null input; writes after disposal throw
  `ObjectDisposedException`.
- **Disposal:** graceful close (`CloseOutputAsync(NormalClosure)` with a bounded wait), then
  hard dispose of the socket; idempotent; disposal racing a pending read completes the read
  loop as a normal end, not an unhandled socket fault.
- **TFM notes:** net472 uses the OS WebSocket stack (Windows 8+; CI's Windows leg covers
  it) and the `ArraySegment` receive overloads behind `#if !NET`.

- [ ] **Step 1: Write failing deterministic tests** over the scripted fake: replay chunks
  yield output frames in order; the `0x00` meta frame yields `PtyCursorFrame` with the
  exact cursor; malformed control-frame JSON throws a protocol failure; a fragmented text
  message assembles once; an output chunk with a broken surrogate decodes with replacement
  instead of throwing; close 1000 ends enumeration; close 4404 throws with the reason; a
  second concurrent enumeration is refused; concurrent writes serialize; write encodes
  UTF-8; dispose is idempotent, closes gracefully, and a dispose racing a pending read ends
  the enumeration; cancellation of `ReadAsync` propagates as `OperationCanceledException`;
  `PtyConnectOptions.Cursor` guards reject `-2` and `2^53`.
- [ ] **Step 2: Verify failures. Step 3: Implement snapshot seam, adapter, session,
  `ConnectAsync`.**
- [ ] **Step 4: Full gate.** PublicApi diff: additive only.
- [ ] **Step 5: Commit** (`feat(sdk): land the PtySession WebSocket working object`).

### Task 6: Live proof, sandbox walkthrough, docs closure

**Files:**
- Modify: `tests/OpenCode.Sdk.Sandbox` (walkthrough gains: create pty → connect ticket-less
  with Basic (the recorded live confirmation of the designed path) → write `echo hello\n` →
  observe output + cursor frame → reconnect with the observed cursor (expect resume, not
  full replay) → remove → observe close)
- Modify: `docs/ROADMAP.md` (shrink the M5 lane; record what landed), research log (new Q
  entry with the arc's observations, including the ticket-less upgrade confirmation), retire
  this plan per `docs/engineering/documentation.md`
- Verify: full standing walkthrough against a server built from the pin (server recipe in
  the 2026-08-27 handoff: `OPENCODE_SERVER_PASSWORD=<pw> bun src/index.ts serve --hostname
  127.0.0.1 --port <p>` from `external/opencode/packages/cli`; sandbox needs
  `--no-launch-profile`)

- [ ] **Step 1: Extend the walkthrough. Step 2: Run it against the pinned server; record
  the observed frames.** Expected: output frames echo the command, one cursor frame after
  replay, cursor-resume yields no duplicate replay, close 1000 on remove.
- [ ] **Step 3: Documentation sweep** (client-runtime, ROADMAP, research log, CONTEXT.md).
- [ ] **Step 4: Full gate. Step 5: Commit** (`docs(sdk): close the location + PTY family
  arc with live evidence`).

## Review Reconciliation (revision 2)

Two independent reviews (Opus, Fable), both REWORK. Convergent findings adopted wholesale:
the header-parameter wall was unbudgeted (both called it the blocker); root-client emission
under internal-raw was undefined; cursor semantics were inverted; `PtySession` sealing broke
the mocking convention; concurrency, pre-upgrade failures, write ordering vs. the generation
writer, replay-chunk units, query-shaping parameters on the new methods, ROADMAP
docs-with-code, and the fingerprint's storage/read-point were all specified.

Two disagreements, resolved:
- **Seam shape:** Opus wanted document-declared header parameters feeding a generic
  `DeclaredHeaders` message member; Fable preferred a single-valued decoration enum and
  warned against a general header channel. Adopted Opus's shape *with* Fable's constraint
  stated as a rule: the channel is internal, reachable only from generated internal-raw
  methods and hand-written doors, and carries only document-declared parameters. Deciding
  factor: `server.experimental.persistentPty.connectToken` declares the same header, so the
  already-queued persistentPty HTTP batch needs the same mechanism — a family-named enum
  would be wrong twice.
- **Location on connect:** Opus accepted ambient-only with doc-comment fixes; Fable showed
  the reachability hole (a per-call-located pty is unreachable through an ambient-only
  door) and the ticket-scope agreement requirement. Adopted Fable's position:
  `PtyConnectOptions.Location` lands in this arc with the sealed merge semantics.

Both reviews independently settled the ticket-less Basic upgrade from upstream source
(auth exemption keys on ticket presence), so the step-0 gate and the minting fallback were
deleted in favor of a Task 6 recorded confirmation.

## Self-Review Notes

- Spec coverage: §5 → Task 1 (+ connect-door merge in Task 5); §3.3 hand-written family +
  token door → Tasks 2–3; §3.3 fingerprints → Task 4; §3.3 session + illustrative use →
  Task 5; deterministic + live evidence posture (ADR-0022) → Tasks 1–5 fakes + Task 6
  walkthrough. §13 non-goals hold: no public header facility (the declared-headers channel
  is internal and document-bounded), no persistentPty WS door.
- Deliberate deviation from strict bite-size: generator-internal steps (Tasks 2/4) name the
  files and the observable acceptance rather than prescribing emitter code sight-unseen —
  the emitter layer's idioms govern; the tests pin behavior.
- Type-consistency check: `PtysClient`/`PtyClient` signatures in Task 3 match Task 2's
  root-client contract and Task 5's `ConnectAsync` addition; `DeclaredHeader` is consumed
  only via `PipelineMessage.DeclaredHeaders`; `ConnectionSnapshot` is produced in Task 5
  where its only consumer lives.

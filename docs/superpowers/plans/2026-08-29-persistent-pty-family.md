# Persistent PTY Family — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use deniz-process:subagent-driven-development
> (recommended) or deniz-process:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the `v2.persistentPty.*` family (eleven operations at accepted snapshot
`106629aa`) callable through the SDK on the ADR-0021 ownership pattern: the generator emits the
family's internal raw layer, hand-written doors own the public surface (`PersistentPtysClient`,
`PersistentPtyClient`), and a hand-written `PersistentPtySession` owns the WebSocket connection —
with the generator learning base64 strings on the way, the normal PTY's socket core extracted for
both families, `persistentPty.connect` becoming transport-owned, live proof on the platforms that
can run the daemon, and the 503 daemon-absent arm proven where they cannot.

**Architecture:** Same shape as the normal PTY family (`src/OpenCode.Sdk/Ptys/`): curation
declares `emission: internalRaw`, the generator produces `PersistentPtysRawClient` /
`PersistentPtyRawClient` plus every wire model, envelope, route, adapter, and serializer entry;
hand-written doors delegate once into the raw twin and add only the knowledge generation may not
import (the `x-opencode-ticket` sentinel, the WebSocket protocol). The socket lifecycle that
`PtySession` already implements — upgrade with the Basic credential, receive/fragment reassembly,
serialized sends, bounded close, idempotent dispose, single active enumeration, failure phases —
moves into a family-neutral `TerminalSocketCore<TFrame>` with three named seams (frame decoder,
close policy, upgrade-failure policy); the persistent family supplies its own frame hierarchy,
options, URI builder, and input framing over that core. `ConnectAsync` returns only after the
server's `attached` frame, so a caller never holds a session whose viewport or existence is
unknown.

**Tech Stack:** .NET multi-TFM (`netstandard2.0;net472;net8.0;net9.0;net10.0`), `ClientWebSocket`,
System.Text.Json source generation; the repository generator under `tools/`; TUnit on
Microsoft.Testing.Platform; the exact-pin server fixture (`bun` source run of `external/opencode`).

**Spec:** the maintainer's decision register (§"Decisions") and the mechanism facts (§"Mechanism
facts") below are the binding input. Evidence: the read-only source study at `106629aa`
(`.scratchpad/persistent-pty/facts-106629aa.md` while it exists, mirrored in Memorizer as
"opencode-sdk-dotnet persistentPty protocol and daemon facts at upstream 106629aa"), research log
Q155, ADR-0013, ADR-0021, ADR-0022, `docs/architecture/client-runtime.md` §"PTY family ownership".
Every open question was resolved by the maintainer on 2026-08-29; nothing below is open.

## Global Constraints

- **Completion gate per task** (`docs/engineering/quality-gates.md`), from the repository root:

  ```bash
  dotnet tool run slopwatch analyze --exclude ".scratchpad/**,external/**" --fail-on warning
  dotnet build --configuration Release
  dotnet format whitespace --verify-no-changes --no-restore
  dotnet format style --verify-no-changes --no-restore --severity warn
  dotnet test --configuration Release --no-build
  ```

  Tasks that touch the generator, curation, the profile, or generated output add:

  ```bash
  dotnet run --file tools/opencode-tool.cs -- --help
  dotnet run --file tools/opencode-tool.cs -- generate --verify
  ```

  Slopwatch stays at **zero**: no suppressions, no `NoWarn`, no `Skip`, no empty catch. The
  analyzer wall is fail-closed — fix the code, never the rule (`.editorconfig` holds the standing
  per-rule arbitration pattern for a genuine misfire).
- **Commits:** one Conventional Commit per task, no AI-attribution trailers, inside the agreed
  development loop (green gate before every commit). **Never push**; the three-OS hosted run in
  Task 7 is maintainer-triggered — finish the local gate, ask the maintainer to push, report the
  matrix result before claiming the task complete.
- **Canon edits pre-approved by this plan's review (2026-08-29), to be written verbatim in Task 8:**
  ADR-0021 scope paragraph; `docs/architecture/client-runtime.md` "Persistent PTY family"
  section; `docs/architecture/protocol-and-generation.md` base64 sentence; `CONTEXT.md` terms;
  `docs/engineering/testing-style.md` platform-gated-leg sentence. Any other canon change is a
  deviation-protocol stop, not an executor's call.
- **ADR-0013 boundary:** `external/opencode` is read-only evidence. Facts that live only in
  upstream source (the ticket sentinel `"1"`, frame shapes, close codes, cursor domain, input
  framing, daemon behavior) inform hand-written runtime code and its tests, never curation or
  generated output. Facts the pinned document carries drive generation.
- **Generated output is never hand-edited.** The manifest identifies generator-owned files;
  `generate` rewrites them, `generate --verify` proves they are current.
- **Coding/testing conventions** (`docs/engineering/coding-style.md`, `testing-style.md`):
  `ConfigureAwait(false)` in product code (tests exempt); the mocking convention on every new
  public door and working object (protected parameterless constructor, `virtual` members,
  `MockSeam` guards — the `PtySession`/`PtyClient` shape); test data in fixtures/builders/constants,
  never inline dumps; fakes only for published contracts (`IPtyWebSocket` is the scripted seam);
  `CultureInfo.InvariantCulture` for every number rendering; `{Symbol}_Should_{Expected}[_When_{Condition}]`.
- **Platform truth:** the daemon `opencode-pty` (npm `@opencode-ai/pty` 0.1.13) ships darwin/linux
  packages only; on Windows at this pin `create` answers the declared 503 and every other route
  answers its daemon-absent arm. Live legs assert the arm the platform can reach; nothing skips.
- **Pending-operation accounting:** the committed marker `src/OpenCode.Sdk/.generation-incomplete`
  starts this arc at `112 selected / 23 pending / 1 transport-owned` and must read
  `122 selected / 12 pending / 2 transport-owned` when Task 5 lands.

## Decisions (maintainer, 2026-08-29) — binding

| # | Decision |
|---|---|
| D1 | **Ownership:** ADR-0021 pattern, second application — `emission: internalRaw`, hand-written `PersistentPtysClient` / `PersistentPtyClient` / `PersistentPtySession`. Recorded as a **scope extension of ADR-0021** (no new ADR). |
| D2 | **Placement:** one group row, handle keyed by `ptyID`. The session-keyed operations (`list`, `create`, `read`) and the unkeyed lifecycle operations (`handoff`, `shutdown`) sit on the collection client with their route values as ordinary arguments — the mechanical ADR-0019 rule, and exactly how upstream flattens the group with `sessionID` as an input field. |
| D3 | **`shutdown` / `handoff` are public** doors with XML docs naming them server-lifecycle operations. |
| D4 | **Bytes, not strings:** terminal output on the socket is `ReadOnlyMemory<byte>`; `snapshot.checkpoint` and `resized.checkpoint` are `ReadOnlyMemory<byte>`. In the generator this is the general rule `contentEncoding: base64` → `ReadOnlyMemory<byte>` (a represented token conversion, ADR-0014); other encodings fail closed. |
| D5 | **`ConnectAsync` awaits `attached`:** the door returns only after the server's `attached` frame; the session exposes it as `Attachment`; a 4404 close before it surfaces as `OpenCodeTransportException` from `ConnectAsync`; `inputProtocol != 1` is a protocol failure at connect. |
| D6 | **Unknown control frame `type` → `PersistentPtyUnknownFrame`** carrying the type and the raw JSON (ADR-0009's tolerance for additive drift); malformed JSON or a missing `type` is a protocol failure. |
| D7 | **Daemon gate = branch assertion, never skip:** on a platform without the daemon the live leg asserts the 503 `ServiceUnavailableError` (`service: "opencode-pty"`) arm; where the daemon runs it asserts the full flow. Both branches assert. |
| Defaults | `PersistentPtyConnectOptions.Role = Controller`, `Takeover = false` (the server's own defaults); `input_protocol=1` always; cursor relayed only (`ReplayComplete.EndOffset`, `Exited.FinalOffset`), never counted; names `PersistentPtys*` mechanically from the group; shared core extracted (not copied); fixture explicit-endpoint mode + WSL2 recipe for Windows workstations. |

## Mechanism facts the tasks argue from (source-verified at `106629aa`)

Paths relative to `external/opencode/`: **group** `packages/protocol/src/groups/persistent-pty.ts`,
**handler** `packages/server/src/handlers/persistent-pty.ts`, **core**
`packages/core/src/persistent-pty/index.ts`, **daemon** `packages/core/src/persistent-pty/daemon.ts`.

### Operations (pin-visible shapes; handler arms beyond the declared 400/401)

| Operation | Method / path | Key | Request | Success | Extra arms and triggers |
|---|---|---|---|---|---|
| `list` | GET `/api/experimental/session/{sessionID}/terminal` | sessionID | — | 200 `{data: Info[]}` | daemon absent → `[]`; 503 daemon error |
| `create` | POST same | sessionID | body `CreateInput` (required `args`, `title`, `env`; optional `command`, `cwd`, `size`) | 200 `{data: Info}` | **503** when the daemon cannot be found or started (the only route that starts it) |
| `read` | GET `…/terminal/read` | sessionID | query `lines` (document: string; source: int 1..65535) | 200 `{data: ReadResult \| null}` | 200 `null` when no current terminal; 400 bad `lines` |
| `get` | GET `/api/experimental/persistent-pty/{ptyID}` | ptyID | — | 200 `{data: Info}` | 404 unknown id **or daemon absent**; 503 |
| `update` | PUT same | ptyID | body `UpdateInput` (required `size`, optional `attachmentID`) | 200 `{data: Info}` | 404 / 503 |
| `remove` | DELETE same | ptyID | — | 204 | 404 / 503 |
| `snapshot` | GET `…/{ptyID}/snapshot` | ptyID | — | 200 `{data: {info, text, checkpoint(base64), cursor{x,y}}}` | 404 / 503 |
| `connectToken` | POST `…/{ptyID}/connect-token` | ptyID | header `x-opencode-ticket` (document: optional string) | 200 `{data: {ticket, expires_in}}` | 403 header ≠ `"1"` or origin, checked **before** existence; 404 / 503 |
| `handoff` | POST `/api/experimental/persistent-pty/handoff` | — | — | 200 `{handoff: Handoff \| null}` | 503 |
| `shutdown` | POST `/api/experimental/persistent-pty/shutdown` | — | — | 204 | 503 |
| `connect` | GET `…/{ptyID}/connect` (`x-websocket`) | ptyID | queries `cursor`, `role`, `attachment_id`, `takeover`, `input_protocol`, `ticket` | 101 | 403 ticket/origin; 400 bad cursor; **no pre-upgrade 404** |

Error bodies: `ServiceUnavailableError {_tag, message, service: "opencode-pty"}`;
`PtyNotFoundError {_tag, ptyID, message}`; `ForbiddenError {message}`. No location resolution
anywhere in the family. The ticket machinery is the normal family's (`PtyTicket.Service`, header
`x-opencode-ticket: 1`, 60 s TTL, single use) with scope `{ptyID}` only.

### The `connect` socket (source-only; hand-written knowledge)

- **Queries:** `cursor` = `Number(query ?? "0")`, must be a safe integer ≥ 0 else HTTP 400 before
  upgrade (no `-1`); `role` = `"observer"` exactly → observer, else controller; `attachment_id` =
  any string, else a server UUID; `takeover` = `"true"` exactly; `input_protocol` = `"1"` → framed.
- **Existence is not checked before upgrade:** a missing terminal or an absent/failed daemon
  upgrades, then closes `4404 "terminal unavailable"`. Close `1000` follows `exited` or any
  daemon-side stream end.
- **Outbound, in order:** text `attached` `{type, attachmentID, inputProtocol: 0|1, info: Info,
  role: "controller"|"observer", generation, replay: {requestedOffset, availableOffset,
  endOffset, truncated}}`; one **binary** replay message (only when non-empty, unchunked); text
  `replay_complete` `{type, endOffset}`; then live frames: **binary** raw output bytes, text
  `resized` `{type, cols, rows, generation, checkpoint: base64}`, `exited` `{type, exitCode?,
  finalOffset}`, `controller_changed` `{type, attachmentID?, generation}`, `title_changed` `{type,
  title}`, `foreground_process_changed` `{type, process: string|null}`.
- **Inbound (framed, `input_protocol=1`):** binary `[type u8][cols u16 BE][rows u16 BE][data…]`;
  `type 0` = control (resize/claim, no data), `type 1` = input; frames shorter than 5 bytes or
  with `cols == 0`/`rows == 0` are ignored; anything sent before `attached` is dropped. Upstream's
  TUI refuses a server whose `attached.inputProtocol` ≠ 1.
- **Cursor:** per-frame offsets are not on the wire; `replay_complete.endOffset` and
  `Info.output.tail` are the only resume anchors (upstream resumes with `cursor = output.tail`
  after restoring `snapshot.checkpoint` into an emulator sized to `info.size`).

### Normal PTY vs persistent PTY (the shared-core verdict)

Shareable verbatim: upgrade with `Authorization` (and `CollectHttpResponseDetails` on `NET`),
receive loop and fragment reassembly, close-frame detection, the send gate, bounded graceful close,
idempotent dispose, single active enumeration, `FailurePhase.PtyWebSocketRead/Write` mapping.
Family-specific: frame decode (text/binary roles are inverted between the families), close-status
wording (4404 means different things), upgrade-failure wording (persistent has a 400 arm and no
404 arm), the send message type (normal sends text, persistent sends binary), URI builder (path,
query set, no location), options (cursor domain), input encoding.

## Names this plan fixes

Mechanical derivations (`OperationNamePolicy`): group `persistentPty` → `PersistentPty`; verb = a
recognized final segment or the HTTP method; response name folds `Get`. **Raw names are
predictions; after `generate` in Task 2 the executor mirrors the emitted signatures exactly.**

| Operation | Raw method (internal) | Response type | Request type | Public door |
|---|---|---|---|---|
| `list` | `ListPersistentPtysAsync(string sessionId, …)` (curation row) | `PersistentPtyListResponse` (`.PersistentPtys`, curation row) | — | `PersistentPtysClient.ListPersistentPtysAsync(sessionId, …)` |
| `create` | `CreatePersistentPtyAsync(string sessionId, PersistentPtyCreateRequest request, …)` | `PersistentPtyCreateResponse` (`.PersistentPty`) | `PersistentPtyCreateRequest` | `PersistentPtysClient.CreatePersistentPtyAsync(sessionId, request, …)` |
| `read` | `GetReadAsync(string sessionId, PersistentPtyReadRequest? request, …)` | `PersistentPtyReadResponse` (`.Read`, nullable) | `PersistentPtyReadRequest` (`Lines`) | `PersistentPtysClient.ReadAsync(sessionId, request, …)` |
| `handoff` | `PostHandoffAsync(…)` | `PersistentPtyHandoffPostResponse` (`.Handoff`, nullable) | — | `PersistentPtysClient.HandoffAsync(…)` |
| `shutdown` | `PostShutdownAsync(…)` | `PersistentPtyShutdownPostResponse` (204, no payload) | — | `PersistentPtysClient.ShutdownAsync(…)` |
| `get` | `GetPersistentPtyAsync(…)` | `PersistentPtyResponse` (`.PersistentPty`) | — | `PersistentPtyClient.GetPersistentPtyAsync(…)` |
| `update` | `PutUpdateAsync(PersistentPtyUpdatePutRequest request, …)` | `PersistentPtyUpdatePutResponse` (`.Update`) | `PersistentPtyUpdatePutRequest` | `PersistentPtyClient.UpdatePersistentPtyAsync(request, …)` |
| `remove` | `RemovePersistentPtyAsync(…)` | `PersistentPtyRemoveResponse` (204) | — | `PersistentPtyClient.RemovePersistentPtyAsync(…)` |
| `snapshot` | `GetSnapshotAsync(…)` | `PersistentPtySnapshotResponse` (`.Snapshot`) | — | `PersistentPtyClient.GetSnapshotAsync(…)` |
| `connectToken` | `PostConnectTokenAsync(string? xOpencodeTicket = null, …)` | `PersistentPtyConnectTokenPostResponse` (`.ConnectToken`) | — | `PersistentPtyClient.CreateConnectTokenAsync(…)` |
| `connect` | — (transport-owned, Task 5) | — | — | `PersistentPtyClient.ConnectAsync(PersistentPtyConnectOptions?, …)` → `PersistentPtySession` |

Generated models (namespace `OpenCode.Sdk.Models`, `*Encoded` suffix stripped): `PersistentPtyInfo`
(`Id`, `Title`, `Command`, `Args`, `Cwd`, `Status`, `Pid`, `ExitCode?`, `SessionId`,
`ForegroundProcess?`, `Size` → promoted `PersistentPtyInfoSize {Cols, Rows}`, `Output` → promoted
`PersistentPtyInfoOutput {Head, Tail}`), `PersistentPtyReadResult`, `PersistentPtySnapshot`
(`Info`, `Text`, `Checkpoint: ReadOnlyMemory<byte>`, `Cursor`), `PersistentPtyHandoff` (`Directory`,
`InstanceId`, `Ticket`, `ExpiresAt: double`), `PtyTicketConnectToken` (already exists). Promoted
inline names follow the emitter's deterministic rule; the executor reads them off the generated
files rather than this table where they differ.

Hand-written public types (all registered in `ReservedNamePolicy.SpineTypeNames`):
`PersistentPtysClient`, `PersistentPtyClient`, `PersistentPtySession`, `PersistentPtyAttachment`,
`PersistentPtyReplayBounds`, `PersistentPtyRole`, `PersistentPtyConnectOptions`,
`PersistentPtyFrame`, `PersistentPtyAttachedFrame`, `PersistentPtyOutputFrame`,
`PersistentPtyReplayCompleteFrame`, `PersistentPtyResizedFrame`, `PersistentPtyExitedFrame`,
`PersistentPtyControllerChangedFrame`, `PersistentPtyTitleChangedFrame`,
`PersistentPtyForegroundProcessChangedFrame`, `PersistentPtyUnknownFrame`.

---

### Task 1: Generator — base64 strings materialize as `ReadOnlyMemory<byte>`

**Files:**
- Create: `tools/OpenCode.Sdk.Tools/Generator/Binding/Models/BinaryTypeReferencePlan.cs`
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Binding/TypePlanBinder.cs` (the `BindCore` switch)
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Binding/TypeReferenceNamePolicy.cs`
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Binding/StructuralUnionPlanBinder.cs` (`ArmName`
  and the token-kind map: a binary arm refuses)
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Emission/TypeSyntaxEmitter.cs`
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Emission/ModelEmitter.cs` (`CollectTypeUsings`)
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Emission/StructuralUnionEmitter.cs`
  (`IsReferenceType`: `BinaryTypeReferencePlan => false`)
- Test: `tests/OpenCode.Sdk.Tools.Tests/Generator/Binding/SpecBinderTests.cs` (the existing
  EncodedStringNode refusal test at ≈ lines 360–379 flips; one new refusal test)
- Regenerate: `src/OpenCode.Sdk/.generation-incomplete` (`persistentPty.snapshot` flips to
  `[bindable]`; no generated source changes because no selected operation carries an encoded string)

**Interfaces:**
- Produces: `BinaryTypeReferencePlan : TypeReferencePlan` (`IsCollection == false`), emitted as
  `ReadOnlyMemory<byte>` (nullable → `ReadOnlyMemory<byte>?`), formatted by
  `TypeReferenceNamePolicy.Format` as `"ReadOnlyMemory<byte>"`, requiring `using System;`.
- Consumed by: Task 2 (`PersistentPtySnapshot.Checkpoint` compiles through the source-generated
  registry on every TFM — that build is this task's compile proof).

- [ ] **Step 1: Flip the existing refusal test into the binding assertion**

Keep the arrangement of the existing test (a `Snapshot` schema whose `checkpoint` property is
`type: string, format: byte, contentEncoding: base64`, referenced by `v2.health.get`'s 200) and
replace its assertion:

```csharp
    [Test]
    public async Task Bind_Should_Materialize_A_Base64_String_As_Bytes()
    {
        // arrangement unchanged from the former refusal test
        var plan = new BindingTestHost().Bind(document, Selection("v2.health.get"), curation);

        var checkpoint = plan.Models
            .Single(static model => model.Name == "Snapshot")
            .Properties
            .Single(static property => property.WireName == "checkpoint");
        await Assert.That(TypeReferenceNamePolicy.Format(checkpoint.Type)).IsEqualTo("ReadOnlyMemory<byte>");
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Content_Encoding_Other_Than_Base64()
    {
        var document = await IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("Snapshot", schema => schema
                .Type("object")
                .Property("checkpoint", property => property
                    .Type("string")
                    .Format("byte")
                    .Raw("contentEncoding", "\"base32\""), required: true)
                .AdditionalPropertiesFalse())
            .WithOperation("v2.health.get", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("Snapshot")))));
        var curation = Curation(Groups("health", RootGroup()));

        var exception = Assert.Throws<BindingException>(
            () => _ = new BindingTestHost().Bind(document, Selection("v2.health.get"), curation));

        var error = exception.Errors.Single(static error => error.Problem.Contains("content encoding", StringComparison.Ordinal));
        await Assert.That(error.Problem).Contains("base32");
    }
```

(`WithOperation`'s configure parameter name: match the existing test ten lines above.)

- [ ] **Step 2: Run the two tests; expect the first to fail on the refusal and the second on the message**

Run: `dotnet test tests/OpenCode.Sdk.Tools.Tests --configuration Release -- --treenode-filter "/*/*/SpecBinderTests/Bind_Should_*Base64*"`
Expected: FAIL (binding refuses `EncodedStringNode`; the second finds no "content encoding" error).

- [ ] **Step 3: Add the plan kind and the binder case**

`tools/OpenCode.Sdk.Tools/Generator/Binding/Models/BinaryTypeReferencePlan.cs`:

```csharp
namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

/// <summary>
/// A base64-encoded string on the wire, materialized as bytes. The conversion is a represented
/// token conversion the runtime performs (ADR-0014) — System.Text.Json decodes base64 natively
/// for <c>ReadOnlyMemory&lt;byte&gt;</c> — so the generated shape stays faithful to the document
/// without a converter of its own.
/// </summary>
internal sealed record BinaryTypeReferencePlan : TypeReferencePlan
{
    public override bool IsCollection => false;
}
```

`TypePlanBinder.BindCore` — insert before the `_ =>` default:

```csharp
        EncodedStringNode { ContentEncoding: "base64" } => Binary(),
        EncodedStringNode encoded => Refuse(
            subject,
            $"content encoding '{encoded.ContentEncoding}' is not supported by the emitter; only base64 materializes as bytes"),
```

and beside the other factories:

```csharp
    private static BinaryTypeReferencePlan Binary() =>
        new()
        {
            IsNullable = false,
            JsonNullRepresentation = JsonNullRepresentation.ClrNull,
        };
```

- [ ] **Step 4: Teach every plan-kind switch the new kind**

`TypeSyntaxEmitter.Emit`:

```csharp
            BinaryTypeReferencePlan => Generic(
                "ReadOnlyMemory",
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ByteKeyword))),
```

`TypeReferenceNamePolicy.Format`: `BinaryTypeReferencePlan => "ReadOnlyMemory<byte>",`

`ModelEmitter.CollectTypeUsings`: add `case BinaryTypeReferencePlan: _ = usings.Add("System"); break;`

`StructuralUnionEmitter.IsReferenceType`: `BinaryTypeReferencePlan => false,` (it is a struct).

`StructuralUnionPlanBinder`: a binary arm is refused fail-closed — in `ArmName` add
`BinaryTypeReferencePlan => throw new InvalidOperationException("Binary arms are refused before naming.")`
only if the binder can reach naming; the real wall goes where arms are bound: refuse with the
message `"structural union arm 'X' is a base64 string; binary arms are not supported"` (read the
binder's arm-binding path and place the refusal beside the existing arm refusals; add a test in
`StructuralUnionPlanBinderTests` or the nearest structural-union test class proving the refusal
message). `SerializerTypeNamePolicy.ContextPropertyName` keeps its `_ => throw` — a bare binary
payload root has no consumer and stays a loud wall.

- [ ] **Step 5: Run the Tools tests; expect green**

Run: `dotnet test tests/OpenCode.Sdk.Tools.Tests --configuration Release`
Expected: PASS, the two tests included.

- [ ] **Step 6: Regenerate and verify**

Run: `dotnet run --file tools/opencode-tool.cs -- generate` then `git diff --stat`
Expected: only `src/OpenCode.Sdk/.generation-incomplete` changes — the line
`- v2.persistentPty.snapshot [refused: schema node 'EncodedStringNode' …]` becomes
`- v2.persistentPty.snapshot [bindable]`. Then `generate --verify` reports current.

- [ ] **Step 7: Full gate, then commit**

```bash
git add tools tests/OpenCode.Sdk.Tools.Tests src/OpenCode.Sdk/.generation-incomplete
git commit -m "feat(tools): materialize base64 strings as ReadOnlyMemory<byte>"
```

---

### Task 2: The HTTP family lands — curation, selection, hand-written doors, contract tests

**Files:**
- Modify: `tools/curation.json` (`groups.persistentPty`; `operationNames` row for `list`;
  `envelopePayloadNames["v2.persistentPty.list"] = "PersistentPtys"`)
- Modify: `tools/generation-profile.txt` (+10 ids, alphabetical: `v2.persistentPty.connectToken`,
  `.create`, `.get`, `.handoff`, `.list`, `.read`, `.remove`, `.shutdown`, `.snapshot`, `.update`)
- Regenerate: `src/OpenCode.Sdk/PersistentPtys/PersistentPtysRawClient.cs`, `PersistentPtyRawClient.cs`,
  request/response/model files, routes, adapters, registry, manifest, marker (`122 / 13 / 1`)
- Create: `src/OpenCode.Sdk/PersistentPtys/PersistentPtysClient.cs`, `src/OpenCode.Sdk/PersistentPtys/PersistentPtyClient.cs`
- Create: `src/OpenCode.Sdk/Internal/PtyTicketHeader.cs` (the sentinel, shared by both families)
- Modify: `src/OpenCode.Sdk/Ptys/PtyClient.cs` (use `PtyTicketHeader.Sentinel`; drop the private const)
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Binding/ReservedNamePolicy.cs` (`SpineTypeNames`
  + `"PersistentPtyClient"`, `"PersistentPtysClient"`)
- Modify: `src/OpenCode.Sdk.Extensions/OpenCodeServiceCollectionExtensions.cs` (+ `PersistentPtysClient` registration)
- Modify: `tests/OpenCode.Sdk.Tests/Snapshots/PublicApi.verified.txt` (accept the additive received baseline)
- Test: `tests/OpenCode.Sdk.Tests/PersistentPtys/PersistentPtysClientContractTests.cs`
- Test data: `tests/OpenCode.Sdk.Tests/Fixtures/Serialization/known-persistent-pty.json`,
  `known-persistent-pty-read.json`, `known-persistent-pty-snapshot.json`,
  `known-persistent-pty-handoff.json`; `tests/OpenCode.Sdk.Tests/Support/WireBodyData.cs`
  (+ `ServiceUnavailableError`, `PersistentPtyConnectTokenBody`)

**Interfaces:**
- Consumes: Task 1 (`Checkpoint` emits as `ReadOnlyMemory<byte>`); the generated raw clients.
- Produces: `PersistentPtysClient` with `GetPersistentPtyClient(string ptyId)`,
  `ListPersistentPtysAsync`, `CreatePersistentPtyAsync`, `ReadAsync`, `HandoffAsync`,
  `ShutdownAsync`; `PersistentPtyClient` with `GetPersistentPtyAsync`, `UpdatePersistentPtyAsync`,
  `RemovePersistentPtyAsync`, `GetSnapshotAsync`, `CreateConnectTokenAsync` (the `ConnectAsync`
  door arrives in Task 4); `OpenCodeClient.PersistentPtys` (generated accessor);
  `internal static class PtyTicketHeader { public const string Sentinel = "1"; }`.

- [ ] **Step 1: Curation rows**

`tools/curation.json` → `groups` (alphabetical position is irrelevant; the object is keyed):

```json
    "persistentPty": {
      "placement": "client",
      "clientName": "PersistentPtys",
      "handleName": "PersistentPtyClient",
      "handleParameter": "ptyID",
      "emission": "internalRaw",
      "reason": "A persistent terminal is a live daemon-owned process worked over its id: get, update, snapshot, remove, connect-token, and the WebSocket connect chain on ptyID (ADR-0019); the session-keyed list, create, and read and the bare handoff and shutdown stay on the collection client with their route values as arguments, exactly as upstream flattens the group with sessionID as an input field. Every public door of the family is hand-written over these raw clients because the connect-token handshake and the WebSocket session need knowledge generation may not import (ADR-0021, extended to this family)."
    }
```

`operationNames` (append; rows are ordered by operationId in the file):

```json
    {
      "operationId": "v2.persistentPty.list",
      "methodName": "ListPersistentPtysAsync",
      "reason": "The group singular 'persistentPty' does not pluralize naively; the reviewed .NET domain name for a set of persistent pseudo-terminals is 'PersistentPtys', exactly like v2.pty.list's 'Ptys'."
    }
```

`envelopePayloadNames`: `"v2.persistentPty.list": "PersistentPtys"`.

- [ ] **Step 2: Select the ten HTTP operations and generate**

Append the ten ids to `tools/generation-profile.txt` in sorted order, then run
`dotnet run --file tools/opencode-tool.cs -- generate`.
Expected: generation succeeds (every wall the telltale predicted is clear: `list` through the two
rows, `connectToken` through `internalRaw`, `snapshot` through Task 1); the marker reads
`Selected operations: 122`, `Pending operations: 13`, `Transport-owned operations: 1`; new files
under `src/OpenCode.Sdk/PersistentPtys/`; `OpenCodeClient` gains the `PersistentPtys` accessor.
`dotnet build` now **fails**: `PersistentPtysClient` does not exist yet. If `generate` refuses
anything, stop and report the exact message — a wall here is a plan defect, not something to route
around.

- [ ] **Step 3: Extract the ticket sentinel**

`src/OpenCode.Sdk/Internal/PtyTicketHeader.cs`:

```csharp
namespace OpenCode.Sdk.Internal;

/// <summary>
/// Knowledge source: upstream-observed — both PTY families' connect-token handlers require the
/// <c>x-opencode-ticket</c> header to carry exactly this value; it exists only in upstream
/// implementation source (ADR-0013/0021), so it lives here in hand-written runtime code and never
/// in curation or generated output.
/// </summary>
internal static class PtyTicketHeader
{
    public const string Sentinel = "1";
}
```

In `PtyClient.cs` delete the private `PtyTicketSentinel` constant and pass
`xOpencodeTicket: PtyTicketHeader.Sentinel`.

- [ ] **Step 4: Write the collection door**

`src/OpenCode.Sdk/PersistentPtys/PersistentPtysClient.cs` — mirror the generated raw signatures
exactly (parameter names, nullability, defaults):

```csharp
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk;

/// <summary>
/// The 'PersistentPtysClient' collection client. Every public door of the persistent PTY family
/// is hand-written over the generated internal raw clients (ADR-0021): the family's center of
/// gravity is a daemon-owned terminal worked through a live WebSocket session and a token
/// handshake whose knowledge the pinned document does not carry, so the surface is owned here
/// while route, status, and schema drift still breaks compilation through
/// <see cref="PersistentPtysRawClient"/>. The session-keyed operations take the session id as an
/// argument, exactly as upstream flattens the group.
/// </summary>
public class PersistentPtysClient
{
    private readonly ConnectionSnapshot? _connection;
    private readonly PersistentPtysRawClient? _raw;

    internal PersistentPtysClient(Pipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _raw = new PersistentPtysRawClient(pipeline);
        _connection = pipeline.Connection;
    }

    /// <summary>
    /// Initializes a mocking instance; members invoked without an override throw an instructive failure.
    /// </summary>
    protected PersistentPtysClient()
    {
    }

    /// <summary>Gets a bound 'PersistentPtyClient'; the handle never caches server state.</summary>
    /// <param name="ptyId">The 'ptyID' route value.</param>
    /// <returns>The bound 'PersistentPtyClient'.</returns>
    public virtual PersistentPtyClient GetPersistentPtyClient(string ptyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ptyId);
        if (ptyId is "." or "..")
        {
            throw new ArgumentException("Route values must not be dot segments.", nameof(ptyId));
        }

        return new PersistentPtyClient(Raw.GetPersistentPtyRawClient(ptyId), Connection, ptyId);
    }

    /// <summary>
    /// List the session's persistent terminals. Answers an empty list when the opencode-pty
    /// daemon is not running.
    /// </summary>
    public virtual Task<PersistentPtyListResponse> ListPersistentPtysAsync(string sessionId,
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
        Raw.ListPersistentPtysAsync(sessionId, requestOptions, cancellationToken);

    /// <summary>
    /// Create a persistent terminal for the session. This is the one operation that starts the
    /// opencode-pty daemon; on a platform without the daemon it answers the declared 503
    /// <see cref="ServiceUnavailableError"/> whose service is <c>opencode-pty</c>.
    /// </summary>
    public virtual Task<PersistentPtyCreateResponse> CreatePersistentPtyAsync(string sessionId,
        PersistentPtyCreateRequest request, OpenCodeRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        Raw.CreatePersistentPtyAsync(sessionId, request, requestOptions, cancellationToken);

    /// <summary>
    /// Read the last rows of the session's most recently controlled terminal. The payload is null
    /// when the session has no current terminal.
    /// </summary>
    public virtual Task<PersistentPtyReadResponse> ReadAsync(string sessionId,
        PersistentPtyReadRequest? request = null, OpenCodeRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        Raw.GetReadAsync(sessionId, request, requestOptions, cancellationToken);

    /// <summary>
    /// Server-lifecycle operation: prepare a daemon handoff so the terminals outlive this server
    /// until a replacement claims them or the handoff expires. The payload is null when this
    /// server owns no daemon.
    /// </summary>
    public virtual Task<PersistentPtyHandoffPostResponse> HandoffAsync(
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
        Raw.PostHandoffAsync(requestOptions, cancellationToken);

    /// <summary>
    /// Server-lifecycle operation: stop the daemon and every terminal it owns. Answers 204 even
    /// when no daemon is running.
    /// </summary>
    public virtual Task<PersistentPtyShutdownPostResponse> ShutdownAsync(
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
        Raw.PostShutdownAsync(requestOptions, cancellationToken);

    private ConnectionSnapshot Connection => _connection ?? throw MockSeam.CreateError("PersistentPtysClient", "Snapshot");

    private PersistentPtysRawClient Raw => _raw ?? throw MockSeam.CreateError("PersistentPtysClient", "RawClient");
}
```

Every public member carries the full XML doc block the normal family uses (`<param>`,
`<returns>`, the two `<exception>` lines naming the declared statuses) — copy the shape from
`PtysClient.cs`; the summaries above are the content.

- [ ] **Step 5: Write the handle door (HTTP doors only)**

`src/OpenCode.Sdk/PersistentPtys/PersistentPtyClient.cs`:

```csharp
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk;

/// <summary>
/// A bound 'PersistentPtyClient' handle; it holds a <see cref="PersistentPtyRawClient"/> and the
/// <see cref="ConnectionSnapshot"/> the WebSocket door needs (ADR-0021). Every represented
/// response rides the generic envelope machinery; the handle adds only the connect-token
/// sentinel and, in the WebSocket door, the protocol the pinned document cannot describe.
/// </summary>
public class PersistentPtyClient
{
    private readonly ConnectionSnapshot? _connection;
    private readonly string? _ptyId;
    private readonly PersistentPtyRawClient? _raw;

    internal PersistentPtyClient(PersistentPtyRawClient raw, ConnectionSnapshot connection, string ptyId)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(ptyId);

        _raw = raw;
        _connection = connection;
        _ptyId = ptyId;
    }

    /// <summary>
    /// Initializes a mocking instance; members invoked without an override throw an instructive failure.
    /// </summary>
    protected PersistentPtyClient()
    {
    }

    /// <summary>Get one persistent terminal. 404 also answers when the daemon is not running.</summary>
    public virtual Task<PersistentPtyResponse> GetPersistentPtyAsync(
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
        Raw.GetPersistentPtyAsync(requestOptions, cancellationToken);

    /// <summary>Resize one persistent terminal; the resize also selects it as the session's current terminal.</summary>
    public virtual Task<PersistentPtyUpdatePutResponse> UpdatePersistentPtyAsync(PersistentPtyUpdatePutRequest request,
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
        Raw.PutUpdateAsync(request, requestOptions, cancellationToken);

    /// <summary>Terminate and remove one persistent terminal.</summary>
    public virtual Task<PersistentPtyRemoveResponse> RemovePersistentPtyAsync(
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
        Raw.RemovePersistentPtyAsync(requestOptions, cancellationToken);

    /// <summary>
    /// Snapshot one persistent terminal: its info, the retained text, the screen checkpoint as
    /// the terminal-escape bytes an emulator sized to <c>Info.Size</c> replays, and the cursor.
    /// </summary>
    public virtual Task<PersistentPtySnapshotResponse> GetSnapshotAsync(
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
        Raw.GetSnapshotAsync(requestOptions, cancellationToken);

    /// <summary>
    /// Create a short-lived single-use ticket for a browser's WebSocket upgrade. The ticket header
    /// the handler requires is applied internally and is never a caller's argument; the ticket is
    /// scoped to this terminal only.
    /// </summary>
    public virtual Task<PersistentPtyConnectTokenPostResponse> CreateConnectTokenAsync(
        OpenCodeRequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
        Raw.PostConnectTokenAsync(xOpencodeTicket: PtyTicketHeader.Sentinel, requestOptions, cancellationToken);

    private ConnectionSnapshot Connection => _connection ?? throw MockSeam.CreateError("PersistentPtyClient", "Snapshot");

    private string PtyId => _ptyId ?? throw MockSeam.CreateError("PersistentPtyClient", "PtyId");

    private PersistentPtyRawClient Raw => _raw ?? throw MockSeam.CreateError("PersistentPtyClient", "RawClient");
}
```

`Connection` and `PtyId` are consumed by Task 4's `ConnectAsync`; until then the analyzer wall
may flag them unused (IDE0051) — if it does, add them in Task 4 instead and keep only `Raw` here.

- [ ] **Step 6: Reserved names, DI, build**

`ReservedNamePolicy.SpineTypeNames`: add `"PersistentPtyClient"`, `"PersistentPtysClient"` in sorted
position. `OpenCodeServiceCollectionExtensions.AddOpenCodeCore`: add
`_ = services.AddSingleton(static PersistentPtysClient (provider) => provider.GetRequiredService<OpenCodeClient>().PersistentPtys);`
in alphabetical position. Run `dotnet build --configuration Release`; expected: green.

- [ ] **Step 7: Fixtures and wire constants**

`tests/OpenCode.Sdk.Tests/Fixtures/Serialization/known-persistent-pty.json`:

```json
{"id":"pty_persistent_7","title":"sdk terminal","command":"/bin/bash","args":["-l"],"cwd":"/","status":"running","pid":5150,"sessionID":"ses_1","foregroundProcess":null,"size":{"cols":80,"rows":24},"output":{"head":0,"tail":42}}
```

`known-persistent-pty-read.json`:

```json
{"ptyID":"pty_persistent_7","title":"sdk terminal","cwd":"/","foregroundProcess":"bash","screen":{"text":"$ echo hello\nhello\n","cols":80,"rows":24,"cursor":{"x":2,"y":2}}}
```

`known-persistent-pty-snapshot.json` (the checkpoint is base64 of the bytes `1B 63` — `ESC c`):

```json
{"info":{"id":"pty_persistent_7","title":"sdk terminal","command":"/bin/bash","args":["-l"],"cwd":"/","status":"running","pid":5150,"sessionID":"ses_1","foregroundProcess":null,"size":{"cols":80,"rows":24},"output":{"head":0,"tail":42}},"text":"$ echo hello\nhello\n","checkpoint":"G2M=","cursor":{"x":2,"y":2}}
```

`known-persistent-pty-handoff.json`:

```json
{"directory":"/tmp/opencode-pty-1000/3f2a","instanceID":"inst_1","ticket":"hnd_1","expiresAt":1756450000000}
```

`WireBodyData.cs` additions:

```csharp
    public const string ServiceUnavailableError =
        "{\"_tag\":\"ServiceUnavailableError\",\"message\":\"opencode-pty could not be started\",\"service\":\"opencode-pty\"}";

    public const string PersistentPtyConnectTokenBody = "{\"ticket\":\"tkt_p1\",\"expires_in\":60}";
```

- [ ] **Step 8: Contract tests — every declared arm of every door**

`tests/OpenCode.Sdk.Tests/PersistentPtys/PersistentPtysClientContractTests.cs`. Representative
tests in full; the table after them is the complete inventory the file must contain.

```csharp
using System.Net;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

public sealed class PersistentPtysClientContractTests
{
    private const string TicketHeader = "x-opencode-ticket";

    private static readonly Uri SessionTerminals = new("http://localhost:4096/api/experimental/session/ses_1/terminal");

    public static IEnumerable<Func<(string Name, Func<OpenCodeClient, Task> Door)>> EveryDoor() =>
    [
        static () => ("list", client => client.PersistentPtys.ListPersistentPtysAsync("ses_1")),
        static () => ("create", client => client.PersistentPtys.CreatePersistentPtyAsync("ses_1", CreateRequest())),
        static () => ("read", client => client.PersistentPtys.ReadAsync("ses_1")),
        static () => ("handoff", client => client.PersistentPtys.HandoffAsync()),
        static () => ("shutdown", client => client.PersistentPtys.ShutdownAsync()),
        static () => ("get", client => client.PersistentPtys.GetPersistentPtyClient("pty_persistent_7").GetPersistentPtyAsync()),
        static () => ("update", client => client.PersistentPtys.GetPersistentPtyClient("pty_persistent_7").UpdatePersistentPtyAsync(UpdateRequest())),
        static () => ("remove", client => client.PersistentPtys.GetPersistentPtyClient("pty_persistent_7").RemovePersistentPtyAsync()),
        static () => ("snapshot", client => client.PersistentPtys.GetPersistentPtyClient("pty_persistent_7").GetSnapshotAsync()),
        static () => ("connectToken", client => client.PersistentPtys.GetPersistentPtyClient("pty_persistent_7").CreateConnectTokenAsync()),
    ];

    [Test]
    public async Task CreatePersistentPtyAsync_Should_Send_The_Typed_Body_And_Return_The_Typed_Terminal()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-persistent-pty.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Envelope(payload));

        var response = await scenario.Client.PersistentPtys.CreatePersistentPtyAsync("ses_1", CreateRequest());

        await Assert.That(response.PersistentPty.Id).IsEqualTo("pty_persistent_7");
        await Assert.That(response.PersistentPty.SessionId).IsEqualTo("ses_1");
        await Assert.That(response.PersistentPty.Output.Tail).IsEqualTo(42);
        var request = scenario.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(request.RequestUri).IsEqualTo(SessionTerminals);
        await Assert.That(request.Body).IsEqualTo("{\"args\":[\"-l\"],\"title\":\"sdk terminal\",\"env\":{}}");
    }

    [Test]
    public async Task CreatePersistentPtyAsync_Should_Throw_The_Declared_503_Daemon_Arm()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.ServiceUnavailable, WireBodyData.ServiceUnavailableError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.PersistentPtys.CreatePersistentPtyAsync("ses_1", CreateRequest()))
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(503);
        var error = (ServiceUnavailableError)exception.Error!;
        await Assert.That(error.Service).IsEqualTo("opencode-pty");
    }

    [Test]
    public async Task CreatePersistentPtyAsync_Should_Return_The_503_Daemon_Arm_On_The_NoThrow_Spine()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.ServiceUnavailable, WireBodyData.ServiceUnavailableError);

        var response = await scenario.Client.PersistentPtys.CreatePersistentPtyAsync(
            "ses_1", CreateRequest(), OpenCodeRequestOptions.NoThrow);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Status).IsEqualTo(503);
        await Assert.That(response.Error).IsTypeOf<ServiceUnavailableError>();
        await Assert.That(response.RawBody).IsEqualTo(WireBodyData.ServiceUnavailableError);
    }

    [Test]
    public async Task ReadAsync_Should_Materialize_A_Null_Payload_As_No_Current_Terminal()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Envelope("null"));

        var response = await scenario.Client.PersistentPtys.ReadAsync("ses_1", new PersistentPtyReadRequest { Lines = "40" });

        await Assert.That(response.IsError).IsFalse();
        await Assert.That(response.Read).IsNull();
        await Assert.That(scenario.Requests.Single().RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/experimental/session/ses_1/terminal/read?lines=40"));
    }

    [Test]
    public async Task GetSnapshotAsync_Should_Materialize_The_Checkpoint_As_Bytes()
    {
        var payload = new FixtureLoader().LoadJson("Serialization.known-persistent-pty-snapshot.json");
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Envelope(payload));

        var response = await scenario.Client.PersistentPtys.GetPersistentPtyClient("pty_persistent_7").GetSnapshotAsync();

        await Assert.That(response.Snapshot.Checkpoint.ToArray()).IsEquivalentTo(new byte[] { 0x1B, 0x63 });
        await Assert.That(response.Snapshot.Cursor.Y).IsEqualTo(2);
    }

    [Test]
    public async Task CreateConnectTokenAsync_Should_Send_The_Ticket_Sentinel_And_Materialize_The_Token()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.Envelope(WireBodyData.PersistentPtyConnectTokenBody));

        var response = await scenario.Client.PersistentPtys.GetPersistentPtyClient("pty_persistent_7").CreateConnectTokenAsync();

        await Assert.That(response.ConnectToken.Ticket).IsEqualTo("tkt_p1");
        var request = scenario.Requests.Single();
        await Assert.That(request.RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/experimental/persistent-pty/pty_persistent_7/connect-token"));
        await Assert.That(request.Headers[TicketHeader]).IsEqualTo("1");
        await Assert.That(request.Body).IsNull();
    }

    [Test]
    [MethodDataSource(nameof(EveryDoor))]
    public async Task Every_Door_Should_Throw_The_Declared_401_Error((string Name, Func<OpenCodeClient, Task> Door) door)
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.Unauthorized, WireBodyData.UnauthorizedError);

        var exception = await Assert.That(async () => await door.Door(scenario.Client)).Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(401);
        await Assert.That(exception.Error).IsTypeOf<UnauthorizedError>();
    }

    [Test]
    [MethodDataSource(nameof(EveryDoor))]
    public async Task Every_Door_Should_Throw_The_Declared_400_Error((string Name, Func<OpenCodeClient, Task> Door) door)
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.BadRequest, WireBodyData.InvalidRequestError);

        var exception = await Assert.That(async () => await door.Door(scenario.Client)).Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(400);
        await Assert.That(exception.Error).IsTypeOf<InvalidRequestError>();
    }

    private static PersistentPtyCreateRequest CreateRequest() => new()
    {
        Args = ["-l"],
        Title = "sdk terminal",
        Env = new Dictionary<string, string>(StringComparer.Ordinal),
    };

    private static PersistentPtyUpdatePutRequest UpdateRequest() => new()
    {
        Size = new PersistentPtyUpdatePutRequestSize { Cols = 120, Rows = 40 },
    };
}
```

(The promoted `Size` type name is whatever the generator emitted; read it off
`src/OpenCode.Sdk/PersistentPtys/PersistentPtyUpdatePutRequest.cs`.)

Complete inventory — one test per row, named `{Door}_Should_{Expected}`:

| Door | Arm | Scenario body | Assertions |
|---|---|---|---|
| list | 200 empty | `Envelope("[]")` | `PersistentPtys.Count == 0`; GET `SessionTerminals` |
| list | 200 one | `Envelope($"[{known}]")` | single `Id`; `SessionId == "ses_1"` |
| list | 503 | `ServiceUnavailableError` | throws 503, `ServiceUnavailableError` |
| create | 200 / 503 throw / 503 NoThrow | above | above |
| create | body with optional members | `Cwd = "/repo"`, `Size` set | body JSON includes `cwd` and `size` |
| read | 200 null | `Envelope("null")` | above |
| read | 200 result | `Envelope(known-read)` | `Read.Screen.Text` contains `hello`; `Read.PtyId` |
| read | 400 | `InvalidRequestError` | throws 400 |
| handoff | 200 null | `{"handoff":null}` | `Handoff` is null, `IsError` false |
| handoff | 200 value | `{"handoff":<known-handoff>}` | `Handoff.Ticket == "hnd_1"`, `ExpiresAt == 1756450000000d` |
| handoff | 503 | `ServiceUnavailableError` | throws 503 |
| shutdown | 204 | empty body, `NoContent` | `IsError` false, `Status == 204`; POST `/api/experimental/persistent-pty/shutdown` |
| shutdown | 503 | `ServiceUnavailableError` | throws 503 |
| get | 200 | `Envelope(known)` | `PersistentPty.Id`; GET `/api/experimental/persistent-pty/pty_persistent_7` |
| get | 404 | `PtyNotFoundError` | throws 404, `PtyNotFoundError` |
| update | 200 | `Envelope(known)` | PUT; body `{"size":{"cols":120,"rows":40}}` |
| update | 404 | `PtyNotFoundError` | throws 404 |
| remove | 204 | `NoContent` | DELETE; `Status == 204` |
| remove | 404 | `PtyNotFoundError` | throws 404 |
| snapshot | 200 / 404 | above / `PtyNotFoundError` | above / throws 404 |
| connectToken | 200 | above | above |
| connectToken | 403 | `ForbiddenError` | throws 403, `ForbiddenError` |
| connectToken | 404 | `PtyNotFoundError` | throws 404 |
| every door | 401 / 400 | data source | above |
| collection | `GetPersistentPtyClient` guards | — | blank, `"."`, `".."` throw `ArgumentException` |
| root | `client.PersistentPtys` | — | not null; DI roster test (`AddOpenCode_Should_Register_Every_Root_Client_Family`) stays green |

- [ ] **Step 9: Run the SDK tests; accept the PublicApi baseline**

Run: `dotnet test --configuration Release` (full). The `PublicApiBaselineTests` fails once with a
received file; review the received diff — it must be **additive only** (the two doors, the
generated `PersistentPtys` accessor, the models and envelopes) — then copy every TFM's
`PublicApi.received.txt` over `tests/OpenCode.Sdk.Tests/Snapshots/PublicApi.verified.txt` (they
are byte-identical across TFMs; verify with a diff before accepting). Re-run: green.

- [ ] **Step 10: Full gate incl. tool smoke and `generate --verify`, then commit**

```bash
git add tools/curation.json tools/generation-profile.txt tools/OpenCode.Sdk.Tools src/OpenCode.Sdk src/OpenCode.Sdk.Extensions tests
git commit -m "feat(sdk): land the persistent PTY HTTP family over generated raw clients"
```

---

### Task 3: Extract the family-neutral socket core from `PtySession`

**Files:**
- Create: `src/OpenCode.Sdk/Internal/ITerminalFrameDecoder.cs`, `ITerminalClosePolicy.cs`,
  `ITerminalUpgradeFailurePolicy.cs`, `TerminalSocketCore.cs`, `PtyFrameDecoder.cs`, `PtyClosePolicy.cs`
- Modify: `src/OpenCode.Sdk/Internal/IPtyWebSocket.cs` (`SendAsync` gains `WebSocketMessageType`),
  `ClientPtyWebSocket.cs` (`ConnectAsync` takes the upgrade policy; `SendAsync` passes the type),
  `PtyUpgradeFailurePolicy.cs` (becomes a sealed singleton implementing the seam),
  `src/OpenCode.Sdk/Ptys/PtySession.cs` (hosts the core), `src/OpenCode.Sdk/Ptys/PtyClient.cs` (passes the policy)
- Test: `tests/OpenCode.Sdk.Tests/Support/ScriptedPtyWebSocket.cs` (records message types),
  `tests/OpenCode.Sdk.Performance.Tests/Support/CannedPtyWebSocket.cs` (signature),
  `tests/OpenCode.Sdk.Tests/Ptys/PtySessionTests.cs` (+1 test; every existing test unchanged)

**Interfaces:**
- Produces:
  ```csharp
  internal interface ITerminalFrameDecoder<out TFrame> where TFrame : class
  { TFrame Decode(WebSocketMessageType messageType, byte[] payload, int count); }
  internal interface ITerminalClosePolicy
  { OpenCodeTransportException? Map(WebSocketCloseStatus? status, string? description); }
  internal interface ITerminalUpgradeFailurePolicy
  { OpenCodeTransportException Map(WebSocketException exception, int? status, string ptyId); }
  internal sealed class TerminalSocketCore<TFrame> : IAsyncDisposable where TFrame : class
  {
      public TerminalSocketCore(IPtyWebSocket socket, ITerminalFrameDecoder<TFrame> decoder, ITerminalClosePolicy closePolicy, Type owner);
      public bool IsDisposed { get; }
      public IAsyncEnumerable<TFrame> ReadAsync(CancellationToken cancellationToken);
      public Task SendAsync(ArraySegment<byte> payload, WebSocketMessageType messageType, CancellationToken cancellationToken);
      public ValueTask DisposeAsync();
  }
  // IPtyWebSocket.SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, CancellationToken cancellationToken)
  // ClientPtyWebSocket.ConnectAsync(Uri uri, string ptyId, ITerminalUpgradeFailurePolicy policy, CancellationToken cancellationToken)
  ```
- Consumed by: Task 4 (`PersistentPtySession` over `TerminalSocketCore<PersistentPtyFrame>`).
- PublicApi: **unchanged** — `PtySession`'s public members keep their exact signatures and documented behavior.

- [ ] **Step 1: Write the one new test (message type recorded), run it, expect a compile failure**

In `ScriptedPtyWebSocket` add `public IReadOnlyList<WebSocketMessageType> SentMessageTypes => _sentTypes;`
(a `List<WebSocketMessageType>` appended beside `_sent`) and change `SendAsync` to the new
signature. In `PtySessionTests`:

```csharp
    [Test]
    public async Task WriteAsync_Should_Send_A_Text_Message()
    {
        var socket = new ScriptedPtyWebSocket();
        await using var session = new PtySession(socket);

        await session.WriteAsync("ls\r");

        await Assert.That(socket.SentMessageTypes.Single()).IsEqualTo(WebSocketMessageType.Text);
        await Assert.That(socket.SentText.Single()).IsEqualTo("ls\r");
    }
```

Run: `dotnet build tests/OpenCode.Sdk.Tests` — Expected: FAIL (interface signature).

- [ ] **Step 2: The seams**

`ITerminalFrameDecoder.cs`, `ITerminalClosePolicy.cs`, `ITerminalUpgradeFailurePolicy.cs` as in
the Interfaces block, each with a summary saying which family fact it isolates. `IPtyWebSocket.SendAsync`
gains `WebSocketMessageType messageType` (doc: "Sends one complete message of the given type").
`ClientPtyWebSocket.SendAsync` passes `messageType` through both `#if` branches;
`ClientPtyWebSocket.ConnectAsync(Uri uri, string ptyId, ITerminalUpgradeFailurePolicy policy, CancellationToken cancellationToken)`
calls `policy.Map(exception, status, ptyId)` where it called the static policy.
`PtyUpgradeFailurePolicy` becomes `internal sealed class PtyUpgradeFailurePolicy : ITerminalUpgradeFailurePolicy`
with `public static PtyUpgradeFailurePolicy Instance { get; } = new();`, a private constructor, and
the existing `Map` as an instance method (body unchanged). `PtyClient.ConnectAsync` passes
`PtyUpgradeFailurePolicy.Instance`.

- [ ] **Step 3: The core**

`src/OpenCode.Sdk/Internal/TerminalSocketCore.cs` — `PtySession`'s current body, generalized.
Behavior is identical: same buffer size, same graceful-close bound, same disposal/`_reading`
semantics, same failure mapping; the only substitutions are `_decoder.Decode(...)` for
`PtyFrameReader.Read(...)`, `_closePolicy.Map(...)` for `PtyCloseFailurePolicy.Map(...)`, the
message type on send, and the owner type in the two messages:

```csharp
using System.Net.WebSockets;
using System.Runtime.CompilerServices;

namespace OpenCode.Sdk.Internal;

/// <summary>
/// The family-neutral WebSocket lifecycle both terminal sessions share: receive with fragment
/// reassembly, serialized sends, a bounded graceful close, idempotent disposal, and one active
/// read enumeration. What differs between families — how a message decodes, what a close status
/// means — rides the two seams; the owner type names the failures.
/// </summary>
internal sealed class TerminalSocketCore<TFrame> : IAsyncDisposable
    where TFrame : class
{
    private const int ReceiveBufferSize = 16 * 1024;

    private static readonly TimeSpan GracefulCloseTimeout = TimeSpan.FromSeconds(5);

    private readonly ITerminalClosePolicy _closePolicy;
    private readonly ITerminalFrameDecoder<TFrame> _decoder;
    private readonly Type _owner;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly IPtyWebSocket _socket;
    private int _disposed;
    private int _reading;

    public TerminalSocketCore(IPtyWebSocket socket, ITerminalFrameDecoder<TFrame> decoder, ITerminalClosePolicy closePolicy, Type owner)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(closePolicy);
        ArgumentNullException.ThrowIfNull(owner);

        _socket = socket;
        _decoder = decoder;
        _closePolicy = closePolicy;
        _owner = owner;
    }

    public bool IsDisposed => Volatile.Read(ref _disposed) is 1;

    public IAsyncEnumerable<TFrame> ReadAsync(CancellationToken cancellationToken) => ReadCoreAsync(cancellationToken);

    public async Task SendAsync(ArraySegment<byte> payload, WebSocketMessageType messageType, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, _owner);

        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(IsDisposed, _owner);
            try
            {
                await _socket.SendAsync(payload, messageType, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (FailureClassification.Handles(exception, FailurePhase.PtyWebSocketWrite))
            {
                throw FailureClassification.Map(exception, FailurePhase.PtyWebSocketWrite, cancellationToken);
            }
        }
        finally
        {
            _ = _sendGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is 1)
        {
            return;
        }

        try
        {
            _ = await TryCloseAsync().ConfigureAwait(false);
        }
        finally
        {
            _socket.Dispose();
        }
    }

    // TryCloseAsync and ReadCoreAsync: PtySession's bodies verbatim, with
    //   throw new InvalidOperationException($"A '{_owner.Name}' carries one active read enumeration; message reassembly cannot be shared across two.");
    //   var failure = _closePolicy.Map(_socket.CloseStatus, _socket.CloseStatusDescription);
    //   yield return _decoder.Decode(received.MessageType, buffer, received.Count);
    //   var assembled = _decoder.Decode(received.MessageType, assembly.Buffer, assembly.Length);
}
```

Every comment `PtySession` carries today (why the gate is not disposed, why the receive is guarded
alone, the disposal-vs-fault distinction) moves with the code it explains.

`PtyFrameDecoder`: `internal sealed class PtyFrameDecoder : ITerminalFrameDecoder<PtyFrame>` with
`public static PtyFrameDecoder Instance { get; } = new();` delegating to `PtyFrameReader.Read`.
`PtyClosePolicy`: same shape over `PtyCloseFailurePolicy.Map`.

- [ ] **Step 4: Re-host `PtySession`**

```csharp
public class PtySession : IAsyncDisposable
{
    private readonly TerminalSocketCore<PtyFrame>? _core;

    internal PtySession(IPtyWebSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _core = new TerminalSocketCore<PtyFrame>(socket, PtyFrameDecoder.Instance, PtyClosePolicy.Instance, typeof(PtySession));
    }

    protected PtySession()
    {
    }

    private TerminalSocketCore<PtyFrame> Core => _core ?? throw MockSeam.CreateError("PtySession", "WebSocket");

    public virtual IAsyncEnumerable<PtyFrame> ReadAsync(CancellationToken cancellationToken = default) => Core.ReadAsync(cancellationToken);

    public virtual Task WriteAsync(string input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var core = Core;
        return core.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(input)), WebSocketMessageType.Text, cancellationToken);
    }

    public virtual async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (_core is not null)
        {
            await _core.DisposeAsync().ConfigureAwait(false);
        }
    }
}
```

XML docs stay word-for-word. `PtySession`'s former `ReadCoreAsync`/`TryCloseAsync` are deleted.

- [ ] **Step 5: Run every PTY test, the benchmark project build, and the gate**

Run: `dotnet test --configuration Release` — Expected: PASS with every pre-existing
`PtySessionTests`, `PtysClientContractTests`, `PtyConnectOptionsTests` unchanged; `dotnet build`
builds `tests/OpenCode.Sdk.Performance.Tests` (`CannedPtyWebSocket.SendAsync` updated to the new
signature). PublicApi baseline unchanged (a change means a public signature moved — revert it).

- [ ] **Step 6: Commit**

```bash
git add src/OpenCode.Sdk tests
git commit -m "refactor(sdk): extract the family-neutral terminal socket core from PtySession"
```

---

### Task 4: `PersistentPtySession` — frames, options, decoder, policies, connect door

**Files:**
- Create under `src/OpenCode.Sdk/PersistentPtys/`: `PersistentPtySession.cs`,
  `PersistentPtyAttachment.cs`, `PersistentPtyReplayBounds.cs`, `PersistentPtyRole.cs`,
  `PersistentPtyConnectOptions.cs`, `PersistentPtyFrame.cs`, `PersistentPtyAttachedFrame.cs`,
  `PersistentPtyOutputFrame.cs`, `PersistentPtyReplayCompleteFrame.cs`, `PersistentPtyResizedFrame.cs`,
  `PersistentPtyExitedFrame.cs`, `PersistentPtyControllerChangedFrame.cs`,
  `PersistentPtyTitleChangedFrame.cs`, `PersistentPtyForegroundProcessChangedFrame.cs`,
  `PersistentPtyUnknownFrame.cs`
- Create under `src/OpenCode.Sdk/Internal/`: `PersistentPtyFrameDecoder.cs`,
  `PersistentPtyClosePolicy.cs`, `PersistentPtyUpgradeFailurePolicy.cs`,
  `PersistentPtyConnectUriBuilder.cs`, `PersistentPtyInputFrame.cs`
- Modify: `src/OpenCode.Sdk/PersistentPtys/PersistentPtyClient.cs` (+ `ConnectAsync`),
  `ReservedNamePolicy.SpineTypeNames` (+ the fifteen new public type names), PublicApi baseline
- Test: `tests/OpenCode.Sdk.Tests/PersistentPtys/PersistentPtySessionTests.cs`,
  `PersistentPtyConnectOptionsTests.cs`, `PersistentPtyConnectUriBuilderTests.cs`,
  `tests/OpenCode.Sdk.Tests/Support/PersistentPtyFrameData.cs`

**Interfaces:**
- Consumes: Task 3's core and seams; Task 2's generated `PersistentPtyInfo` and
  `OpenCodeJsonContext.Default.PersistentPtyInfo`.
- Produces (public):
  ```csharp
  public enum PersistentPtyRole { Controller, Observer }
  public sealed record PersistentPtyReplayBounds(long RequestedOffset, long AvailableOffset, long EndOffset, bool Truncated);
  public sealed record PersistentPtyAttachment
  {   public required string AttachmentId { get; init; }
      public required int InputProtocol { get; init; }
      public required PersistentPtyInfo Info { get; init; }
      public required PersistentPtyRole Role { get; init; }
      public required long Generation { get; init; }
      public required PersistentPtyReplayBounds Replay { get; init; } }
  public sealed record PersistentPtyConnectOptions
  {   public long? Cursor { get; init; }                 // 0..9007199254740991, else ArgumentOutOfRangeException
      public PersistentPtyRole Role { get; init; }       // default Controller
      public string? AttachmentId { get; init; }
      public bool Takeover { get; init; } }
  public abstract class PersistentPtyFrame { private protected PersistentPtyFrame() {} }
  public sealed class PersistentPtyAttachedFrame(PersistentPtyAttachment attachment) : PersistentPtyFrame { public PersistentPtyAttachment Attachment { get; } }
  public sealed class PersistentPtyOutputFrame(ReadOnlyMemory<byte> data) : PersistentPtyFrame { public ReadOnlyMemory<byte> Data { get; } }
  public sealed class PersistentPtyReplayCompleteFrame(long endOffset) : PersistentPtyFrame { public long EndOffset { get; } }
  public sealed class PersistentPtyResizedFrame(int cols, int rows, long generation, ReadOnlyMemory<byte> checkpoint) : PersistentPtyFrame { … }
  public sealed class PersistentPtyExitedFrame(int? exitCode, long finalOffset) : PersistentPtyFrame { … }
  public sealed class PersistentPtyControllerChangedFrame(string? attachmentId, long generation) : PersistentPtyFrame { … }
  public sealed class PersistentPtyTitleChangedFrame(string title) : PersistentPtyFrame { … }
  public sealed class PersistentPtyForegroundProcessChangedFrame(string? process) : PersistentPtyFrame { … }
  public sealed class PersistentPtyUnknownFrame(string type, JsonElement payload) : PersistentPtyFrame { … }
  public class PersistentPtySession : IAsyncDisposable
  {   public virtual PersistentPtyAttachment Attachment { get; }
      public virtual IAsyncEnumerable<PersistentPtyFrame> ReadAsync(CancellationToken cancellationToken = default);
      public virtual Task WriteAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken = default);
      public virtual Task ResizeAsync(int cols, int rows, CancellationToken cancellationToken = default);
      public virtual ValueTask DisposeAsync(); }
  // PersistentPtyClient.ConnectAsync(PersistentPtyConnectOptions? options = null, CancellationToken cancellationToken = default) : Task<PersistentPtySession>
  ```
  Frame constructors are public (the `PtyOutputFrame` precedent: a consumer substituting the
  session scripts the frames its override yields). Frame classes use primary constructors only if
  the analyzer wall accepts them in this repository; otherwise explicit constructors with guards.
- Produces (internal): `PersistentPtySession.AttachAsync(IPtyWebSocket socket, string ptyId, CancellationToken)`;
  `PersistentPtyInputFrame.Encode(byte type, long cols, long rows, ReadOnlySpan<byte> data) : byte[]`;
  `PersistentPtyConnectUriBuilder.Build(ConnectionSnapshot, string ptyId, PersistentPtyConnectOptions?) : Uri`.

- [ ] **Step 1: Wire literals for the tests**

`tests/OpenCode.Sdk.Tests/Support/PersistentPtyFrameData.cs`:

```csharp
using System.Buffers.Binary;
using System.Text;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>
/// The persistent-PTY wire literals the session tests run on: the JSON control frames exactly as
/// the handler emits them and the framed-input layout the server parses. Knowledge source:
/// upstream-observed at 106629aa (handler:138-182, 212-219).
/// </summary>
internal static class PersistentPtyFrameData
{
    public const string InfoJson =
        "{\"id\":\"pty_persistent_7\",\"title\":\"sdk terminal\",\"command\":\"/bin/bash\",\"args\":[\"-l\"],\"cwd\":\"/\",\"status\":\"running\",\"pid\":5150,\"sessionID\":\"ses_1\",\"foregroundProcess\":null,\"size\":{\"cols\":80,\"rows\":24},\"output\":{\"head\":0,\"tail\":42}}";

    public const string AttachedJson =
        "{\"type\":\"attached\",\"attachmentID\":\"att_1\",\"inputProtocol\":1,\"info\":" + InfoJson +
        ",\"role\":\"controller\",\"generation\":3,\"replay\":{\"requestedOffset\":0,\"availableOffset\":0,\"endOffset\":42,\"truncated\":false}}";

    public const string AttachedObserverJson =
        "{\"type\":\"attached\",\"attachmentID\":\"att_2\",\"inputProtocol\":1,\"info\":" + InfoJson +
        ",\"role\":\"observer\",\"generation\":3,\"replay\":{\"requestedOffset\":10,\"availableOffset\":20,\"endOffset\":42,\"truncated\":true}}";

    public const string AttachedRawProtocolJson =
        "{\"type\":\"attached\",\"attachmentID\":\"att_3\",\"inputProtocol\":0,\"info\":" + InfoJson +
        ",\"role\":\"controller\",\"generation\":3,\"replay\":{\"requestedOffset\":0,\"availableOffset\":0,\"endOffset\":0,\"truncated\":false}}";

    public const string ReplayCompleteJson = "{\"type\":\"replay_complete\",\"endOffset\":42}";

    /// <summary>The checkpoint is base64 of ESC c (0x1B 0x63).</summary>
    public const string ResizedJson = "{\"type\":\"resized\",\"cols\":120,\"rows\":40,\"generation\":4,\"checkpoint\":\"G2M=\"}";

    public const string ExitedJson = "{\"type\":\"exited\",\"exitCode\":0,\"finalOffset\":99}";

    public const string ExitedWithoutCodeJson = "{\"type\":\"exited\",\"finalOffset\":99}";

    public const string ControllerChangedJson = "{\"type\":\"controller_changed\",\"attachmentID\":\"att_9\",\"generation\":5}";

    public const string TitleChangedJson = "{\"type\":\"title_changed\",\"title\":\"vim\"}";

    public const string ForegroundProcessChangedJson = "{\"type\":\"foreground_process_changed\",\"process\":null}";

    public const string UnknownTypeJson = "{\"type\":\"scrollback_trimmed\",\"bytes\":1024}";

    public const string TypelessJson = "{\"cols\":1}";

    public const string TruncatedJson = "{\"type\":\"resized\",";

    public const string TerminalUnavailableReason = "terminal unavailable";

    public static byte[] Output(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>Builds the framed input the server parses: [type u8][cols u16 BE][rows u16 BE][data].</summary>
    public static byte[] Framed(byte type, int cols, int rows, byte[] data)
    {
        var frame = new byte[5 + data.Length];
        frame[0] = type;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(1, 2), checked((ushort)cols));
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(3, 2), checked((ushort)rows));
        data.CopyTo(frame, 5);
        return frame;
    }
}
```

- [ ] **Step 2: Session and options tests (all red)**

`PersistentPtySessionTests.cs` — the complete list; four shown in full:

```csharp
using System.Net.WebSockets;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

public sealed class PersistentPtySessionTests
{
    private const WebSocketCloseStatus TerminalUnavailable = (WebSocketCloseStatus)4404;

    [Test]
    public async Task AttachAsync_Should_Consume_The_Attached_Frame_And_Expose_The_Attachment()
    {
        var socket = new ScriptedPtyWebSocket()
            .Text(PersistentPtyFrameData.AttachedJson)
            .Closing(WebSocketCloseStatus.NormalClosure);

        await using var session = await PersistentPtySession.AttachAsync(socket, "pty_persistent_7", CancellationToken.None);

        await Assert.That(session.Attachment.AttachmentId).IsEqualTo("att_1");
        await Assert.That(session.Attachment.Role).IsEqualTo(PersistentPtyRole.Controller);
        await Assert.That(session.Attachment.Info.Id).IsEqualTo("pty_persistent_7");
        await Assert.That(session.Attachment.Replay.EndOffset).IsEqualTo(42);
        await Assert.That(await ReadAllAsync(session)).IsEmpty();
    }

    [Test]
    public async Task AttachAsync_Should_Refuse_A_Terminal_Unavailable_Close_Before_Attached()
    {
        var socket = new ScriptedPtyWebSocket().Closing(TerminalUnavailable, PersistentPtyFrameData.TerminalUnavailableReason);

        var failure = await Assert
            .That(async () => _ = await PersistentPtySession.AttachAsync(socket, "pty_persistent_7", CancellationToken.None))
            .Throws<OpenCodeTransportException>();

        await Assert.That(failure!.Message).Contains("4404");
        await Assert.That(failure.Message).Contains("daemon");
    }

    [Test]
    public async Task ReadAsync_Should_Yield_Output_As_Bytes_And_The_Replay_Bracket_In_Order()
    {
        var socket = new ScriptedPtyWebSocket()
            .Text(PersistentPtyFrameData.AttachedJson)
            .Binary(PersistentPtyFrameData.Output("$ "))
            .Text(PersistentPtyFrameData.ReplayCompleteJson)
            .Binary(PersistentPtyFrameData.Output("hello\n"))
            .Closing(WebSocketCloseStatus.NormalClosure);
        await using var session = await PersistentPtySession.AttachAsync(socket, "pty_persistent_7", CancellationToken.None);

        var frames = await ReadAllAsync(session);

        await Assert.That(frames.Count).IsEqualTo(3);
        await Assert.That(((PersistentPtyOutputFrame)frames[0]).Data.ToArray()).IsEquivalentTo(PersistentPtyFrameData.Output("$ "));
        await Assert.That(((PersistentPtyReplayCompleteFrame)frames[1]).EndOffset).IsEqualTo(42);
        await Assert.That(((PersistentPtyOutputFrame)frames[2]).Data.ToArray()).IsEquivalentTo(PersistentPtyFrameData.Output("hello\n"));
    }

    [Test]
    public async Task WriteAsync_Should_Send_A_Framed_Binary_Input_Carrying_The_Attached_Viewport()
    {
        var socket = new ScriptedPtyWebSocket().Text(PersistentPtyFrameData.AttachedJson);
        await using var session = await PersistentPtySession.AttachAsync(socket, "pty_persistent_7", CancellationToken.None);

        await session.WriteAsync(PersistentPtyFrameData.Output("ls\n"));

        await Assert.That(socket.SentMessageTypes.Single()).IsEqualTo(WebSocketMessageType.Binary);
        await Assert.That(socket.SentMessages.Single())
            .IsEquivalentTo(PersistentPtyFrameData.Framed(1, 80, 24, PersistentPtyFrameData.Output("ls\n")));
    }

    private static async Task<List<PersistentPtyFrame>> ReadAllAsync(PersistentPtySession session)
    {
        var frames = new List<PersistentPtyFrame>();
        await foreach (var frame in session.ReadAsync())
        {
            frames.Add(frame);
        }

        return frames;
    }
}
```

The rest, one test each:

| Test | Script | Assertion |
|---|---|---|
| `AttachAsync_Should_Refuse_A_Raw_Input_Protocol` | `AttachedRawProtocolJson` | throws; message contains `input protocol` and `1` |
| `AttachAsync_Should_Refuse_A_First_Frame_That_Is_Not_Attached` | `ReplayCompleteJson` first | throws; message names `attached` |
| `AttachAsync_Should_Refuse_A_Normal_Close_Before_Attached` | `Closing(1000)` only | throws; message names `attached` |
| `AttachAsync_Should_Expose_A_Truncated_Observer_Replay` | `AttachedObserverJson` | `Role == Observer`, `Replay.Truncated`, `Replay.RequestedOffset == 10` |
| `ReadAsync_Should_Decode_Each_Control_Frame_Kind` (`[MethodDataSource]` over resized/exited/exited-without-code/controller_changed/title_changed/foreground_process_changed) | attached + the frame + 1000 | the typed frame with its members (`Resized.Checkpoint.ToArray() == [0x1B,0x63]`, `Exited.ExitCode == 0` / `null`, `ControllerChanged.AttachmentId == "att_9"`, `TitleChanged.Title == "vim"`, `ForegroundProcessChanged.Process == null`) |
| `ReadAsync_Should_Yield_An_Unknown_Control_Type_As_An_Unknown_Frame` | `UnknownTypeJson` | `Unknown.Type == "scrollback_trimmed"`, `Payload.GetProperty("bytes").GetInt32() == 1024` |
| `ReadAsync_Should_Refuse_A_Control_Frame_Without_A_Type` | `TypelessJson` | throws; message contains `type` |
| `ReadAsync_Should_Refuse_Truncated_Control_Json` | `TruncatedJson` | throws `OpenCodeTransportException` |
| `ReadAsync_Should_Assemble_A_Fragmented_Output_Message_Once` | `BinaryFragments(Output("frag-mented"), 5)` | single output frame, bytes equal |
| `ReadAsync_Should_Assemble_A_Fragmented_Control_Frame_Once` | `TextFragments` of `ResizedJson` split | single resized frame |
| `ReadAsync_Should_Refuse_A_Terminal_Unavailable_Close_Mid_Stream` | attached, output, `Closing(4404)` | throws after yielding the output |
| `ReadAsync_Should_Refuse_An_Abnormal_Close` | attached, `Closing(1011)` | throws; message contains `1011` |
| `ReadAsync_Should_Track_The_Viewport_From_A_Resized_Frame` | attached, `ResizedJson`, then write | the sent frame carries 120×40 |
| `ResizeAsync_Should_Send_A_Control_Frame_And_Track_The_Viewport` | attached; `ResizeAsync(100, 30)` then `WriteAsync` | first send `Framed(0,100,30,[])`, second `Framed(1,100,30,data)` |
| `ResizeAsync_Should_Refuse_A_Zero_Or_Oversized_Dimension` | — | `ArgumentOutOfRangeException` for 0 and 65536 |
| `WriteAsync_Should_Throw_After_Dispose` | attached | `ObjectDisposedException` |
| `ReadAsync_Should_Refuse_A_Second_Concurrent_Enumeration` | attached + `Parking()` | `InvalidOperationException` |

`PersistentPtyConnectOptionsTests`: `Cursor = -1` and `Cursor = 9_007_199_254_740_992` throw
`ArgumentOutOfRangeException`; `Cursor = 0` and `null` are accepted; defaults are
`Role == Controller`, `Takeover == false`, `AttachmentId == null`.

`PersistentPtyConnectUriBuilderTests` (over a `ConnectionSnapshot("http://localhost:4096", null, null)`):
defaults → `ws://localhost:4096/api/experimental/persistent-pty/pty_persistent_7/connect?input_protocol=1`;
all options set (`Cursor = 42`, `Observer`, `AttachmentId = "att_1"`, `Takeover = true`) →
`…/connect?cursor=42&role=observer&attachment_id=att_1&takeover=true&input_protocol=1`; `https` →
`wss`; a dot-segment id refuses.

Run: `dotnet build tests/OpenCode.Sdk.Tests` — Expected: FAIL (types missing).

- [ ] **Step 3: Public types**

Frames, attachment, bounds, role, options as in the Interfaces block, each in its own file with
XML docs stating what the wire carries (bytes on output, the checkpoint as terminal-escape bytes,
the unknown carrier's tolerance). `PersistentPtyConnectOptions.Cursor`'s init guard mirrors
`PtyConnectOptions` with `MinimumCursor = 0` and the message
`"The persistent PTY cursor must be null to replay from the oldest retained byte, or between 0 and 9007199254740991."`

- [ ] **Step 4: Internal pieces**

`PersistentPtyInputFrame`:

```csharp
using System.Buffers.Binary;

namespace OpenCode.Sdk.Internal;

/// <summary>
/// Encodes the framed input protocol (input_protocol=1): [type u8][cols u16 BE][rows u16 BE][data].
/// Knowledge source: upstream-observed — the server ignores frames shorter than five bytes and
/// frames whose cols or rows are zero, so both are refused here rather than sent to be dropped.
/// </summary>
internal static class PersistentPtyInputFrame
{
    public const byte ControlType = 0;

    public const byte InputType = 1;

    private const int HeaderLength = 5;

    public static byte[] Encode(byte type, long cols, long rows, ReadOnlySpan<byte> data)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cols, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cols, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rows, ushort.MaxValue);

        var frame = new byte[HeaderLength + data.Length];
        frame[0] = type;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(1, 2), (ushort)cols);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(3, 2), (ushort)rows);
        data.CopyTo(frame.AsSpan(HeaderLength));
        return frame;
    }
}
```

`PersistentPtyClosePolicy` (singleton, `ITerminalClosePolicy`): `NormalClosure` → null; 4404 →
`"The opencode server closed the persistent PTY WebSocket with status 4404{reason}; the terminal does not exist or the opencode-pty daemon is unavailable."`;
otherwise `"The opencode persistent PTY WebSocket closed abnormally with status {code}{reason}."`
(reuse the `FormatReason` shape from `PtyCloseFailurePolicy`).

`PersistentPtyUpgradeFailurePolicy` (singleton, `ITerminalUpgradeFailurePolicy`): `null` status →
"failed before the connection was established"; `400` → `"…answered the persistent PTY '{ptyId}' WebSocket upgrade with HTTP 400; the connect query was rejected (the cursor must be a safe integer at or above zero)."`;
`401 or 403` → credential/origin refused; anything else → the generic "instead of completing the
protocol upgrade" wording. There is deliberately no 404 arm.

`PersistentPtyConnectUriBuilder.Build`: path prefix `/api/experimental/persistent-pty/`, suffix
`/connect`; query through `QueryStringBuilder`: `AddText("cursor", options?.Cursor?.ToString(CultureInfo.InvariantCulture))`,
`AddText("role", options?.Role is PersistentPtyRole.Observer ? "observer" : null)`,
`AddText("attachment_id", options?.AttachmentId)`, `AddText("takeover", options?.Takeover is true ? "true" : null)`,
`AddText("input_protocol", "1")`; scheme swap copied from `PtyConnectUriBuilder` (extract the
`ToWebSocketScheme` helper into a shared `WebSocketSchemePolicy` if the duplicate offends the
reviewer; two six-line copies are acceptable to land, one is better).

`PersistentPtyFrameDecoder` (singleton, `ITerminalFrameDecoder<PersistentPtyFrame>`):

```csharp
using System.Net.WebSockets;
using System.Text.Json;
using OpenCode.Sdk.Internal.Serialization;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk.Internal;

/// <summary>
/// Reads one assembled persistent-PTY message. Knowledge source: upstream-observed — binary
/// frames are raw terminal bytes; text frames are JSON objects whose <c>type</c> names one of the
/// seven control kinds. An unrecognized type is carried, not refused (the socket is declared
/// experimental); a body that is not a JSON object with a string <c>type</c> is a protocol failure.
/// </summary>
internal sealed class PersistentPtyFrameDecoder : ITerminalFrameDecoder<PersistentPtyFrame>
{
    private const string ControlFrameFailure =
        "The opencode server sent a persistent PTY control frame whose body is not a JSON object carrying a string 'type'.";

    public static PersistentPtyFrameDecoder Instance { get; } = new();

    private PersistentPtyFrameDecoder()
    {
    }

    public PersistentPtyFrame Decode(WebSocketMessageType messageType, byte[] payload, int count)
    {
        if (messageType is WebSocketMessageType.Binary)
        {
            // The receive buffer is reused by the next message; the frame owns a copy.
            return new PersistentPtyOutputFrame(new ReadOnlyMemory<byte>(payload.AsSpan(0, count).ToArray()));
        }

        try
        {
            using var document = JsonDocument.Parse(new ReadOnlyMemory<byte>(payload, 0, count));
            var root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object
                || !root.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind is not JsonValueKind.String)
            {
                throw new OpenCodeTransportException(ControlFrameFailure);
            }

            return typeElement.GetString() switch
            {
                "attached" => new PersistentPtyAttachedFrame(ReadAttachment(root)),
                "replay_complete" => new PersistentPtyReplayCompleteFrame(root.GetProperty("endOffset").GetInt64()),
                "resized" => new PersistentPtyResizedFrame(
                    root.GetProperty("cols").GetInt32(),
                    root.GetProperty("rows").GetInt32(),
                    root.GetProperty("generation").GetInt64(),
                    root.GetProperty("checkpoint").GetBytesFromBase64()),
                "exited" => new PersistentPtyExitedFrame(
                    root.TryGetProperty("exitCode", out var exitCode) && exitCode.ValueKind is JsonValueKind.Number ? exitCode.GetInt32() : null,
                    root.GetProperty("finalOffset").GetInt64()),
                "controller_changed" => new PersistentPtyControllerChangedFrame(
                    root.TryGetProperty("attachmentID", out var attachment) && attachment.ValueKind is JsonValueKind.String ? attachment.GetString() : null,
                    root.GetProperty("generation").GetInt64()),
                "title_changed" => new PersistentPtyTitleChangedFrame(root.GetProperty("title").GetString()!),
                "foreground_process_changed" => new PersistentPtyForegroundProcessChangedFrame(
                    root.GetProperty("process").ValueKind is JsonValueKind.String ? root.GetProperty("process").GetString() : null),
                var other => new PersistentPtyUnknownFrame(other!, root.Clone()),
            };
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new OpenCodeTransportException(ControlFrameFailure, exception);
        }
    }

    private static PersistentPtyAttachment ReadAttachment(JsonElement root) =>
        new()
        {
            AttachmentId = root.GetProperty("attachmentID").GetString()!,
            InputProtocol = root.GetProperty("inputProtocol").GetInt32(),
            Info = root.GetProperty("info").Deserialize(OpenCodeJsonContext.Default.PersistentPtyInfo)
                   ?? throw new OpenCodeTransportException(ControlFrameFailure),
            Role = string.Equals(root.GetProperty("role").GetString(), "observer", StringComparison.Ordinal)
                ? PersistentPtyRole.Observer
                : PersistentPtyRole.Controller,
            Generation = root.GetProperty("generation").GetInt64(),
            Replay = ReadReplay(root.GetProperty("replay")),
        };

    private static PersistentPtyReplayBounds ReadReplay(JsonElement replay) =>
        new(
            replay.GetProperty("requestedOffset").GetInt64(),
            replay.GetProperty("availableOffset").GetInt64(),
            replay.GetProperty("endOffset").GetInt64(),
            replay.GetProperty("truncated").GetBoolean());
}
```

(`JsonElement.Deserialize(JsonTypeInfo<T>)` is the source-generated, AOT-safe door; if the
generated context exposes the info under a different accessor name, use that one.)

- [ ] **Step 5: The session and the connect door**

`PersistentPtySession.cs`:

```csharp
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using OpenCode.Sdk.Internal;

namespace OpenCode.Sdk;

/// <summary>
/// A live persistent-terminal connection. Opened only after the server's <c>attached</c> frame,
/// so <see cref="Attachment"/> is always known: read the frames the server sends, write framed
/// input carrying the current viewport, resize, and dispose to close. The session owns its
/// socket. Output rides as bytes; a caller feeding an emulator writes them as they are.
/// </summary>
public class PersistentPtySession : IAsyncDisposable
{
    private const int InputProtocolVersion = 1;

    private readonly PersistentPtyAttachment? _attachment;
    private readonly TerminalSocketCore<PersistentPtyFrame>? _core;
    private long _cols;
    private long _rows;

    internal PersistentPtySession(TerminalSocketCore<PersistentPtyFrame> core, PersistentPtyAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(attachment);

        _core = core;
        _attachment = attachment;
        _cols = attachment.Info.Size.Cols;
        _rows = attachment.Info.Size.Rows;
    }

    /// <summary>
    /// Initializes a mocking instance; members invoked without an override throw an instructive failure.
    /// </summary>
    protected PersistentPtySession()
    {
    }

    /// <summary>Gets what the server granted at attach time: identity, role, generation, terminal info, and replay bounds.</summary>
    public virtual PersistentPtyAttachment Attachment => _attachment ?? throw MockSeam.CreateError("PersistentPtySession", "Attachment");

    /// <summary>
    /// Reads the frames the server sends until it closes normally. The replay, when any, arrives as
    /// one output frame bracketed by <see cref="PersistentPtyReplayCompleteFrame"/>; a resize the
    /// server reports updates the viewport later writes carry. One active enumeration per session.
    /// </summary>
    public virtual IAsyncEnumerable<PersistentPtyFrame> ReadAsync(CancellationToken cancellationToken = default) =>
        ReadCoreAsync(Core, cancellationToken);

    /// <summary>
    /// Writes terminal input as one framed binary message carrying the current viewport. The
    /// bytes are sent exactly as given; a shell's Enter is whatever the terminal expects.
    /// </summary>
    public virtual Task WriteAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken = default) =>
        SendFrameAsync(PersistentPtyInputFrame.InputType, input, cancellationToken);

    /// <summary>Resizes the terminal through a control frame and records the viewport for later writes.</summary>
    public virtual Task ResizeAsync(int cols, int rows, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cols, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cols, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rows, ushort.MaxValue);

        Volatile.Write(ref _cols, cols);
        Volatile.Write(ref _rows, rows);
        return SendFrameAsync(PersistentPtyInputFrame.ControlType, ReadOnlyMemory<byte>.Empty, cancellationToken);
    }

    /// <summary>Closes the connection; idempotent, bounded, and a pending read ends normally.</summary>
    public virtual async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (_core is not null)
        {
            await _core.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Attaches over an upgraded socket: reads the first frame, which the server sends before any
    /// replay, and refuses anything but an <c>attached</c> frame negotiating input protocol 1.
    /// A terminal that does not exist or a daemon that is not running closes 4404 here rather
    /// than on the first read.
    /// </summary>
    internal static async Task<PersistentPtySession> AttachAsync(IPtyWebSocket socket, string ptyId, CancellationToken cancellationToken)
    {
        var core = new TerminalSocketCore<PersistentPtyFrame>(
            socket, PersistentPtyFrameDecoder.Instance, PersistentPtyClosePolicy.Instance, typeof(PersistentPtySession));
        PersistentPtyFrame? first = null;
        await foreach (var frame in core.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            first = frame;
            break;
        }

        if (first is not PersistentPtyAttachedFrame attached)
        {
            throw new OpenCodeTransportException(first is null
                ? $"The opencode server closed the persistent PTY '{ptyId}' WebSocket before sending the 'attached' frame."
                : $"The opencode server sent a '{first.GetType().Name}' before the persistent PTY '{ptyId}' 'attached' frame.");
        }

        if (attached.Attachment.InputProtocol is not InputProtocolVersion)
        {
            throw new OpenCodeTransportException(
                $"The opencode server negotiated persistent PTY input protocol {attached.Attachment.InputProtocol} for '{ptyId}'; this SDK speaks protocol 1 (framed input), so the server is out of date.");
        }

        return new PersistentPtySession(core, attached.Attachment);
    }

    private TerminalSocketCore<PersistentPtyFrame> Core => _core ?? throw MockSeam.CreateError("PersistentPtySession", "WebSocket");

    private async IAsyncEnumerable<PersistentPtyFrame> ReadCoreAsync(
        TerminalSocketCore<PersistentPtyFrame> core,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var frame in core.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (frame is PersistentPtyResizedFrame resized)
            {
                Volatile.Write(ref _cols, resized.Cols);
                Volatile.Write(ref _rows, resized.Rows);
            }

            yield return frame;
        }
    }

    private Task SendFrameAsync(byte type, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var core = Core;
        var frame = PersistentPtyInputFrame.Encode(type, Volatile.Read(ref _cols), Volatile.Read(ref _rows), data.Span);
        return core.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary, cancellationToken);
    }
}
```

`PersistentPtyClient.ConnectAsync`:

```csharp
    /// <summary>
    /// Opens the terminal's live WebSocket session and returns once the server has attached this
    /// connection. The upgrade is the SDK's transport divergence (own socket; the Basic credential
    /// on the upgrade request; no ticket minted for itself). Unlike the normal PTY family, the
    /// server does not check the terminal's existence before upgrading: a missing terminal or an
    /// absent daemon closes 4404 right after, which this door surfaces here rather than on the
    /// first read.
    /// </summary>
    public virtual async Task<PersistentPtySession> ConnectAsync(PersistentPtyConnectOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var connection = Connection;
        var ptyId = PtyId;
        var address = PersistentPtyConnectUriBuilder.Build(connection, ptyId, options);
        ClientPtyWebSocket socket;
        try
        {
            socket = new ClientPtyWebSocket(connection.Authorization);
        }
        catch (PlatformNotSupportedException exception)
        {
            throw new OpenCodeTransportException(
                $"The opencode persistent PTY '{ptyId}' WebSocket could not be constructed on this platform.", exception);
        }

        try
        {
            await socket.ConnectAsync(address, ptyId, PersistentPtyUpgradeFailurePolicy.Instance, cancellationToken).ConfigureAwait(false);
            return await PersistentPtySession.AttachAsync(socket, ptyId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
```

- [ ] **Step 6: Reserved names, PublicApi, gate**

Add the fifteen new public type names to `ReservedNamePolicy.SpineTypeNames`
(`ReservedNamePolicyTests.SpineTypeNames_Should_Mirror…` is the referee). Run the full gate; accept
the additive PublicApi baseline as in Task 2 Step 9.

- [ ] **Step 7: Commit**

```bash
git add src/OpenCode.Sdk tools/OpenCode.Sdk.Tools/Generator/Binding/ReservedNamePolicy.cs tests
git commit -m "feat(sdk): add the persistent PTY WebSocket session and its connect door"
```

---

### Task 5: `persistentPty.connect` becomes transport-owned

**Files:**
- Modify: `tools/curation.json` (`transportOwned` row for `v2.persistentPty.connect`)
- Regenerate: `src/OpenCode.Sdk/.generation-incomplete` (`122 / 12 / 2`)
- Test: `tests/OpenCode.Sdk.Tools.Tests/Generator/Ingestion/PinnedSpecSmokeTests.cs` (or the
  nearest pinned-spec test) — assert both WebSocket operations are transport-owned in the plan

- [ ] **Step 1: Obtain the fingerprint the validator computes**

Add the row with a deliberately wrong hash (64 zeros) and run `generate`; the refusal names the
computed `subtreeSha256`. Replace the placeholder with the computed value:

```json
    {
      "operationId": "v2.persistentPty.connect",
      "subtreeSha256": "<the computed value>",
      "reason": "The persistent PTY WebSocket session door is hand-written over its own URL/query construction (ADR-0021 pattern, Task 4); the operation is never selected into the generation profile, so this fingerprint is the only generation-time check that a spec refresh reshaping method, path, the six connect queries, the x-websocket marker, or declared responses fails loudly instead of drifting silently under the hand-written door."
    }
```

- [ ] **Step 2: Regenerate, verify the marker, test, gate, commit**

`generate` → marker `Selected operations: 122`, `Pending operations: 12`,
`Transport-owned operations: 2` with `- v2.persistentPty.connect [fingerprint-pinned]` under
`Transport-owned:` and no `persistentPty.connect` line under `Pending:`. Add a pinned-spec test
asserting `plan.TransportOwnedOperationIds` equals `["v2.persistentPty.connect", "v2.pty.connect"]`.

```bash
git add tools/curation.json src/OpenCode.Sdk/.generation-incomplete tests/OpenCode.Sdk.Tools.Tests
git commit -m "feat(tools): fingerprint-pin the persistent PTY connect operation as transport-owned"
```

---

### Task 6: Exact-pin fixture explicit-endpoint mode, daemon gate, WSL2 recipe

**Files:**
- Modify: `tests/Shared/PinnedOpenCodeServerFixture.cs` (external-endpoint mode)
- Create: `tests/Shared/ExternalServerEndpoint.cs` (the env pair, parsed once),
  `tests/Shared/PersistentPtyDaemonGate.cs`
- Test: `tests/OpenCode.Sdk.Tests/PinnedServerFixtureTests.cs` (+ external-mode test over `LoopbackHttpServer`)
- Docs: the place `OPENCODE_SDK_TESTS_KEEP_LOGS` is documented (grep the repository; add the two
  new variables beside it) and `tests/OpenCode.Sdk.Sandbox/README.md` (the WSL2 recipe)

**Interfaces:**
- Produces:
  ```csharp
  internal sealed record ExternalServerEndpoint(Uri Endpoint, string Password)
  { public static ExternalServerEndpoint? FromEnvironment(); }   // OPENCODE_SDK_TESTS_ENDPOINT + OPENCODE_SDK_TESTS_PASSWORD; both or neither, else InvalidOperationException
  public static class PersistentPtyDaemonGate
  { public static bool DaemonExpected { get; } }   // false on Windows (no @opencode-ai/pty win32 package at 106629aa), true elsewhere; overridable by OPENCODE_SDK_TESTS_PTY_DAEMON=0|1 for a WSL2 endpoint run
  ```
- Fixture behavior in external mode: `InitializeAsync` spawns nothing, probes `GET /api/health`
  through a throw-away `OpenCodeClient` (fail-fast with the endpoint in the message on any
  failure), prints the reported `version` and the pinned submodule commit so the operator can see
  the pair; `Endpoint`/`CreateClient` use the external pair; `DisposeAsync` releases nothing it
  did not start.

- [ ] **Step 1: Tests first** — `PinnedServerFixtureTests`:
  `Fixture_Should_Attach_To_An_External_Endpoint_When_The_Environment_Names_One` (a
  `LoopbackHttpServer` answering `WireBodyData.HealthOk`; construct the fixture through a new
  internal constructor taking `ExternalServerEndpoint`; assert `Endpoint` equals the loopback and
  `CreateClient()` targets it) and
  `Fixture_Should_Refuse_An_External_Endpoint_That_Does_Not_Answer_Health` (loopback answering
  500; `InitializeAsync` throws naming the endpoint). `ExternalServerEndpointTests`: both variables
  → record; one of the two → `InvalidOperationException`; neither → null.
  `PersistentPtyDaemonGateTests`: the override variable wins over the platform default.
- [ ] **Step 2: Implement** (fixture branches on `ExternalServerEndpoint.FromEnvironment()` before
  resolving the pinned command; the daemon gate reads the platform through
  `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` so net472 compiles).
- [ ] **Step 3: Docs** — beside `OPENCODE_SDK_TESTS_KEEP_LOGS`: the three variables, one line each.
  Sandbox README, new section "Live legs against a WSL2 server (Windows workstations)":

  ```text
  1. In WSL2, at the same checkout (/mnt/<drive>/…/external/opencode):
       bun install --frozen-lockfile --ignore-scripts        # places the linux opencode-pty package
       OPENCODE_PASSWORD=<pw> bun packages/cli/src/index.ts serve --port 4097
  2. On Windows:
       $env:OPENCODE_SDK_TESTS_ENDPOINT = "http://localhost:4097"
       $env:OPENCODE_SDK_TESTS_PASSWORD = "<pw>"
       $env:OPENCODE_SDK_TESTS_PTY_DAEMON = "1"
       dotnet test --configuration Release
  The exact-pin discipline is the operator's: the WSL2 server must be built from the same
  submodule commit; the fixture prints both so a mismatch is visible, it cannot verify a source
  run's version.
  ```
- [ ] **Step 4: Gate, commit** — `test(shared): add the exact-pin fixture's external-endpoint mode and the daemon gate`.

---

### Task 7: Live proof — `PersistentPtyLiveTests`, the sandbox leg, the hosted run

**Files:**
- Test: `tests/OpenCode.Sdk.Tests/PersistentPtys/PersistentPtyLiveTests.cs`
- Create: `tests/OpenCode.Sdk.Sandbox/PersistentPtyWalkthrough.cs`; modify `SandboxRunner.cs` (register the leg after `PtySessionWalkthrough`)

**Interfaces:** consumes Tasks 2, 4, 6.

- [ ] **Step 1: The live test** (one class, `[ClassDataSource<PinnedOpenCodeServerFixture>(Shared = SharedType.PerTestSession)]`,
  `[NotInParallel("pinned-opencode-server")]`, `[Timeout(120_000)]`):

```csharp
    [Test]
    public async Task Terminal_Lifecycle_Should_Round_Trip_Or_Answer_The_Daemon_Absent_Arm(CancellationToken cancellationToken)
    {
        using var client = server.CreateClient();
        var session = await client.Sessions.CreateSessionAsync(new SessionCreateRequest { Title = "persistent pty live" }, cancellationToken: cancellationToken);
        var created = await client.PersistentPtys.CreatePersistentPtyAsync(
            session.Session.Id, CreateRequest(), OpenCodeRequestOptions.NoThrow, cancellationToken);

        if (!PersistentPtyDaemonGate.DaemonExpected)
        {
            await AssertDaemonAbsentArmsAsync(client, session.Session.Id, created, cancellationToken);
            return;
        }

        await AssertRoundTripAsync(client, session.Session.Id, created, cancellationToken);
    }
```

  `AssertDaemonAbsentArmsAsync`: `created.Status == 503`, `created.Error is ServiceUnavailableError { Service: "opencode-pty" }`;
  `ListPersistentPtysAsync` → empty; `ReadAsync` → `Read` null; `HandoffAsync` → `Handoff` null;
  `ShutdownAsync` → 204. Every assertion is a real server answer on the platform without a daemon.

  `AssertRoundTripAsync`: `created.Status == 200`, `Status == Running`; list contains the id;
  `GetPersistentPtyClient(id).GetSnapshotAsync()` → `Snapshot.Info.Id == id`; `ConnectAsync()` →
  `Attachment.Role == Controller`, `InputProtocol == 1`; `WriteAsync("echo sdk-live\n")` (LF is
  Enter for a Unix line discipline; the normal family's CR finding was PSReadLine on Windows);
  read frames until the concatenated output contains `sdk-live` (bounded by the token); `ResizeAsync(100, 30)`
  → a `PersistentPtyResizedFrame` with 100×30 arrives; `ReadAsync(sessionId)` over HTTP → `Read.Screen.Text`
  contains `sdk-live`; dispose the session; `RemovePersistentPtyAsync()` → 204; list no longer contains it.

- [ ] **Step 2: The sandbox leg** — mirrors the round trip with printed evidence lines
  (`ppty-create`, `ppty-attach`, `ppty-echo`, `ppty-resize`, `ppty-read`, `ppty-remove`), and on a
  platform without the daemon prints the 503 arm and the daemon-absent answers instead. Register
  after `PtySessionWalkthrough` in `SandboxRunner`.
- [ ] **Step 3: Local gate** — on this Windows workstation the live test takes the 503 branch; run it
  once more through the WSL2 recipe (Task 6) and paste both outputs into the task report.
- [ ] **Step 4: Commit, then ask the maintainer to push** — the three-OS run is the proof that the
  Linux and macOS legs start the daemon from `bun install`'s output (named risk: first hosted run).
  Report the matrix result, including the ubuntu/macos job logs' `ppty-*`/test lines, before
  claiming the task complete. Commit: `test(sdk): prove the persistent PTY family live and its daemon-absent arms`.

---

### Task 8: Canon, research log, roadmap, handoff

**Files:** `docs/adr/0021-normal-pty-public-surface-hand-written.md`,
`docs/architecture/client-runtime.md`, `docs/architecture/protocol-and-generation.md`,
`CONTEXT.md`, `docs/engineering/testing-style.md`, `docs/research/00-research-log.md` (Q156),
`docs/ROADMAP.md`, `docs/agents/handover-prompts/HANDOFF-2026-08-29.md`.

- [ ] **Step 1: ADR-0021 scope paragraph** — append after the decision paragraph (Date stays; this
  is a scope revision the maintainer chose over a new record):

  > **Scope (revised 2026-08-29):** the persistent PTY family (`v2.persistentPty.*`) follows the
  > same ownership pattern for the same two reasons — its `connect` operation is a WebSocket
  > upgrade and its connect-token handshake requires the same `x-opencode-ticket` sentinel — while
  > its wire differs from the normal family's (binary output, JSON text control frames, framed
  > input, a cursor domain without a live-only mode, no pre-upgrade existence check). Both
  > families' public doors are hand-written over generated internal raw clients and share one
  > family-neutral socket core behind named decode, close, and upgrade seams.

- [ ] **Step 2: `client-runtime.md`** — new `### Persistent PTY family` after "PTY WebSocket
  session", stating (status quo, no history): ownership and placement (D1/D2), the public doors
  and the lifecycle doors (D3), `ConnectAsync` returning after `attached` with `Attachment`
  exposed (D5), bytes on output and checkpoint (D4), the frame hierarchy and the unknown carrier
  (D6), the framed input protocol and viewport tracking, the cursor rule (relay only; resume via
  `output.tail`), the close codes (1000; 4404 = not found **or** daemon unavailable), the
  pre-upgrade 400/401/403 arms and the absence of a 404 arm, the daemon facts a caller must know
  (platform packages darwin/linux only at the pin; `create` answers 503 elsewhere; the daemon is
  the server's child; `shutdown` ends every terminal), and the shared-core note pointing at the
  three seams. Update the "PTY family ownership" opening sentence from "the one family" to "one of
  the two families".
- [ ] **Step 3: `protocol-and-generation.md`** — "Generated model shape", new bullet:
  `A string declaring contentEncoding: base64 materializes as ReadOnlyMemory<byte> — a represented token conversion the serializer performs natively; any other content encoding fails closed (ADR-0014).`
- [ ] **Step 4: `CONTEXT.md`** — under "Upstream domain": **Persistent PTY** (a terminal owned by
  the `opencode-pty` daemon rather than the server process; survives server restarts through a
  handoff; keyed to a session; the source of `read`'s "current terminal"), **Attachment** (one
  live connection to a persistent PTY: identity, controller/observer role, generation, replay
  bounds), **Controller / Observer**, **Checkpoint** (the terminal-escape byte stream that repaints
  a screen state; carried base64 on the wire, bytes in the SDK), **Framed input** (the
  `input_protocol=1` layout). Under "This project's language": **Terminal socket core** (the
  family-neutral WebSocket lifecycle both sessions share).
- [ ] **Step 5: `testing-style.md` §6** — append to the `Skip` bullet:
  `A platform-gated live leg is not a skip: it asserts the arm the platform can reach (the daemon-absent 503 where the opencode-pty daemon cannot run, the full flow where it can), names the branch it took, and both branches assert.`
- [ ] **Step 6: Research log Q156** — "What did the persistent PTY family arc land, and what did the
  live proof show?" in the Q152/Q153 shape: method (plan, tasks, reviews), what landed, the
  protocol facts that became runtime behavior, the daemon/CI finding (first hosted run of the
  daemon start), the WSL2 recipe result, and the upstream-report candidates (the `read` `lines`
  range invisible in the document; 4404 overload; `PtyTicket.ConnectToken` without TTL).
- [ ] **Step 7: ROADMAP + handoff** — profile `122 / 12 / 2`; the persistentPty batch paragraph in
  the status section; "Coverage to full — the other twelve" list shrinks (the persistentPty rows
  leave; `vcs.base`, `config.get`, `fs.list` remain the curation-only trio); Known Gaps gains the
  two source-watch file sets (normal + persistent) as the Proposal 2 input. Handoff: rewrite the
  "Start here" and goals for the next session.
- [ ] **Step 8: Commit** — `docs: record the persistent PTY family in canon, research, and roadmap`.

---

## Self-review (run before handing the plan to executors)

1. **Spec coverage:** D1 → Task 2/8; D2 → Task 2; D3 → Task 2; D4 → Tasks 1, 2 (snapshot test), 4
   (output/checkpoint frames); D5 → Task 4 (`AttachAsync`, connect door, tests); D6 → Task 4
   (unknown carrier + tests); D7 → Tasks 6, 7; defaults → Task 4 options/URI tests; shared core →
   Task 3; transport-owned → Task 5; WSL2 → Task 6; canon → Task 8. No fact from the "Mechanism
   facts" section lacks a consumer.
2. **Placeholders:** the only "mirror the generated signature" instructions are for names the
   generator emits deterministically and the executor reads off the regenerated files in the same
   task; every test row names its scenario and assertion.
3. **Type consistency:** `ITerminalFrameDecoder<TFrame>.Decode(WebSocketMessageType, byte[], int)`
   is what both decoders implement and the core calls; `IPtyWebSocket.SendAsync(ArraySegment<byte>,
   WebSocketMessageType, CancellationToken)` is what both sessions call and both fakes implement;
   `PersistentPtySession.AttachAsync(IPtyWebSocket, string, CancellationToken)` is what the door
   and the tests call; `PersistentPtyInputFrame.Encode(byte, long, long, ReadOnlySpan<byte>)` is
   what `SendFrameAsync` calls and `PersistentPtyFrameData.Framed` mirrors for assertions.

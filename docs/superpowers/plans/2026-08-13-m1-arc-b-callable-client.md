# M1 Arc B Callable Client Execution Plan

Date: 2026-08-13

**Goal:** make `v2.health.get` and `v2.session.message` callable through their final
generated SDK surface, backed by one minimal hand-written HTTP behavior core.

**Architecture:** SpecIR plus curation binds into client, operation, envelope, route, and
error plans. Generic emitters render those plans without knowing operation IDs, wire group
names, or concrete client names. Generated methods delegate once into a hand-written
`Pipeline`; generated response adapters own operation-specific success and error mapping.

## Boundaries

- This is an opencode SDK generator, not a general OpenAPI generator. Arc B supports only
  selected opencode dialect shapes consumed by M1 and fails closed on any other selected shape.
- Public API policy is curation-driven. The `session` group row declares `Sessions`,
  `SessionClient`, and `sessionID` as its handle parameter. The Binder applies one general
  partial-application rule; no Binder or emitter branch names `session` or either selected
  operation. All other operations stay pending in M1.
- C# identifiers use ordinary PascalCase for acronym tokens regardless of length:
  `id` -> `Id`, `sessionID` -> `SessionId`, `callID` -> `CallId`, `URL` -> `Url`.
  `[JsonPropertyName]` preserves wire spelling; exceptional brand spelling remains curated.
- Optionality and nullability stay independent. An optional non-null collection may be absent
  and reads as empty; explicit JSON `null` is rejected. Its public property remains non-null.
- `net472` receives a conditional framework `<Reference Include="System.Net.Http" />`; no
  `System.Net.Http` NuGet package or second HTTP implementation is added.
- URI constructors own endpoint authority. BYO `HttpClient` requires `options.Endpoint`; the
  SDK neither reads nor mutates `HttpClient.BaseAddress`. Endpoints are absolute HTTP(S), carry
  no query/fragment, and retain a normalized path prefix.
- Every non-2xx remains an API response. Only status-map-allowed known tags become their concrete
  error type; an unknown tag, undeclared status, or known tag at the wrong status becomes
  `UnknownOpenCodeError` with its raw payload. Malformed error JSON retains raw body with a null
  typed error. Throwing calls carry it on `OpenCodeApiException`; `NoThrow` calls carry it on
  `OpenCodeResponse.RawBody`. Unexpected or malformed 2xx responses are protocol failures.
  `OperationCanceledException` is never remapped.
- Retry, telemetry, hooks, DI Extensions, launcher, SSE, raw `SendAsync`, bodies, query
  parameters, and operation breadth are out of scope.

## Task 0: Retarget The Spec Pin To The v2 Branch

**Areas:** `spec/`, `Generator/Ingestion/Walls`, `tools/curation.json`, regenerated models.

Executes the 2026-08-13 direction (ADR-0005 revised: v2 protocol surface only; platform
evidence: research doc 15).

- [x] Pin a `v2`-branch snapshot of `packages/protocol/openapi.json` as `spec/openapi.json`
  and rewrite `spec/SNAPSHOT.md` provenance (branch, commit, retrieval date, refresh steps).
- [x] Add one ingestion-wall admit rule: a single-element `allOf` wrapper carrying only
  validation keywords unwraps into its base schema; every other `allOf` stays refused.
  Re-census `prefixItems`/`patternProperties`/empty-schema sites against the new pin.
- [ ] Re-derive the selected wire contracts from the new pin: `v2.health.get` success is the
  `ServiceHealth` object (`{healthy, version, pid}` observed live 2026-08-13), and
  `v2.session.message` keeps the `{data}` envelope over the dotted `Session.Message.Info`
  schema. Error maps are re-read from the v2 responses — do not assume the 1.x tables.
- [x] Map dotted schema names mechanically (`Session.Message.Info` → `SessionMessageInfo`;
  283 of 324 v2 schemas are dotted, so mechanical is the only sane default). Curated
  overrides exist only for collisions, C#-illegal results, and brand spellings — no taste
  renames (maintainer, 2026-08-13).
- [x] Adopt the v2 `location[...]` query convention as the only per-request scoping
  mechanism (maintainer, 2026-08-13: the v1 ledger is closed — no v1 convention, including
  `x-opencode-directory`, survives and no backward compatibility is owed).
- [x] Retire the Modern/Legacy surface split (`SpecSurface`, pending-legacy reporting,
  legacy placement rules): the pinned surface is single; simplify selection, curation
  validation, and generate reporting accordingly.
- [x] Regenerate the selected model closure; update curation rows, serialization tests, and
  fixtures to the v2 shapes; run the full repository gates.

## Task 1: Model Policy And Operation Binding

**Areas:** `Generator/Binding/`, `ModelEmitter`, `tools/curation.json`, Binder/emitter tests,
and committed generated models.

- [x] Change `CSharpNamePolicy` to ordinary PascalCase acronym handling and regenerate the
  selected closure. Assert representative `Id`, `SessionId`, `MessageId`, `CallId`, and `Url`
  mappings before approving an API baseline.
- [x] Route optional non-null collection copying through an internal helper whose input is
  nullable and output is non-null. Preserve immutable recursive copies; verify absent -> empty
  and explicit null -> `JsonException` under source-generated System.Text.Json.
- [x] Add `handleParameter` to group curation. Require it and `handleName` together,
  validate it names a required path parameter, and never consume a query/body value. Keep
  every pending operation flat.
- [ ] Add consumed-only `ClientPlan`, `OperationPlan`, `OperationParameterPlan`, `EnvelopePlan`,
  and `ErrorMapPlan` plus an `OperationPlanBinder`. Reuse schema-resolved type names instead of
  reinterpreting schemas.
- [ ] Bind only ordinary GET + JSON + path-parameter operations with one 200 success and
  no body/query/stream/wildcard. Bind the health success object to `HealthResponse`, curated
  `{data}` to `SessionMessageResponse.Message`, and the error maps Task 0 derived.
- [ ] Test complete plans, handle-versus-method parameters, collisions, documentation, status
  maps, batched failures, and determinism. A synthetic same-shape operation with different names
  must bind without new production branches; an operation in a group without a handle
  declaration must remain flat even when it carries `sessionID`.

## Task 2: Hand-Written HTTP Core

**Areas:** `src/OpenCode.Sdk/OpenCode.Sdk.csproj`, root options/response/exception types,
`Internal/Abstractions/IEnvironmentProvider`,
`Internal/{Pipeline,ResponseAdapter,SystemEnvironmentProvider}`, friend metadata, and tests.

- [ ] Add the conditional `net472` framework reference. Add `ErrorBehavior`, client/request
  options, `OpenCodeResponse` including nullable error-path `RawBody`, and the
  `OpenCodeException` spine with complete XML documentation.
- [ ] Implement endpoint validation/joining and owned-versus-injected `HttpClient` lifetime.
  Disposing the root makes its wrappers unusable and never disposes a BYO client.
- [ ] Resolve explicit password before one-time `OPENCODE_SERVER_PASSWORD` fallback. Decorate
  each request, never default headers, with Basic user `opencode` and the
  `OpenCode.Sdk/<informational-version>` User-Agent; per-request scoping rides the v2
  `location[...]` query convention, bound as operation parameters rather than a header.
- [ ] Use only `HttpClient.SendAsync(request, ResponseHeadersRead, cancellationToken)`. Own each
  request and buffered response, preserve cancellation, and wrap network/protocol failures as
  `OpenCodeTransportException`.
- [ ] Centralize default throw versus per-call `NoThrow` in
  `Pipeline.ExecuteAsync<TResponse>(HttpMethod, string, ResponseAdapter<TResponse>,
  OpenCodeRequestOptions?, CancellationToken)` where `TResponse : OpenCodeResponse`. Adapters
  deserialize only through `OpenCodeJsonContext` and construct success/error envelopes.

## Task 3: Generated Callable Surface And Ownership

**Areas:** new emitters, `SourceEmitter`, `GenerationWriter`, analyzer config, CI, and tests.

- [ ] Emit one file per public type: `OpenCodeClient`, `SessionsClient`, `SessionClient`, both
  responses, and `OpenCodeRoutes`; emit adapters under `Internal/ResponseAdapters/`.
  `OperationMethodEmitter` returns members to `ClientEmitter`, never partial operation files.
- [ ] Emit virtual client members/protected mock constructors, immutable handle state, escaped
  routes, guarded payload getters and `PrintMembers`, declared exception XML, and one pipeline
  delegation per operation method.
- [ ] Extend ownership to root generated files and the adapter subtree. Refuse unmanifested
  overwrites and refuse overwrite/deletion of manifest entries without the exact provenance
  header; retain case-insensitive collision checks and deterministic stale cleanup.
- [ ] Add one micro-snapshot per emitter family, aggregate generated-source compilation, and
  writer tests for mixed-root preservation, stale deletion, provenance, formatting, and drift.
- [ ] Enable the decided CS1591 gate, document hand-written public members, add only necessary
  rule arbitration, and add Linux CI `generate --verify`.

## Task 4: Contracts And M1 Closure

- [ ] Test real generated clients through a recording `HttpMessageHandler`: health 200/400/401;
  message 200/400/401 and both 404 variants; default and `NoThrow`; unknown/malformed errors;
  guarded payloads; route escaping; endpoint joining; auth/directory/User-Agent; cancellation;
  transport failure; response disposal; owned/BYO lifetime; and protected mock seams.
- [ ] Assert an undeclared status and a known tag returned at the wrong declared status both
  produce `UnknownOpenCodeError` with the exact tag and raw payload.
- [ ] For malformed non-2xx JSON, assert `Error` is null and exact `RawBody` is retained on both
  `OpenCodeApiException` and the `NoThrow` response. Assert success responses have no raw body.
- [ ] Pin `PublicApiGenerator` 11.5.4 centrally, add it plus `Verify.TUnit` to
  `OpenCode.Sdk.Tests`, and review one baseline after casing and callable surface settle. Keep
  `.generation-incomplete`; packing must still fail only because breadth is pending.
- [ ] Run `generate`, clean `generate --verify`, Release build, supported test legs, format,
  Slopwatch, and intentional pack refusal.
- [ ] Start a password-enabled real `opencode2 serve` (v2 pre-release channel; record the
  exact installed build version as demo evidence). Obtain IDs with authenticated raw calls to
  the pinned surface's session-list and message-list operations, then call only the two
  generated operations from a temporary `.scratchpad/` console. Print the health payload,
  concrete message type, and message `Id`; paste command/output into the PR and move
  `docs/ROADMAP.md` to M2.

## Acceptance Shape

```csharp
using var client = new OpenCodeClient(new Uri("http://localhost:4096"));

HealthResponse health = await client.GetHealthAsync(cancellationToken: cancellationToken);
SessionClient session = client.Sessions.GetSessionClient(sessionId);
SessionMessageResponse message = await session.GetMessageAsync(
    messageId,
    options: requestOptions,
    cancellationToken: cancellationToken);
```

Stop for maintainer review if binding needs Microsoft.OpenApi/raw JSON, an emitter needs an
operation/client-specific branch, the selected shape exceeds M1 capabilities, a structural
union appears, ownership cannot distinguish hand-written files, a temporary public/transport
API becomes necessary, or any locked product TFM cannot compile.

Run these final repository gates:

```text
dotnet run --file tools/opencode-tool.cs -- generate
dotnet run --file tools/opencode-tool.cs -- generate --verify
dotnet build --configuration Release
dotnet test --configuration Release --no-build
dotnet format --verify-no-changes --no-restore
dotnet tool run slopwatch analyze --exclude ".scratchpad/**,external/**" --fail-on warning
dotnet pack src/OpenCode.Sdk/OpenCode.Sdk.csproj --configuration Release
```

The first six commands must succeed. The pack command must fail only because
`.generation-incomplete` records pending operations.

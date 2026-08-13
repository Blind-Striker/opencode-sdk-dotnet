# Public API Design — OpenCode.Sdk & OpenCode.Sdk.Extensions

Date: 2026-08-13

> **Status: vision / reference — not sealed.** Binding decisions live in the ADRs and
> `AGENTS.md`; this document is direction and design rationale, not law. Contradicting it
> is a finding to note, not a deviation-protocol event.
>
> The v1.x dual-surface facts here (operation counts, the legacy hub, envelope inventories)
> predate the 2026-08-13 v2 retarget (ADR-0005) and are historical; the mechanisms remain
> reference material.

Design specification produced by the public-API brainstorm session (2026-08-08/09, ROADMAP
queue item 1). Every decision below was discussed and sealed individually with the
maintainer; rationale and evidence are recorded inline. Process sequencing agreed for what
follows this document: a **grill session** stress-tests this spec before any plan exists;
a **generator-architecture session** designs the generator's internals; then
`writing-plans` produces the (expectedly multi-phase) implementation plan.

## 1. Scope and inputs

**In scope:** the complete public API surface of `OpenCode.Sdk` and
`OpenCode.Sdk.Extensions` — error model, response envelopes, client composition, naming
and projection rules, transport and extensibility, options and DI, event model, model-layer
rules, and the launcher's public shape.

**Out of scope:** the generator's internal architecture (ingestion/SpecIR shape, emission
layering, curation-config format, spec-refresh tooling — its own design session); the MCP
server (sequenced later, ADR-0006); launcher implementation depth (deep-dive at
implementation, §13).

**The generator as a black box.** This spec treats the model-layer generator (ADR-0003)
purely through its contract:

- *Inputs:* the pinned spec (`spec/openapi.json`, v1.18.15) + a declarative, fail-closed
  curation config (naming map, handle rules, exclusions, per-property overrides,
  per-parameter type overrides, content-type→payload map).
- *Outputs:* models, response envelopes, request input records, op methods (modern +
  legacy), per-union tolerant converters (ADR-0009), forward-only paginators, the
  `[JsonSerializable]` registry, `OpenCodeRoutes` constants, the hand-wired-op
  fingerprint manifest (ADR-0008), emitted guard clauses, emitted XML docs.
- *Properties:* output passes the analyzer wall on merit (ADR-0003); `dotnet format`
  post-step; CI regen-verifies; emission respects MA0048 (one type per file), MA0051
  (80 lines / 60 statements per method), and the 150-column advisory.

**Evidence base:** research docs 01–12 (notably 08 codegen spike, 09/10 upstream
genealogy, 11 error-channel scope, 12 transport extensibility) plus primary-source
verification performed during the design and grill sessions: the pinned spec itself, the opencode JS SDK in the submodule (client wrappers,
hey-api generated client, `server.ts`, interceptors), Azure.Core / System.ClientModel
documentation, AWS SDK generator conventions, and two compile experiments (DIM
availability, MA0053 behavior) whose findings are stated where used.

## 2. Cross-cutting principles

### 2.1 Fail-closed first — two regimes, deliberately opposite

- **Build/generation time: fail-closed everywhere.** The generator breaks on unmapped
  operations, unknown spec constructs, and missing curation entries; CI regen-verify
  breaks on drift; the analyzer wall breaks on new rules. Defensiveness is highest
  exactly where plain generation meets customization.
- **Runtime wire compatibility: tolerant by explicit decision.** opencode ships hourly
  betas; a consumer's SDK being older than the server it talks to is normal operation,
  not an edge case. Unknown discriminators and unknown error tags must never kill the
  client — and every such tolerance point is an explicit, recorded decision (§14), never
  an accident.

### 2.2 Extend-only evolution

Within a major version, the public surface only grows: new sub-clients, new methods, new
options properties, new exception subtypes, new event variants. The mechanics chosen
throughout this spec (classes over interfaces, exception hierarchy over closed result
unions, options objects, curated maps that only gain rows) exist to keep every upstream
spec refresh shippable as a non-breaking minor. Upstream's own churn is absorbed at our
majors only (ADR-0005/0006).

### 2.3 Recorded deviations

Where this design knowingly departs from upstream's own design idiom, the departure is
recorded with rationale so a future reader does not "fix" it back (the ADR-0004 pattern).
The major one is the error model (§4.4).

## 3. Packages and TFMs (relay)

Package split, TFM matrix, and dependency policy are locked in `AGENTS.md` / ADR-0002 /
ADR-0006 and unchanged. This spec adds/confirms:

- **`System.Diagnostics.DiagnosticSource`** joins core's dependencies on downlevel TFMs
  (`ActivitySource`-based telemetry, §9.3; in-box on modern TFMs; no-op without
  listeners; Azure.Core precedent).
- **`OpenCode.Sdk` and `OpenCode.Sdk.Extensions` are co-developed** — their contracts
  evolve together in the same change (the `AddOpenCodeClient` → `IHttpClientBuilder`
  shape is part of the core transport design, not an afterthought). Versioning stays
  independent (ADR-0006). The MCP server remains sequenced after both.
- Wherever the SDK or its tooling performs filesystem I/O, Testably supplies the shared
  `System.IO.Abstractions.IFileSystem` contract: `Testably.Abstractions` in production and
  `Testably.Abstractions.Testing` in tests. The independent
  `TestableIO.System.IO.Abstractions.Analyzers` package enforces the seam repo-wide.

## 4. Error model

### 4.1 Decision

API failures throw through a **typed exception spine** by default; per-call `NoThrow` returns
them on the response spine. Transport and protocol failures always throw:

```csharp
public class OpenCodeException : Exception { }              // base — one catch point

public class OpenCodeApiException : OpenCodeException       // non-2xx API responses
{
    public int Status { get; }
    public OpenCodeError? Error { get; }                    // typed tagged payload; null when unparseable
    public string? RawBody { get; }                         // always available for diagnostics
}

public class OpenCodeTransportException : OpenCodeException // network/protocol-level failure
```

The spec's tagged error payloads (37 of the 44 error-named schemas at v1.18.15 — 20
Effect-style `_tag` + 17 `{name, data}`; the remaining 7 are event/union wrappers that
live in the event model) are generated as typed models under an `OpenCodeError` base and carried **as data** on the exception —
pattern-matchable exactly the way upstream's own TUI reads `result.error.name` /
`result.error.data.forceRequired`:

```csharp
try
{
    var resp = await client.Sessions.GetAsync(sessionId, cancellationToken: ct);
}
catch (OpenCodeApiException e) when (e.Error is SessionNotFoundError)
{
    // 404 — typed payload, no string sniffing
}
```

An **unknown error tag** from a newer server deserializes into a generic error carrier on
the base exception (tag string + raw payload) — never a crash (§2.1, §14).

The wire has two tagging conventions — Effect-style `{"_tag": "...", ...}` on HTTP error
bodies and `{"name": "...", "data": {...}}` on domain errors — both map onto the same
typed `OpenCodeError` hierarchy; the distinction is a generator concern, invisible to
consumers.

### 4.2 Why exceptions and not Result (four mechanisms)

1. **Open error set.** The spec regenerates on every upstream push; the error union can
   never be closed, so exhaustive matching — the actual payoff of Result/DU designs — is
   structurally unattainable, and adding variants to a closed union breaks exhaustive
   matchers (an extend-only violation). Adding an exception subtype breaks no one.
2. **Single error channel.** Result-returning methods still throw for usage errors,
   cancellation (`OperationCanceledException` is a BCL convention), and infrastructure —
   consumers would manage two failure channels per call; in practice one gets ignored.
3. **The stream plane must throw anyway.** SSE surfaces are `IAsyncEnumerable<T>`
   (locked); a mid-stream disconnect cannot be a return value. A Result API would split
   the SDK into two philosophies.
4. **Ecosystem memory.** AWS, Azure, System.ClientModel, OpenAI, Octokit all throw typed
   exceptions; .NET consumers' muscle memory and error-handling infrastructure assume it.

C# 14 has no discriminated unions; if C# 15 ships them, Result/Try companions can be
added **additively** — the reverse migration (Result → exceptions) would be a breaking
major. Option asymmetry favors the exception spine now.

### 4.3 Channel choice is per call site, not per error class

Upstream's own SDK resolves "expected vs exceptional" per call site, not per error type:
hey-api returns `{data, error}` by default with `throwOnError` opt-ins shipped by the
generator at both per-call and client level (hey-api machinery, not opencode design —
research doc 11); upstream's app/CLI carry ~76 non-generated `throwOnError` sites while
the TUI itself reads results with typed field access (the move-session dialog reads
`result.error.data.forceRequired`). We mirror the mechanism with the ecosystem-correct
default inverted: **throw by default, opt out per call** (§6). Azure/System.ClientModel
precedent: `RequestContext { ErrorOptions = ErrorOptions.NoThrow }` /
`ClientErrorBehaviors.NoThrow`, per-invocation, no global switch.

### 4.4 Recorded deviation from upstream

Upstream's client contract rejects with *tagged structural domain failures* (type guards,
not subclass identity) plus a single infrastructure `ClientError` (doc 02). That idiom is
correct for Effect/JS (exception identity is unreliable across JS bundles/realms;
errors-as-values is Effect's paradigm). This SDK deliberately renders the same taxonomy
through .NET's idiom instead: typed exceptions whose payload **is** the tagged data. What
upstream expresses structurally we express nominally; nothing in the taxonomy is lost,
and the per-call no-throw channel (§6) preserves the errors-as-values consumption style
where a call site wants it. Do not "fix" this back to a Result-first design without
revisiting §4.2's four mechanisms.

### 4.5 Errors on streams (three tiers)

1. **Establishment and mid-stream transport failure → exceptions** — thrown from the
   first/subsequent `MoveNextAsync` inside `await foreach`. No auto-reconnect (locked);
   resume is consumer-driven (durable stream: `after` cursor; live stream: refresh
   authoritative state and resubscribe).
2. **Run failures → events.** Provider/auth/output failures during a run arrive as typed
   `session.error` events (an 8-variant error union including `ApiError.IsRetryable`) —
   data in the stream; the run died, not the stream.
3. **Cancellation → `OperationCanceledException`** via the token (BCL convention).

`NoThrow` does not apply to streams.

## 5. Response envelopes

### 5.1 Decision

Every operation returns a **generated, typed envelope** (AWS response-object style, not a
generic `Result<T>`):

```csharp
public abstract record OpenCodeResponse
{
    public required int Status { get; init; }
    public bool IsError { get; init; }
    public OpenCodeError? Error { get; init; }   // populated only on NoThrow error path
    public string? RawBody { get; init; }        // populated only on NoThrow error path
}

public sealed record SessionListResponse : OpenCodeResponse
{
    private readonly IReadOnlyList<Session>? _sessions;
    public required IReadOnlyList<Session> Sessions   // wire envelope "data" → named property
    {
        get => _sessions ?? throw new InvalidOperationException(
            "The response is an error; check IsError before accessing Sessions.");
        init => _sessions = value;
    }
    public required SessionsCursor Cursor { get; init; }  // wire "cursor": {previous?, next?}
}
```

- Payload properties are **named** (never `.Value`), `required`, **non-nullable**, with a
  guarded getter: touching the payload of a NoThrow error response throws
  `InvalidOperationException` with an instructive message. The success path pays no null
  checks and gets no NRT warnings.
- The error path is constructed through an **internal `[SetsRequiredMembers]`
  error-constructor** — the single `null!` in the SDK lives there and never leaks.
- The v2 wire envelopes (`{data}` ×12, `{data, location}` ×20, paged `{cursor, data}`)
  map to named properties (`Location`, `Cursor` alongside the payload). A handful of
  modern responses are not enveloped (`{healthy}`, bare `LocationInfo`,
  `ProjectCopyCopy`) — the fail-closed curation map names their payloads like any other.
- 204 operations return an envelope with no payload property beyond the shared response
  metadata.
- Payload property names come from the curation map (fail-closed: an unmapped envelope
  breaks generation, § 8.5).
- Pagination follows upstream's Page discipline (doc 02): continuation accepts **only the
  cursor**; filters/ordering ride inside it (verified server-side: the cursor
  base64url-encodes the full filter set). The cursor is bidirectional — a generated
  `SessionsCursor { Previous, Next }` (both `string?`); list ops take `cursor: string?`.
  For `{cursor, data}` ops the generator additionally emits a forward-only
  `IAsyncEnumerable<T>` paginator walking `Next` (Azure `Pageable<T>` / AWS Paginators
  precedent; backward paging stays manual via `Previous`).
- Non-JSON response bodies generate through a fail-closed content-type→payload map:
  `application/octet-stream` → `Stream` payload on an envelope that implements
  `IDisposable` and owns the underlying response (`fs.read`; AWS `GetObjectResponse`
  precedent — consumers must dispose, and only these envelopes are disposable);
  `text/*` → `string` payload (`vcs.diff.raw`). An unmapped content type breaks
  generation.
- Because record `ToString()`/`PrintMembers` reads public getters, the generator emits a
  guarded `PrintMembers` override on every envelope: error-path instances print
  `Status`/`Error` and skip the payload — logging a `NoThrow` error response never
  throws.

### 5.2 Rejected alternatives

- **Generic `OpenCodeResponse<T>` with `.Value`** — loses named readability and has no
  home for per-op envelope extras (cursor, location).
- **Nullable payload properties** — moves the safety check to compile time for the NoThrow
  minority but taxes every default-path access with null-handling; contradicts ADR-0004's
  nullable-is-last-resort principle.
- **Azure-style two-layer surface** (typed convenience + protocol methods) — the typed
  envelope already carries status/error/raw metadata, so a second per-op layer would be
  2× public surface for no capability. Azure needs it for its codegen strategy; we don't.

## 6. Request options and channel selection

```csharp
public sealed class OpenCodeRequestOptions
{
    public ErrorBehavior ErrorBehavior { get; init; }   // Default | NoThrow
    public string? Directory { get; init; }             // per-call x-opencode-directory override
    public static OpenCodeRequestOptions NoThrow { get; }
}
```

- **Throw by default; `NoThrow` opt-in per call.** Decisive evidence for the default: 19
  of 61 modern operations return 204 (22 carry no 2xx JSON payload at all) — under a
  no-throw default an unchecked failure on a payload-less call is completely silent (no
  guarded getter to trip). C# has no must-use enforcement; upstream's consumers escape
  their result-default through ~76 scattered `throwOnError` opt-ins. The .NET SDK
  ecosystem is uniformly throw-default.
- **No global NoThrow.** A client-level switch changes method contracts at a distance —
  code reading `resp.Sessions` could no longer trust the line without knowing client
  configuration, and a shared DI client would flip behavior for every consumer.
  Research doc 11: no throw-default SDK ships a client-level error mode (FDG: "DO NOT
  have public members that can either throw or not based on some option"; every
  client-level switch found is escalation-only). Reversal trigger, extend-only: a
  scoped no-throw sub-view (`client.NoThrow`) may be added if MCP-server dogfooding
  demonstrates the need.
- `CancellationToken` stays a **separate last parameter** (TAP convention — deliberately
  unlike Azure's RequestContext, which folds it in).
- `Directory` resolves the ROADMAP question: client-level default (§10) + per-call
  override, the .NET rendering of upstream's `createOpencodeClient({directory})` +
  per-request header. Carried as the `x-opencode-directory` header on every request,
  SSE included (header-only; upstream's GET/HEAD query rewrite is a browser/EventSource
  constraint we don't share). Server precedence is query > header, so explicit per-op
  `location[…]` parameters override the ambient value — documented behavior.
- The options class grows extend-only (adding properties is non-breaking).

## 7. Client composition

### 7.1 Composition root, dual initialization

`OpenCodeClient` is the composition root. Both initialization paths are first-class:

```csharp
// Standalone
var client = new OpenCodeClient(new Uri("http://localhost:4096"));
var client = new OpenCodeClient(endpoint, new OpenCodeClientOptions { Password = "...", Directory = "/repo" });
var client = new OpenCodeClient(httpClient, options);          // BYO HttpClient

// DI (Extensions package)
services.AddOpenCodeClient(configuration.GetSection("OpenCode"));   // returns IHttpClientBuilder
services.AddOpenCodeClient(o => { o.Endpoint = ...; });
```

Sub-clients (`Sessions`, `Files`, …, `Legacy`) are readonly properties sharing one
transport core and options — lightweight facades, no per-sub-client DI registrations
(consumers wanting one inject the root and forward).

### 7.2 Bound session handle

Collection-level operations live on `Sessions` (`ListAsync`, `CreateAsync`,
`GetActiveAsync`); **session-scoped operations live only on the bound handle**:

```csharp
SessionClient session = client.Sessions.GetSessionClient(sessionId);
await session.PromptAsync(new SessionPrompt { ... }, ct);
await session.Permissions.ReplyAsync(requestId, reply, ct);
await session.InterruptAsync(ct);
```

Rule: **bound clients never cache server state** — a handle holds an immutable id plus
the shared pipeline reference (partial application, not an entity). Staleness is
impossible by construction: a deleted session 404s identically to the flat call. Azure
precedent: `GetBlobContainerClient(name)`. There is exactly one way to make a
session-scoped call (no duplicate flat overloads).

The **legacy surface has no handles** — flat calls only, matching its generated,
no-taste-investment nature (§8.4).

### 7.3 Legacy hub

`client.Legacy.…` is the single legacy-marked sub-surface required by ADR-0005; legacy
types live in the `OpenCode.Sdk.Legacy.*` namespace subtree. Deleting the legacy area at
our 2.0-absorbing major removes one property and one namespace subtree — "deleted
wholesale" made structural. The 16 stripped-name collisions resolve by namespace
separation. No `[Obsolete]`/`[EditorBrowsable]` stigma: legacy is an actively supported
surface (78 ops of today's real capability; the MCP server feeds on it) — the `Legacy`
name is the marker.

### 7.4 No interfaces; mockability via virtual members

**No `IOpenCodeClient` / per-sub-client interfaces** (the AWS `IAmazonS3` pattern is
rejected):

- Adding a member to an interface breaks every implementor (source: unimplemented-member
  error; binary: `TypeLoadException`); adding a method to a class breaks no caller and no
  inheritor (worst case CS0108 hiding *warning*). With upstream's velocity, our surface
  grows every minor — interfaces would turn each spec refresh into an implementor-breaking
  event.
- Default interface members cannot rescue this on our TFM matrix: **verified by
  compilation** — DIM with `LangVersion=14.0` fails with CS8701 ("target runtime doesn't
  support default interface implementation") on both net472 and netstandard2.0. DIM is
  runtime-gated (`RuntimeFeature.DefaultImplementationsOfInterfaces` absent on .NET
  Framework — terminal, no new CLR features) and cannot be polyfilled (Polyfill ships
  source, not runtime behavior). A DIM body also cannot reach client transport state, so
  it could not implement a new operation anyway.
- BCL precedent: ADO.NET shipped `IDbConnection` in .NET 1.0, hit exactly this wall, and
  .NET 2.0 moved evolution to `DbConnection` abstract classes; `Stream` never had an
  `IStream`. Interfaces remain right where polymorphism is real (multi-implementation
  contracts, consumer-owned ports) — an SDK client is one implementation plus test
  doubles.
- The interface + abstract-base "DIM mimic" hybrid was considered and rejected: "please
  derive from the base" is unenforceable in C#, so the interface's compat trap stays
  live while doubling the public surface.

**Mock seam:** client types (root, sub-clients, `SessionClient`) are unsealed with
`virtual` members and a `protected` parameterless constructor (Azure/OpenAI/gRPC
pattern). **Verified: this costs zero analyzer exceptions** — MA0053 with
`public_class_should_be_sealed = true` deliberately skips classes that declare virtual
members (the stricter `class_with_virtual_member_shoud_be_sealed` option is off);
follow-up: a one-line guard comment in `.editorconfig` records that the option is off on
purpose. Everything else — models, envelopes, options, exceptions, all generated types —
stays `sealed`. Response records with public `init` construct freely in tests; no model
factories needed. The shared `Pipeline` accessor throws an instructive
`InvalidOperationException` (never a bare NRE) when a client created through the mocking
constructor invokes a member the test did not override.

### 7.5 HttpClient ownership

**The provider owns the instance.** Injected `HttpClient` (including via
IHttpClientFactory): never disposed by the SDK. Self-created (no injection): owned and
disposed; modern TFMs use `SocketsHttpHandler` with pooled connection lifetime; net472
specifics fold into the existing spike items (including the
`ServicePointManager.DefaultConnectionLimit = 2` gotcha). Streaming requests use
`HttpCompletionOption.ResponseHeadersRead`, so `HttpClient.Timeout` (default 100 s)
governs only until response headers arrive and cannot kill long-lived SSE — the clean
counterpart of upstream's `req.timeout = false` fetch hack.

## 8. Naming and projection rules

### 8.1 Structural projection

- **The dotted operationId hierarchy is mirrored** as nested sub-clients
  (`session.permission.reply` → `Sessions`→handle→`Permissions.ReplyAsync`;
  `integration.attempt.cancel` → `Integrations.Attempts.CancelAsync`). Upstream's own
  generated client nests the same way.
- **Verb segments fold into method names** (`integration.connect.key` →
  `ConnectKeyAsync` — an action, not a resource).
- **Single-op root groups become root-client methods**: `GetHealthAsync()`,
  `GetLocationAsync()`.

### 8.2 Naming map (curation config v0)

| Group | Sub-client | Notes |
|---|---|---|
| `session` | `Sessions` + `SessionClient` handle | handle children: `Permissions`, `Questions`, `Revert`, `Events` (§11.1) |
| `fs` | `Files` | `ReadAsync` (binary → `Stream` payload, §5.1), `ListAsync`, `FindAsync` |
| `provider`, `model`, `agent`, `command`, `skill`, `reference`, `credential` | mechanical plural | `Providers`, `Models`, … |
| `integration` | `Integrations` (+ `Attempts`) | `ConnectKeyAsync` / `ConnectOAuthAsync` |
| `permission` | `Permissions` (+ `Requests`, `Saved`) | session-scoped ops on the handle |
| `question` | `Questions` (+ `Requests`) | |
| `event` | `Events` | `SubscribeAsync` → stream |
| `projectCopy` | `ProjectCopies` | |
| `pty` | `Pty` (singular-collective) | "Ptys" rejected; acronym casing §12.2 |
| `health`, `location` | root methods | |

### 8.3 Method naming rules

- `Async` suffix always. Verbless operationIds infer the verb from HTTP shape: GET single
  → `Get*`, GET collection → `List*` (`session.messages` → `ListMessagesAsync`).
- `remove` stays `RemoveAsync` (mechanical fidelity to upstream's verb; no `Delete`
  normalization — smaller curation surface, zero semantic gain).
- **Complex-body rule:** ≤ 2 scalar wire parameters → flat parameter list; more → a
  generated input record (`PromptAsync(new SessionPrompt { … })`).

### 8.4 All operation methods are generated

Both surfaces' methods (61 modern + 127 legacy) are **generator-emitted**. The earlier
"hand-written public surface" position (doc 06) is overturned by the maintainer on the
regen-radar argument: hand-written methods sit outside CI regen-verify and go silently
stale when the spec moves; generated methods make every spec drift a loud diff or a
broken build. Method bodies are one-line delegations —

```csharp
public virtual Task<SessionGetResponse> GetAsync(
    string sessionId, OpenCodeRequestOptions? options = null, CancellationToken cancellationToken = default)
    => Pipeline.ExecuteAsync(
        HttpMethod.Get,
        OpenCodeRoutes.Sessions.Get(sessionId),
        SessionGetResponseAdapter.Instance,
        options,
        cancellationToken);
```

— behavior (NoThrow, retry, error mapping) lives once in the hand-written core (§9), never
in bodies. Modern group curation may declare a bound handle's collection name, handle name, and
required path parameter; the Binder mechanically partially applies that parameter. Legacy stays
flat. Hand-written remains the identity
core: transport pipeline, SSE engine + stream endpoint wiring (stream endpoints are
detected by their `text/event-stream` content type — `x-effect-stream` exists only on
the durable endpoint — and are hand-wired; the generator emits their item schemas),
launcher, exception hierarchy, envelope base, options types, DI extensions.

The radar covers both sides of the boundary (ADR-0008): generated output is CI
regen-verified; every excluded or hand-wired operation is **fingerprint-pinned** — the
generator hashes each such operation's spec subtree (path, parameters, content types,
transitive schemas) into a committed manifest, and a spec refresh that moves a pinned
construct breaks the build for explicit review.

### 8.5 Curation is declarative and fail-closed

The naming map, handle rules, envelope payload names, exclusions, and per-property
overrides are generator **input config**, reviewed in PRs. A spec refresh that introduces
an operation/envelope/union with no mapping **breaks generation** — naming decisions are
forced, never improvised by the tool.

### 8.6 Route constants

`OpenCodeRoutes` is generated as the single source of truth for paths, used by generated
bodies and public for `SendAsync` consumers:

```csharp
public static class OpenCodeRoutes
{
    public static class Sessions
    {
        public const string List = "/api/session";
        public const string GetTemplate = "/api/session/{sessionID}";
        public static string Get(string sessionId) => $"/api/session/{Uri.EscapeDataString(sessionId)}";
    }
}
```

## 9. Transport and extensibility

### 9.1 One behavior core

A hand-written `ExecuteAsync` core is the single home of request behavior: request
decoration (auth header, directory, User-Agent), send via the injected/owned
`HttpClient`, idempotency-aware retry loop, response/error deserialization, tagged→typed
error mapping, throw-vs-populate per `ErrorBehavior`, telemetry.

### 9.2 BCL-first extensibility ladder — no invented framework

No custom pipeline/policy abstraction (`OpenCodePipelinePolicy` does not exist). The
guiding anti-pattern: abstraction-on-abstraction (the repository-over-EF-Core class of
mistakes). Consumers extend on three rungs, each an existing mechanism:

1. **Options knobs** — e.g. `OpenCodeClientOptions.Retry`. The built-in retry lives in
   the core, is ON by default, and retries **idempotent operations only** (stricter
   than `AddStandardResilienceHandler()`, which replays all methods unless
   `DisableForUnsafeHttpMethods()` is applied); for streams it applies to establishment
   only. One disable knob; no foreign-retry auto-detection (no surveyed SDK does it —
   research doc 12). Documented recipe: disable ours and add the official resilience
   handler at rung 3, routing the SSE/stream client around it (its 30 s total / 10 s
   attempt timeouts kill long-lived streams). In-box retry exists because bare-core
   consumers (no DI) otherwise have none; upstream's SDK ships zero retry and its one
   compensating consumer produced the write-replay bug (doc 03).
2. **Delegate hooks** — `OpenCodeClientOptions.OnSendingRequest` /
   `OnReceivedResponse`: **synchronous `void`**, BCL types only
   (`Action<HttpRequestMessage>` / `Action<HttpResponseMessage>`), fired **per physical
   attempt** (a retried request is a new message), after SDK decoration / immediately
   before send and before deserialization respectively; hook exceptions propagate
   unwrapped (invoked outside the transport-error mapping). Sync-only is the ecosystem
   shape (Azure's `HttpPipelineSynchronousPolicy.OnSendingRequest/OnReceivedResponse` —
   the names match verbatim — and AWS's `BeforeRequestEvent`; no major ships async
   delegate hooks on options — research doc 12): async work belongs at rung 3. This is
   the .NET rendering of upstream's own `interceptors.request/response.use` (upstream
   implements directory rewriting and a version-mismatch check exactly this way). JS's
   `interceptors.error.use` has no .NET counterpart because typed error mapping is core
   behavior here.
3. **`DelegatingHandler` chains** — full power, ecosystem-composable:
   `AddOpenCodeClient(...)` returns `IHttpClientBuilder`, so `.AddHttpMessageHandler<>()`,
   resilience handlers, and OTel HttpClient instrumentation compose naturally; standalone
   consumers inject a handler/HttpClient.

SDK context for rung-3 handlers travels via documented keys in
`HttpRequestMessage.Options` (`Properties` on net472) — an idiomatic bridge, no new types.

### 9.3 Telemetry

`ActivitySource` spans + `ILogger` (Logging.Abstractions) around operations, implemented
in the core (works above any transport). Downlevel TFMs require the
`System.Diagnostics.DiagnosticSource` package (§3).

### 9.4 Raw escape hatch

```csharp
Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
    OpenCodeRequestOptions? options = null, CancellationToken cancellationToken = default);
```

One root-client method: applies decoration and the pipeline, returns the raw response, no
typed mapping. Rationale: the fail-closed generator guarantees a standing window between
"op exists upstream" and "op exists in our release"; without an escape hatch consumers
are hostage to our release cadence. Paired with `OpenCodeRoutes` (§8.6).

## 10. Options and DI (three lifetimes)

| Layer | Type | Holds |
|---|---|---|
| Client lifetime | `OpenCodeClientOptions` | `Endpoint`, `Password`, default `Directory`, `Retry`, delegate hooks, logging plumbing |
| Single call | `OpenCodeRequestOptions` | `ErrorBehavior`, `Directory` override |
| Composition | `AddOpenCodeClient(...)` in Extensions | config binding (M.E.Options), `AddHttpClient<OpenCodeClient>`, returns `IHttpClientBuilder` |

- **Auth:** HTTP basic. Resolution order: explicit `Password` option →
  `OPENCODE_SERVER_PASSWORD` environment fallback (documented; upstream parity; read
  once at client construction) → launcher-supplied automatically (§13). Credentials
  never live in per-call options; SDK telemetry and logging never record the
  `Authorization` header or `Password` (requests handed to hooks/handlers are the
  consumer's own domain).
- Core never references `Microsoft.Extensions.Http`/DI (doc 06's 13-SDK consensus);
  Logging.Abstractions is the one tolerated core ME dependency.

## 11. Event model and streams

### 11.1 Two streams, two types (upstream commitment)

Upstream defines these as **distinct APIs with different schemas and guarantees** — "a
session ID is not a filter on the live stream" (doc 02). The SDK mirrors that:

```csharp
// Live: instance-wide, no replay. Disconnect ⇒ refresh authoritative state, resubscribe.
IAsyncEnumerable<OpenCodeEvent> client.Events.SubscribeAsync(CancellationToken ct);

// Durable: per-session, replayable. Resume via the aggregate-sequence cursor.
IAsyncEnumerable<SessionDurableEvent> session.Events.SubscribeAsync(long? after, CancellationToken ct);
Task<SessionHistoryResponse>          session.Events.ListHistoryAsync(long? after, int? limit, CancellationToken ct);
// ListHistoryAsync is a plain list op: after/limit parameters, {data, hasMore} envelope — no cursor.
```

- The live union is the spec's 88-variant event union; the durable stream's schema is
  `SessionDurableEvent` (the spec's single `oneOf`). They do not share a type.
- Wire note: the OpenAPI projection types `after`/`limit` as strings; the Effect source
  is `NumberFromString` — the SDK keeps numeric parameters via the curation
  per-parameter type override. Durable SSE frames carry `{id, event, data}` where
  `data` is a JSON-encoded `SessionDurableEvent`.
- Streams are lazy: the connection opens on first `MoveNextAsync`; the token cancels.
  No auto-reconnect (locked); the `after` cursor is consumer-held state.
- Mechanism: `SseParser` (`System.Net.ServerSentEvents`, downlevel package) over a
  `ResponseHeadersRead` response stream.
- Risk note (doc 10): the durable stream is protocol-surface-only with no legacy
  counterpart — the newest, least-proven part of the upstream surface; integration tests
  must exercise it early.

### 11.2 Unknown-discriminator rule (runtime tolerance, recorded)

An unknown `type`/tag value **never kills a stream and is never silently dropped** — it
surfaces as an explicit variant:

```csharp
case UnknownEvent u:            // u.Type (string), u.Payload (JsonElement)
    logger.LogDebug("Unknown event type {Type}", u.Type);
    break;
```

The same rule applies to **every generated union** (including the error union inside
`session.error`): one mechanical generator rule, no curation. Mechanism (ADR-0009): a
generator-emitted custom converter per union base — STJ's `UnknownDerivedTypeHandling`
is serialization-side only and cannot express deserialization fallback (spike: unknown
discriminator throws) — buffering the element, reading the tag position-independently,
and dispatching through the source-generated context so AOT holds. This resolves the
forward-compatibility question parked by the codegen spike.

## 12. Model-layer rules (generator policy additions to ADR-0004)

1. **`Uri` for URL-semantic properties** — endpoint/launcher URLs and model fields marked
   by spec `format: uri` or the curation map. Filesystem paths stay `string` (paths are
   not URIs). The analyzer wall is the fail-closed detector: CA1056/CA1054 firing on a
   generated `*Url` string property breaks the build and forces Uri-or-arbitration.
   Escape hatch if version skew ever delivers malformed URLs: per-property fallback to
   string, recorded in curation.
2. **Acronym casing: ordinary PascalCase** — all acronym tokens normalize consistently
   (`Id`, `Io`, `Ip`, `Url`, `Api`, `Pty`, `Mcp`, `Tui`, `Vcs`, `Lsp`); brand spellings use
   curated exceptions (`OAuth`). Resolves the spike's S101/CA1707 findings mechanically.
3. **Identifier mapping** — every wire name maps mechanically to PascalCase with
   `[JsonPropertyName]` carrying wire fidelity (`_tag`, snake_case, dotted schema names
   per doc 09's mangling requirement).
4. **`WhenWritingNull`** — nullable properties (rare by ADR-0004) default to
   `JsonIgnoreCondition.WhenWritingNull`; the spec's 7 model-level `anyOf`-null fields
   where null carries meaning are curatable to explicit-null per property (an eighth
   `anyOf`-null location lives inside SSE stream metadata the generator never models);
   an unmapped `anyOf`-null fails generation.
5. **Guard emission** — every generated method begins with BCL throw-helper guards
   (`ArgumentNullException.ThrowIfNull`, `ArgumentException.ThrowIfNullOrEmpty`;
   Polyfill covers downlevel TFMs). Coverage is mechanical, not memory-dependent. The
   hand-written core uses the same helpers; CA1062 is raised from `suggestion` to
   `error` so the analyzer wall guards this contract. No Contract
   framework (Code Contracts is dead tooling); invariants live in the type system (NRT,
   `required`, immutability, guarded getters); internal assumptions use `Debug.Assert`.
6. **XML documentation emission; CS1591 becomes `error`** — resolving the deferral
   recorded in doc 07 D9. Generated docs come from spec `summary`/`description` where
   present; where the spec carries no text — the norm for models (3/472 schemas,
   27/1836 properties at v1.18.15; operations are well covered at 185/188) — the
   generator emits a deterministic synthesized fallback from names and structure, so
   CS1591 stays `error` with no exemption. Operation methods additionally emit
   AWS-style `<exception cref>` lists from the spec's declared error responses.
   Hand-written surface is documented by hand.

## 13. Launcher public surface

```csharp
public sealed class OpenCodeServer : IAsyncDisposable, IDisposable
{
    public static Task<OpenCodeServer> StartAsync(
        OpenCodeServerOptions? options = null, CancellationToken cancellationToken = default);

    public Uri Endpoint { get; }        // parsed from "opencode server listening on <url>"
    public int ProcessId { get; }
    public OpenCodeClient CreateClient(OpenCodeClientOptions? options = null);
}

public sealed class OpenCodeServerOptions
{
    public string? BinaryPath { get; init; }            // default: PATH discovery ("opencode")
    public string Hostname { get; init; } = "127.0.0.1";
    public int? Port { get; init; }                     // null (default) = automatic; explicit = exact, fail-loud
    public string? WorkingDirectory { get; init; }
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public string? Password { get; init; }              // exported to the child as OPENCODE_SERVER_PASSWORD
    public OpenCodeConfig? Config { get; init; }        // serialized to OPENCODE_CONFIG_CONTENT (typed; upstream parity)
    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }
}
```

- Readiness mirrors upstream: parse stdout for the listening line (the URL must come from
  there anyway); timeout kills and reports captured output.
- Port selection: `null` (default) requests an automatic port — `--port=0` first (the
  readiness line prints the actually bound port; CLI acceptance of 0 is evidenced by
  upstream's own test fixture, which spawns the CLI with `--port 0` and parses the bound
  port from stdout — `test/lib/cli-process.ts`; release-binary confirmation at launcher
  build-out), falling back to a launcher-probed free port (`TcpListener(0)`) with bounded
  retry on child bind failure. An explicit `Port` is used exactly and fails loud on
  conflict — never scanned.
- `CreateClient()` wires endpoint + password automatically (§10 auth chain).
- Upstream's `createOpencodeTui` has no counterpart here (out of SDK scope).
- **Implementation is explicitly a deep-dive** (maintainer-flagged; not simple): the
  six-point anatomy of doc 06 §3 (arg quoting per TFM, continuous stdout/stderr drain
  against pipe-buffer deadlock, Unix SIGTERM P/Invoke grace, tree-kill fallbacks, Windows
  Job Object orphan protection, net11 light-up collapse), the ROADMAP net472 spike items,
  and port-conflict/ephemeral-port handling. Acceptance
  criterion stands: no merge without three-OS CI running real `opencode serve` start/stop
  tests. Reference implementation: MCP C# SDK `StdioClientTransport`.

## 14. Exclusions and recorded tolerances

**Exclusions (explicit, fail-closed list in curation config):**

- `pty.connect` — **both surfaces** (`v2.pty.connect` and legacy `pty.connect`): a
  WebSocket upgrade masquerading as a plain GET in every spec we may generate from; the
  shipping hey-api SDK emits a non-functional GET. We exclude rather than ship an API
  that cannot work; real WebSocket support is future extend-only work. Upstream's
  next-generation codegen excludes `fs.read`, `pty.connect`, and `pty.connectToken`
  (`omitEndpoints`); we deliberately deviate on the other two — `fs.read` generates
  through the binary payload rule (§5.1) and `pty.connectToken` as a normal op (its
  token stays usable with consumer-owned WebSocket code via the escape hatch).

**Recorded runtime tolerances (the §2.1 registry):**

1. Unknown union discriminators → `UnknownEvent`/unknown-variant carriers (§11.2).
2. Unknown error tags → base API exception with raw payload (§4.1).
3. `OPENCODE_SERVER_PASSWORD` environment fallback, read once at client construction
   (§10) — a runtime convenience, documented.
4. (Escape, if ever exercised) per-property `Uri`→`string` fallback for malformed URLs
   (§12.1).

## 15. Deferred and follow-ups

- **Generator internal architecture** — separate design session: ingestion/SpecIR shape,
  emission layering, curation-config format, exclusion mechanics, `.g.cs`-vs-on-merit
  file mechanics (ADR-0003), multi-TFM emission, spec-refresh tooling, emitter test
  strategy (snapshot testing). New scope from the grill: per-parameter type overrides,
  the content-type→payload map, per-union tolerant converter emission (ADR-0009),
  forward-only paginator emission, guarded `PrintMembers` emission, and the
  fingerprint manifest (ADR-0008).
- **Grill session — completed 2026-08-09.** Outcome: ADR-0007 (error model), ADR-0008
  (generation boundary + fingerprint pinning), ADR-0009 (unknown-variant tolerance);
  research docs 11 (error-channel scope) and 12 (transport extensibility); factual and
  design corrections applied throughout this spec.
- **Launcher deep-dive** (§13) and the net472 spike items (ROADMAP); the grill adds
  `--port=0` verification and child bind-failure signature detection.
- **UNVERIFIED items carried forward:** the
  `sync.*` group's relation to the durable stream (doc 10); upstream migration-guide
  existence (doc 09). (Ephemeral-port support left this list 2026-08-10: upstream's own
  test fixture spawns the CLI with `--port 0` — §13.)

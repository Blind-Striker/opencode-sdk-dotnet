# Error-channel scope: per-call NoThrow only, or a client-level default too?

Date: 2026-08-13

> Research snapshot, 2026-08-09. Question: given the sealed throw-by-default error model
> (typed `OpenCodeException` spine, per-call `OpenCodeRequestOptions.ErrorBehavior`),
> should `OpenCodeClientOptions` also carry a client-level `ErrorBehavior` default?
> Sources: Microsoft Learn API reference (Azure.Core, System.ClientModel, System.Net.Http,
> Framework Design Guidelines), azure.github.io .NET guidelines, AWS SDK for .NET API
> reference, elastic.co .NET client reference, GitHub source of openai-dotnet,
> elastic-transport-net, octokit.net, stripe-dotnet, Refit README, heyapi.dev docs and
> hey-api issue tracker, and the pinned `external/opencode` submodule (v1.18.15) — all
> retrieved 2026-08-09. Throw-vs-Result itself is sealed and not relitigated here.

## 1. The question

Every operation throws typed exceptions by default and returns a typed envelope
(`Status`/`IsError`/`Error`/`RawBody` + guarded payload getter); a per-call
`OpenCodeRequestOptions { ErrorBehavior = Default | NoThrow }` exists. The only open
point is scope: is per-call the *only* channel switch, or does
`OpenCodeClientOptions` get an `ErrorBehavior` default (per-call still overriding)?
The stakes: DI-shared client trust and line-level readability versus ergonomics for
NoThrow-heavy consumers (our own planned MCP server).

## 2. Precedent survey

| SDK / layer | Throw default? | No-throw channel? | Scope of switch | Switch toggles |
|---|---|---|---|---|
| Azure SDK for .NET (Azure.Core) | Yes — `RequestFailedException` | Yes, protocol methods only | **Per-call**: `RequestContext.ErrorOptions = ErrorOptions.NoThrow` | Error-status responses only |
| System.ClientModel | Yes — `ClientResultException` | Yes | **Per-call**: `RequestOptions.ErrorOptions = ClientErrorBehaviors.NoThrow` | Error-status responses; caller must check `Response.IsError` |
| OpenAI .NET (on System.ClientModel) | Yes | Yes, protocol methods only | **Per-call** `RequestOptions` (as above) | `Response.IsError` responses |
| AWS SDK for .NET | Yes — `AmazonServiceException` | No | none | — |
| Elastic.Clients.Elasticsearch | **No** — response-based (`IsValid`) | Throw is the *opt-in* | **Client-level** `ElasticsearchClientSettings.ThrowExceptions()` **and per-request** `IRequestConfiguration.ThrowExceptions` | Client and server call failures become exceptions |
| Refit | Yes — `ApiException` | Yes | **Per-signature**: return `Task<ApiResponse<T>>` / `IApiResponse<T>` | Non-success responses wrapped instead of thrown |
| gRPC for .NET | Yes — `RpcException` | No | none | — |
| Octokit | Yes — `ApiException` + subtypes | No | none | — |
| Stripe.net | Yes — `StripeException` | No | none | — |
| Raw `HttpClient` (BCL) | **No** for status codes | Throw is the *opt-in* | **Per-response**: `EnsureSuccessStatusCode()` | Non-2xx status → `HttpRequestException` |
| hey-api fetch client (upstream opencode JS SDK) | **No** — `{data, error}` tuple | Throw is the *opt-in* | Generator-config default **+ client-level** `createClient({throwOnError})` **+ per-call** (type-level generic) | Which channel the call resolves to; per-call flag also flips the *static return type* |

Per-SDK notes:

- **Azure.Core** — `RequestContext.ErrorOptions`: "Controls under what conditions the
  operation raises an exception if the underlying response indicates a failure"
  ([Learn](https://learn.microsoft.com/en-us/dotnet/api/azure.requestcontext.erroroptions)).
  `RequestContext` is a per-invocation parameter of protocol methods; typed convenience
  methods are throw-only. Client-level `Azure.Core.ClientOptions` exposes Diagnostics,
  Retry, RetryPolicy, Transport — **no error-behavior property**
  ([Learn](https://learn.microsoft.com/en-us/dotnet/api/azure.core.clientoptions)).
- **System.ClientModel** — `ClientErrorBehaviors` flags enum: `Default` "will throw an
  exception … if the service returns an error response"; `NoThrow` "will not throw …
  Callers of the service method must check the Response.IsError property"
  ([Learn](https://learn.microsoft.com/en-us/dotnet/api/system.clientmodel.primitives.clienterrorbehaviors)).
  It lives solely on per-call
  [`RequestOptions`](https://learn.microsoft.com/en-us/dotnet/api/system.clientmodel.primitives.requestoptions);
  client-level
  [`ClientPipelineOptions`](https://learn.microsoft.com/en-us/dotnet/api/system.clientmodel.primitives.clientpipelineoptions)
  (logging, tracing, timeout, retry, transport) has **no error-behavior property**.
- **OpenAI .NET** — source-verified: the pipeline throws unless the per-call options
  carry the flag — `if (message.Response.IsError && (options?.ErrorOptions &
  ClientErrorBehaviors.NoThrow) != ClientErrorBehaviors.NoThrow) throw new
  ClientResultException(...)`
  ([ClientPipelineExtensions.cs](https://github.com/openai/openai-dotnet/blob/main/OpenAI/src/Custom/Internal/ClientPipelineExtensions.cs)).
  Only protocol methods accept `RequestOptions`; convenience methods are throw-only.
  `OpenAIClientOptions` derives from `ClientPipelineOptions` — no switch.
- **AWS** — "Most exceptions thrown to client code will be service-specific exceptions"
  ([AmazonServiceException](https://docs.aws.amazon.com/sdkfornet/v3/apidocs/items/Runtime/TServiceException.html)).
  [`ClientConfig`](https://docs.aws.amazon.com/sdkfornet/v3/apidocs/items/Runtime/TClientConfig.html)
  lists retry/timeout/endpoint options; the only "throw" mentions concern config
  validation. No error-channel option at any scope.
- **Elastic** — the strongest client-level precedent, but in the *opposite direction*:
  default is "a c/go like error checking on response.`IsValid`"; `ThrowExceptions`
  opts *into* throwing, settable client-wide on `ElasticsearchClientSettings`
  ([elastic.co](https://www.elastic.co/docs/reference/elasticsearch/clients/dotnet/_options_on_elasticsearchclientsettings))
  and per-request via `IRequestConfiguration.ThrowExceptions`
  ([elastic-transport-net source](https://github.com/elastic/elastic-transport-net/blob/main/src/Elastic.Transport/Configuration/IRequestConfiguration.cs)).
- **Refit** — `ApiException` on non-success; declaring the interface method as
  `Task<ApiResponse<T>>`/`IApiResponse<T>` selects the wrapped, non-throwing channel.
  Purely signature-level — no runtime switch at either scope
  ([README](https://github.com/reactiveui/refit)). The channel choice is visible in the
  *type* at every call site.
- **gRPC for .NET** — "awaiting a unary gRPC call returns the message … and throws an
  `RpcException` if there's a failure"; no non-throwing mode documented at any scope
  ([Learn](https://learn.microsoft.com/en-us/aspnet/core/grpc/error-handling)).
- **Octokit** — `Connection.HandleErrors` throws `ApiException` (or a mapped subtype)
  unconditionally for every status ≥ 400
  ([Connection.cs](https://github.com/octokit/octokit.net/blob/main/Octokit/Http/Connection.cs)).
- **Stripe.net** — `LiveApiRequestor.RequestAsync` is documented "Thrown if the request
  fails" and ends in an unconditional `throw BuildStripeException(readResponse)`
  ([LiveApiRequestor.cs](https://github.com/stripe/stripe-dotnet/blob/master/src/Stripe.net/Infrastructure/Public/LiveApiRequestor.cs)).
- **BCL `HttpClient`** — no-throw for status codes; `EnsureSuccessStatusCode()` is the
  per-response opt-in that "throws an `HttpRequestException` if `StatusCode` is outside
  of the range 200-299"
  ([Learn](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpresponsemessage.ensuresuccessstatuscode)).

### hey-api and upstream opencode: machinery vs choice

Verified in the pinned submodule: the JS SDK is generated by `@hey-api/openapi-ts`
0.90.10 (`external/opencode/packages/sdk/js/package.json`), and **both** scopes are
generator-shipped machinery, not opencode design — `ClientOptions.throwOnError?: boolean`
and the per-call `throwOnError` (`@default false`) live in
`packages/sdk/js/src/v2/gen/client/types.gen.ts` (a `.gen` file). The client-level
*default* was added to hey-api by user request
([hey-api/hey-api#961](https://github.com/hey-api/hey-api/issues/961)); hey-api's own
migration notes track the option's moves across generator config and client config
([heyapi.dev](https://heyapi.dev/docs/openapi/typescript/migrating)). What opencode
*chose* is only the consumption pattern: client-level `createClient({ throwOnError:
true })` in the app layer (`packages/app/src/context/server-sdk.tsx`,
`.../directory-sync.ts`), heavy per-call `{ throwOnError: true }` in the ACP/CLI layer
(`packages/opencode/src/acp/service.ts` et al.), and occasional per-call de-escalation
`{ throwOnError: false }` (`packages/app/src/components/terminal.tsx`). Upstream's docs
table (`packages/web/src/content/docs/sdk.mdx`) confirms the default: "`throwOnError` —
Throw errors instead of return — `false`".

Two mechanisms make this precedent non-transferable to C#:

1. **Direction.** Upstream's client-level flag *escalates* (no-throw default → opt into
   throwing). Escalation-by-config is safe: worst case, an exception surfaces where a
   check already existed. Our proposed client-level flag would *suppress* (throw default
   → opt into silence) — the direction in which config-at-a-distance destroys signal.
2. **Types.** hey-api's per-call `throwOnError` is a **type-level generic**
   (`ThrowOnError extends boolean = false` on every generated method,
   `packages/sdk/js/src/v2/gen/sdk.gen.ts`): passing it per call flips the static return
   type from `{data, error}` tuple to bare data. The client-level flag is runtime-only —
   unannotated call sites keep the pessimistic tuple type, so the compiler still forces
   an error check even when runtime throws. C# has no equivalent: our envelope type is
   identical under both behaviors, so a client-level flip is invisible to the compiler
   at every call site.

## 3. First-party guidance

- Framework Design Guidelines,
  [Exception Throwing](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/exception-throwing):
  "❌ DO NOT return error codes." "✔️ DO report execution failures by throwing
  exceptions." And decisively for this question: **"❌ DO NOT have public members that
  can either throw or not based on some option."**
- The sanctioned non-throwing alternatives are *member-granular*, not object modes:
  Tester-Doer and Try-Parse, with "✔️ DO provide an exception-throwing member for each
  member using the Try-Parse Pattern"
  ([Exceptions and Performance](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/exceptions-and-performance)).
- The Azure .NET guidelines require throwing — "DO throw `RequestFailedException` or
  its subtype when a service method fails with non-success status code" — and contain
  **no** client-level error-behavior guidance
  ([azure.github.io](https://azure.github.io/azure-sdk/dotnet_introduction.html)).
  Azure/System.ClientModel's per-call `NoThrow` is thus a deliberate, narrowly scoped
  modern deviation from the FDG rule: the option exists, but only where the call site
  literally names it, and only on the protocol-method sub-surface.
- BCL layering points the same way: where the platform is no-throw by default
  (`HttpClient`), the throw upgrade is a visible per-response call, not a client mode.

Net: across every surveyed throw-by-default .NET SDK, the no-throw channel — where it
exists at all — is selected **per call** (Azure, System.ClientModel, OpenAI) or **per
signature** (Refit). **No throw-by-default SDK surveyed offers a client-level error-mode
switch.** The only client-level switches found (Elastic, hey-api) sit on
response-default clients and point in the escalation direction.

## 4. Mechanisms for and against a client-level default here

Against (per-call only):

- **Line-level trust.** `resp.Sessions` after an un-optioned call is safe *iff* the
  client is throw-default. A client-level flag makes every such line unreviewable
  without knowing DI configuration — precisely the FDG "throw or not based on some
  option" failure mode, widened from a member to the whole client.
- **DI sharing.** One registration serves many consumers; a library or second team
  member resolving `OpenCodeClient` inherits a silently flipped contract. None of the
  surveyed .NET SDKs make this possible.
- **The 204 exposure.** 19 of 61 modern ops return 204 (design spec §6): under NoThrow
  there is no payload getter to trip, so an unchecked failure is *completely* silent.
  A client-level NoThrow default converts this from a per-call, locally visible risk
  into a config-at-a-distance one.
- **Extend-only evolution.** Shipping without the client-level knob is extend-only
  reversible: it can be added later without breaking anyone. Shipping *with* it cannot
  be removed without a break.

For (client-level default):

- **Dogfood ergonomics.** The MCP server maps failures to tool-error results uniformly;
  per-call-only means `OpenCodeRequestOptions.NoThrow` on nearly every call. Upstream
  felt the mirror-image pain: its app/CLI layers carry approximately 76 non-generated
  `throwOnError: true` call sites to escape result-default behavior (design spec §6).
- **Upstream symmetry.** The JS SDK has the client-level knob and upstream's app uses
  it — but §2 shows it is inherited hey-api machinery, opposite in direction, and
  type-guarded in a way C# cannot replicate. The symmetry is superficial.

## 5. Recommendation (decision input — maintainer seals)

**Keep per-call as the only runtime channel switch. Do not add
`OpenCodeClientOptions.ErrorBehavior`.** Every throw-by-default precedent stops at
per-call/per-signature scope; the only client-level precedents run in the safe
(escalating) direction on response-default clients; FDG explicitly forbids
option-conditional throwing, and the per-call form is the ecosystem's tightly contained
deviation from that rule. The 19×204 silent-failure surface makes the suppressing
direction uniquely dangerous for this API, and per-call-only is the extend-only choice.

Middle path, if MCP-server ergonomics prove painful in practice: a **scoped no-throw
sub-view** rather than a config default — e.g. `client.NoThrow` (or
`client.WithOptions(OpenCodeRequestOptions.NoThrow)`) returning a view that pre-applies
the option. Mechanism precedent: Refit encodes the channel in the visible signature;
Azure separates the sub-surfaces (convenience vs protocol). Pros: the channel choice
stays legible at the call site or in a narrowly scoped local
(`var nt = client.NoThrow;`), DI keeps one uniform registration, and the MCP server
gets its one-liner. Cons: a second public surface to generate and document, and a
`nt`-style local can still drift from its declaration — weaker than per-call
visibility, far stronger than DI-config invisibility. This is additive, so it can wait
for demonstrated need. A named-options/second-registration pattern (a differently
configured client under a distinct service key) is the weakest acceptable variant:
it at least makes the flipped behavior a *different registered object*, but call sites
still read identically — not recommended while the sub-view option exists.

## 6. Sources

- [RequestContext.ErrorOptions — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/azure.requestcontext.erroroptions)
- [ClientErrorBehaviors enum — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.clientmodel.primitives.clienterrorbehaviors)
- [RequestOptions — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.clientmodel.primitives.requestoptions) / [ClientPipelineOptions — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.clientmodel.primitives.clientpipelineoptions) / [Azure.Core.ClientOptions — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/azure.core.clientoptions)
- [Azure SDK .NET guidelines — azure.github.io](https://azure.github.io/azure-sdk/dotnet_introduction.html)
- [AmazonServiceException — AWS SDK for .NET API reference](https://docs.aws.amazon.com/sdkfornet/v3/apidocs/items/Runtime/TServiceException.html) / [ClientConfig](https://docs.aws.amazon.com/sdkfornet/v3/apidocs/items/Runtime/TClientConfig.html)
- [Options on ElasticsearchClientSettings — elastic.co](https://www.elastic.co/docs/reference/elasticsearch/clients/dotnet/_options_on_elasticsearchclientsettings) / [IRequestConfiguration.cs — elastic-transport-net](https://github.com/elastic/elastic-transport-net/blob/main/src/Elastic.Transport/Configuration/IRequestConfiguration.cs)
- [ClientPipelineExtensions.cs — openai/openai-dotnet](https://github.com/openai/openai-dotnet/blob/main/OpenAI/src/Custom/Internal/ClientPipelineExtensions.cs)
- [Refit README](https://github.com/reactiveui/refit)
- [Error handling with gRPC on .NET — Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/grpc/error-handling)
- [Connection.cs — octokit/octokit.net](https://github.com/octokit/octokit.net/blob/main/Octokit/Http/Connection.cs)
- [LiveApiRequestor.cs — stripe/stripe-dotnet](https://github.com/stripe/stripe-dotnet/blob/master/src/Stripe.net/Infrastructure/Public/LiveApiRequestor.cs)
- [HttpResponseMessage.EnsureSuccessStatusCode — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpresponsemessage.ensuresuccessstatuscode)
- [FDG: Exception Throwing](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/exception-throwing) / [FDG: Exceptions and Performance](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/exceptions-and-performance)
- [hey-api migration notes — heyapi.dev](https://heyapi.dev/docs/openapi/typescript/migrating) / [hey-api/hey-api#961](https://github.com/hey-api/hey-api/issues/961)
- Pinned submodule `external/opencode` (v1.18.15): `packages/sdk/js/package.json`,
  `packages/sdk/js/src/v2/gen/client/types.gen.ts`, `packages/sdk/js/src/v2/gen/sdk.gen.ts`,
  `packages/app/src/context/server-sdk.tsx`, `packages/opencode/src/acp/service.ts`,
  `packages/app/src/components/terminal.tsx`, `packages/web/src/content/docs/sdk.mdx`

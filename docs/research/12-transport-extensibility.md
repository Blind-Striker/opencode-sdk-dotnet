# Transport extensibility and resilience: precedent survey

Date: 2026-08-09

> Research snapshot, 2026-08-09. Primary sources only: Azure SDK for .NET source and
> Microsoft Learn API reference (Azure.Core, System.ClientModel), aws/aws-sdk-net source +
> AWS developer guide, dotnet/extensions source + Learn resilience docs, ASP.NET Core gRPC
> docs, openai/openai-dotnet source, octokit/octokit.net source, stripe/stripe-dotnet
> README, microsoft/kiota-dotnet source, nuget.org package metadata. All retrieved
> 2026-08-09. Informs design-spec §9 (transport & extensibility ladder); the maintainer
> seals all decisions — §§5–7 are decision input, not decisions.

## 1. Question and stakes

The design spec pins a three-rung extensibility ladder: options knobs, BCL-typed delegate
hooks (`OnSendingRequest`/`OnReceivedResponse`), and `DelegatingHandler` chains — with an
internal idempotency-aware retry loop in the hand-written `ExecuteAsync` core and an
explicit "no invented pipeline framework" prior. Three questions are under stress-test:
(1) is a sync-only delegate rung idiomatic, or does it sacrifice necessary async
capability; (2) who should own retry when consumers can attach the official
`Microsoft.Extensions.Http.Resilience` stack to our `IHttpClientBuilder` (double-retry
hazard); (3) if hooks stay, do they run per-call or per-attempt relative to retry.

## 2. Precedent table

| SDK | Pipeline model | Hook shape | Retry owner | Retry replaceable how | Double-retry guidance |
|---|---|---|---|---|---|
| Azure.Core | Policy chain (`HttpPipelinePolicy`) | Policy objects, async+sync duals; sync convenience base `HttpPipelineSynchronousPolicy` with `void OnSendingRequest/OnReceivedResponse` | Built-in `RetryPolicy` inside the pipeline | `ClientOptions.Retry` knobs; `ClientOptions.RetryPolicy` = subclass or `DelayStrategy` | Caveat: replacing `RetryPolicy` drops library response classifiers |
| System.ClientModel | Policy chain (`PipelinePolicy`) | Policy objects, async+sync duals | `ClientRetryPolicy` via `ClientPipelineOptions.RetryPolicy` | Set `RetryPolicy` property; per-call `RequestOptions.AddPolicy` | Positioning made explicit: `PerCall` (before retry) vs `PerTry` (after retry) |
| AWS SDK for .NET | Internal handler pipeline (`IPipelineHandler`) | **Sync events** on the client: `BeforeRequestEvent`/`AfterResponseEvent`/`ExceptionEvent` (`EventHandler` style, `void`) | Retry handler in pipeline, driven by `ClientConfig.RetryMode`/`MaxErrorRetry` | Config knobs only; pipeline customization is `protected virtual`/`Internal`-namespace | None found |
| ME.Http.Resilience | `DelegatingHandler` wrapping Polly v8 pipeline | n/a — it *is* the handler rung | Retry strategy inside the standard handler | `AddResilienceHandler` (custom), `RemoveAllResilienceHandlers` | "Only add one resilience handler and avoid stacking handlers" |
| gRPC .NET | `Interceptor` objects (async via continuation) | Async interceptor methods; sync `BlockingUnaryCall` separate | **Channel** `ServiceConfig` `RetryPolicy` — not interceptors | Channel config only; hedging XOR retry | Streaming: never retried after first response message ("committed") |
| OpenAI .NET | Inherits SCM pipeline (`OpenAIClientOptions : ClientPipelineOptions`) | SCM policy objects | SCM retry (3 retries, exp. backoff, 408/429/5xx) | Via inherited SCM options | — |
| Octokit | None — `Connection(…, IHttpClient)` injection only | None | None shipped | n/a | n/a |
| Stripe.net | None — `IHttpClient`/`HttpClient` injection only | None | Internal (`MaxNetworkRetries`, auto idempotency keys) | Knob only (set to 0) | n/a |
| Kiota | `DelegatingHandler` middleware chain | Handlers only | `RetryHandler` — a public `DelegatingHandler` in the default chain | Replace/reorder handler list; per-request `RetryHandlerOption` | n/a |

## 3. Per-SDK notes

### Azure.Core

- `HttpPipelinePolicy` is abstract with **both** `ProcessAsync(HttpMessage, ReadOnlyMemory<HttpPipelinePolicy>)`
  (async) and `Process(...)` (sync) — the dual exists because Azure clients expose sync
  *and* async service methods ([API ref](https://learn.microsoft.com/en-us/dotnet/api/azure.core.pipeline.httppipelinepolicy)).
- Consumers insert policies with `ClientOptions.AddPolicy(policy, position)`;
  `HttpPipelinePosition` = `PerCall` (0, "invoked once per pipeline invocation"),
  `PerRetry` (1, "invoked every time request is retried"), `BeforeTransport` (2)
  ([AddPolicy](https://learn.microsoft.com/en-us/dotnet/api/azure.core.clientoptions.addpolicy),
  [enum](https://learn.microsoft.com/en-us/dotnet/api/azure.core.httppipelineposition)).
- **Sync hook precedent:** `HttpPipelineSynchronousPolicy` — "a policy that doesn't do any
  asynchronous or synchronously blocking operations" — exposes exactly
  `void OnSendingRequest(HttpMessage)` / `void OnReceivedResponse(HttpMessage)`
  ([API ref](https://learn.microsoft.com/en-us/dotnet/api/azure.core.pipeline.httppipelinesynchronouspolicy)).
  Our spec's rung-2 names match this precedent verbatim.
- Retry: defaults 3 retries, exponential, 0.8 s initial / 1 min max; tune via
  `ClientOptions.Retry` (`Delay`, `MaxDelay`, `MaxRetries`, `Mode`, `NetworkTimeout`;
  Retry-After header always honored) ([RetryOptions](https://learn.microsoft.com/en-us/dotnet/api/azure.core.retryoptions),
  [Configuration.md](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/core/Azure.Core/samples/Configuration.md)).
  Full replacement via `ClientOptions.RetryPolicy`: subclass `RetryPolicy` (per-attempt
  virtuals `OnSendingRequest`/`OnRequestSent` + `ShouldRetry(Async)`, or take over
  `Process(Async)` entirely) or pass a `DelayStrategy`
  ([RetryPolicy](https://learn.microsoft.com/en-us/dotnet/api/azure.core.pipeline.retrypolicy)).
  Documented caveat: if you swap in your own policy without delegating to the base,
  "the library-specific response classifiers *will not* be respected" — Azure's answer to
  external retry is "replace ours properly", not auto-detection
  ([Configuration.md](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/core/Azure.Core/samples/Configuration.md)).

### System.ClientModel (SCM)

Same architecture, generalized: `PipelinePolicy` with `Process`/`ProcessAsync`
(`PipelineMessage`, `IReadOnlyList<PipelinePolicy>`, `int currentIndex`)
([API ref](https://learn.microsoft.com/en-us/dotnet/api/system.clientmodel.primitives.pipelinepolicy)).
`ClientPipelineOptions` carries `RetryPolicy`, `Transport`, `NetworkTimeout`, and
`AddPolicy(policy, PipelinePosition)`; `PipelinePosition.PerCall` is defined as "insert
**before** the pipeline's RetryPolicy", `PerTry` as "insert **after** the RetryPolicy …
run each time the pipeline tries to send", plus `BeforeTransport`
([options](https://learn.microsoft.com/en-us/dotnet/api/system.clientmodel.primitives.clientpipelineoptions),
[enum](https://learn.microsoft.com/en-us/dotnet/api/system.clientmodel.primitives.pipelineposition)).
`RequestOptions.AddPolicy(policy, position)` scopes a policy to a single service-method
call ([API ref](https://learn.microsoft.com/en-us/dotnet/api/system.clientmodel.primitives.requestoptions)).
The per-call/per-try distinction is thus a first-class, documented concept in both
Microsoft client stacks.

### AWS SDK for .NET

- The pipeline is real but not a consumer extension point: `IPipelineHandler` is a public
  interface (`void InvokeSync`, `Task<T> InvokeAsync<T>`)
  ([source](https://github.com/aws/aws-sdk-net/blob/main/sdk/src/Core/Amazon.Runtime/Pipeline/IPipelineHandler.cs)),
  but customization goes through `protected virtual void CustomizeRuntimePipeline(RuntimePipeline)`
  (subclass-only) on `AmazonServiceClient`, or the global singleton
  `RuntimePipelineCustomizerRegistry` — public class, **`Amazon.Runtime.Internal`
  namespace** (used by AWS's own extension packages)
  ([source](https://github.com/aws/aws-sdk-net/blob/main/sdk/src/Core/Amazon.Runtime/Internal/RuntimePipelineCustomizerRegistry.cs)).
- The *supported* consumer hooks are classic **sync events** on the client:
  `public event RequestEventHandler BeforeRequestEvent`, `AfterResponseEvent`
  (`ResponseEventHandler`), `ExceptionEvent` (`ExceptionEventHandler`) — all
  `void (object sender, EventArgs e)` delegates. `WebServiceRequestEventArgs` exposes
  headers, parameters, service name, endpoint, and the original request object — **SDK-level
  data, not `HttpRequestMessage`**
  ([AmazonServiceClient.cs](https://github.com/aws/aws-sdk-net/blob/main/sdk/src/Core/Amazon.Runtime/AmazonServiceClient.cs),
  [RequestHandler.cs](https://github.com/aws/aws-sdk-net/blob/main/sdk/src/Core/Amazon.Runtime/RequestHandler.cs)).
- Retry is config-only: `ClientConfig.RetryMode` (Legacy/Standard/Adaptive) +
  `MaxErrorRetry`, env vars `AWS_RETRY_MODE`/`AWS_MAX_ATTEMPTS`; no documented custom
  retry replacement ([dev guide](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/retries-timeouts.html)).
  The HTTP transport is injectable via `ClientConfig.HttpClientFactory`
  ([source](https://github.com/aws/aws-sdk-net/blob/main/sdk/src/Core/Amazon.Runtime/_netstandard/ClientConfig.cs)).

### gRPC .NET

Client `Interceptor` overrides (`AsyncUnaryCall`, `AsyncServerStreamingCall`, …) are
async-capable via the continuation/call-object pattern; the sync path
(`BlockingUnaryCall`) is a *separate* override — the two are explicitly not
interchangeable ([interceptors](https://learn.microsoft.com/en-us/aspnet/core/grpc/interceptors)).
Retry is **not** an interceptor concern: it is channel configuration
(`GrpcChannelOptions.ServiceConfig` → `MethodConfig.RetryPolicy` with `MaxAttempts`,
backoff, `RetryableStatusCodes`). Streaming precedent directly relevant to our SSE rule:
"Streaming RPCs that return multiple messages from the server **won't retry after the
first message has been received**" (the call becomes "committed"); apps re-establish
manually. Hedging cannot be combined with a retry policy
([retries](https://learn.microsoft.com/en-us/aspnet/core/grpc/retries)).

### OpenAI .NET / Octokit / Stripe.net (single-service calibration)

- **OpenAI**: ships zero invented surface — `OpenAIClientOptions : ClientPipelineOptions`
  ([source](https://github.com/openai/openai-dotnet/blob/main/OpenAI/src/Custom/OpenAIClientOptions.cs)),
  so the whole SCM rung (policies, `RetryPolicy`, `Transport = HttpClientPipelineTransport(httpClient)`)
  comes inherited; README documents auto-retry "up to three additional times using
  exponential backoff" on 408/429/500/502/503/504
  ([README](https://github.com/openai/openai-dotnet/blob/main/README.md)).
- **Octokit**: no pipeline, no hooks, no retry; the extension point is constructor
  injection of `IHttpClient` into `Connection`
  ([source](https://github.com/octokit/octokit.net/blob/main/Octokit/Http/Connection.cs)); README shows none of this.
- **Stripe**: internal retries on connection errors/timeouts/409 with **idempotency keys
  always added** to make retries safe; one knob (`MaxNetworkRetries`, settable to 0);
  custom `HttpClient` via `SystemNetHttpClient`; "no explicit request/response hooks,
  interceptors, or middleware" ([README](https://github.com/stripe/stripe-dotnet/blob/master/README.md)).

### Kiota (Microsoft.Kiota.Http.HttpClientLibrary)

Microsoft's generated-SDK stack builds *everything* as `DelegatingHandler` middleware:
default C# chain includes Retry, Redirect, ParametersNameDecoding, UserAgent,
HeadersInspection, UriReplacement; custom middleware = write a `DelegatingHandler`, add it
to `KiotaClientFactory.CreateDefaultHandlers()`, chain via
`ChainHandlersCollectionAndGetFirstLink`
([middleware doc](https://learn.microsoft.com/en-us/openapi/kiota/middleware)).
`RetryHandler` retries 429/503, honors `Retry-After` (seconds or HTTP-date), exponential
backoff otherwise, per-request override via `RetryHandlerOption`, and — key detail —
**only retries when `request.IsBuffered()`**, i.e. never replays unbuffered/streaming
content ([RetryHandler.cs](https://github.com/microsoft/kiota-dotnet/blob/main/src/http/httpClient/Middleware/RetryHandler.cs)).
The cost of handler-hosted retry is visible in the source: the handler must clone the
request (`CloneAsync`) for every attempt because `HttpRequestMessage` is single-send.

## 4. Microsoft.Extensions.Http.Resilience deep-dive

- **TFMs (nuget.org, v10.8.0):** `net8.0; net9.0; net10.0; netstandard2.0; net462` — it
  covers our entire matrix including net472 (≥ 4.6.2) and ns2.0
  ([nuget](https://www.nuget.org/packages/Microsoft.Extensions.Http.Resilience)).
- **Standard pipeline** (`AddStandardResilienceHandler()`), outermost→innermost:
  rate limiter (1000 concurrent) → **total timeout 30 s** → retry (3 attempts,
  exponential, jitter, 2 s base) → circuit breaker (10 % failure ratio, min throughput
  100, 30 s sampling, 5 s break) → **attempt timeout 10 s**
  ([Learn](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience)).
- **Retryability:** `HttpClientResiliencePredicates.IsTransient` = status ≥ 500, 408, 429,
  or `HttpRequestException`/`TimeoutRejectedException`; `ShouldRetryAfterHeader = true`
  installs a `DelayGenerator` honoring `Retry-After`
  ([predicates](https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.Http.Resilience/Polly/HttpClientResiliencePredicates.cs),
  [options](https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.Http.Resilience/Polly/HttpRetryStrategyOptions.cs)).
  **Idempotency is NOT considered by default** — it "makes retries for all HTTP methods";
  opting out is explicit: `options.Retry.DisableFor(...)` or
  `DisableForUnsafeHttpMethods()` (POST/PATCH/PUT/DELETE/CONNECT)
  ([Learn](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience)).
- **Stacking guidance:** "you should only add one resilience handler and avoid stacking
  handlers"; `RemoveAllResilienceHandlers()` exists precisely to clear and rebuild
  ([Learn](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience)).
  **No official guidance was found telling SDKs to auto-disable internal retry when a
  resilience handler is present** — no precedent SDK does runtime detection; the pattern
  everywhere is documentation plus a disable knob (Azure caveat doc, gRPC channel-level
  ownership, Stripe `MaxNetworkRetries = 0`).
- **No-DI story:** `ResilienceHandler` is a public `DelegatingHandler` constructible
  directly (`new ResilienceHandler(pipeline) { InnerHandler = socketsHandler }`) — the
  documented pattern for static/singleton clients without a container
  ([HttpClient guidelines](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines)).
- **Streaming hazard (analysis):** the standard handler's 30 s total timeout and 10 s
  attempt timeout apply to every request on that client — a long-lived SSE `GET` through a
  standard-resilience client will be killed by design. Any recipe we document must route
  the event stream around the resilience handler (separate named client) or reconfigure
  timeouts; stream *resume* stays consumer-driven via the `after` cursor.

## 5. Analysis — retry ownership options

**(a) Core-internal idempotency-aware loop** (current spec). Matches the clear precedent
majority: Azure.Core, SCM/OpenAI, AWS, Stripe, and gRPC all own retry inside the
client/channel core, on by default, with knobs to tune and a way to disable — none of them
delegate default resilience to the handler chain. The internal loop also has strictly
better information than any handler: it knows the operation (spec-level idempotency, not
just HTTP-method heuristics), rebuilds requests without `CloneAsync` machinery, and knows
when it is establishing an SSE stream (retry establishment only — the gRPC "committed"
rule). Double-retry is handled the precedent way: a first-class disable knob plus a
documented recipe. Cost: our loop and StandardResilience can silently multiply
(3 × 4 = 12 sends) if the consumer reads no docs.

**(b) No retry in core; handler-chain guidance only.** Keeps the core smallest and makes
the official stack the only retry engine (dependency-wise viable even standalone:
ME.Http.Resilience covers ns2.0/net462, and `ResilienceHandler` works without DI — §4).
But no surveyed SDK except Octokit ships zero default resilience, and Octokit is the
thinnest client in the table; default DX would regress below upstream's own JS SDK.

**(c) Retry as a public `DelegatingHandler` in our default chain** (Kiota-style). Works
standalone and composes/removes naturally under `IHttpClientBuilder`; exactly one major
precedent (Kiota — notably Microsoft's *generated*-client stack, not its hand-written
ones). Costs: request cloning per attempt, idempotency knowledge must travel via
`HttpRequestMessage.Options` keys instead of being ambient in `ExecuteAsync`, and the
SSE-establishment-only rule needs the `IsBuffered()`-style guard rather than direct
knowledge. It also puts a public type on our API surface that duplicates what
StandardResilience already is.

Constraint check: all three options are dependency-clean for core (option a/c need no new
package; option b pushes the dependency to consumers). TFMs are a non-issue (§4).

## 6. Analysis — hook-rung shape

Precedent on delegate hooks is unambiguous: **no surveyed major ships async delegate hooks
on options.** The shapes that exist are (i) sync `void` event/virtual hooks — AWS's
`BeforeRequestEvent`/`AfterResponseEvent` events and Azure's
`HttpPipelineSynchronousPolicy.OnSendingRequest`/`OnReceivedResponse` (§3) — and
(ii) full async *object* rungs (policies/interceptors/handlers). Azure even splits the two
deliberately: the sync base exists because header-stamping/observation needs no async, and
anything async graduates to a real policy. Our ladder mirrors that split with rung 3
(`DelegatingHandler`, fully async) already present, so a sync-only rung 2 sacrifices
nothing — async hook logic has a home one rung up, and inventing an async delegate rung
would *exceed* what any major exposes on options (the "no invented framework" prior cuts
against it).

Positioning: both Microsoft stacks make per-call vs per-attempt explicit (`PerCall` =
before the retry policy, `PerRetry`/`PerTry` = after it; §3). For delegate hooks that
mutate a concrete `HttpRequestMessage`, per-attempt is the only coherent choice in our
design — each attempt rebuilds the message, so a per-call mutation would silently vanish
on retries. Azure's own `RetryPolicy.OnSendingRequest` runs per attempt, "even for the
first attempt". Per-call semantics remain available to consumers as a rung-3 handler
registered *outside* whatever resilience handler they add (handler order in
`IHttpClientBuilder` is consumer-controlled), so we need no second hook pair.

Dropping rung 2 entirely (Stripe/Octokit ship nothing) is defensible but discards the
upstream-parity argument: opencode's JS SDK ships `interceptors.request/response.use`, and
AWS/Azure show a lightweight sync hook rung is an idiomatic, cheap convenience.

## 7. Recommendation (decision input — maintainer seals)

- **Retry: option (a)** — keep the internal idempotency-aware loop, aligned with the
  precedent majority. Sharpen it with: retry only spec-idempotent operations by default
  (stricter than StandardResilience's retry-everything default, matching Stripe's
  safety posture); honor `Retry-After`; single obvious disable
  (`Retry.MaxRetries = 0` or a `Disabled` flag); SSE = establishment-only. Ship a
  documented StandardResilience recipe in `OpenCode.Sdk.Extensions` docs: disable our
  retry, add the handler, and route the event-stream client around it (timeout hazard,
  §4). No runtime detection of foreign handlers — no precedent does it.
- **Hooks: keep rung 2 sync-only**, explicitly per-attempt, `void`-returning BCL
  signatures — precedent-named (`HttpPipelineSynchronousPolicy`, AWS events) and
  documented as "fast, non-blocking mutation/observation; anything async is a
  DelegatingHandler". If the maintainer weighs async capability above precedent, the
  correct move per this survey is dropping rung 2 in favor of handler recipes — not an
  async delegate rung, which nothing in the field validates.

## 8. Sources

- [Azure.Core `HttpPipelinePolicy`](https://learn.microsoft.com/en-us/dotnet/api/azure.core.pipeline.httppipelinepolicy) / [`HttpPipelinePosition`](https://learn.microsoft.com/en-us/dotnet/api/azure.core.httppipelineposition) / [`ClientOptions.AddPolicy`](https://learn.microsoft.com/en-us/dotnet/api/azure.core.clientoptions.addpolicy) / [`RetryOptions`](https://learn.microsoft.com/en-us/dotnet/api/azure.core.retryoptions) / [`RetryPolicy`](https://learn.microsoft.com/en-us/dotnet/api/azure.core.pipeline.retrypolicy) / [`HttpPipelineSynchronousPolicy`](https://learn.microsoft.com/en-us/dotnet/api/azure.core.pipeline.httppipelinesynchronouspolicy) — Microsoft Learn API reference
- [Azure.Core samples: Configuration.md](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/core/Azure.Core/samples/Configuration.md); [Azure SDK .NET guidelines (HttpPipeline)](https://azure.github.io/azure-sdk/dotnet_introduction.html)
- [System.ClientModel `PipelinePolicy`](https://learn.microsoft.com/en-us/dotnet/api/system.clientmodel.primitives.pipelinepolicy) / [`ClientPipelineOptions`](https://learn.microsoft.com/en-us/dotnet/api/system.clientmodel.primitives.clientpipelineoptions) / [`PipelinePosition`](https://learn.microsoft.com/en-us/dotnet/api/system.clientmodel.primitives.pipelineposition) / [`RequestOptions`](https://learn.microsoft.com/en-us/dotnet/api/system.clientmodel.primitives.requestoptions)
- aws/aws-sdk-net: [AmazonServiceClient.cs](https://github.com/aws/aws-sdk-net/blob/main/sdk/src/Core/Amazon.Runtime/AmazonServiceClient.cs), [RequestHandler.cs](https://github.com/aws/aws-sdk-net/blob/main/sdk/src/Core/Amazon.Runtime/RequestHandler.cs), [ResponseHandler.cs](https://github.com/aws/aws-sdk-net/blob/main/sdk/src/Core/Amazon.Runtime/ResponseHandler.cs), [ExceptionHandler.cs](https://github.com/aws/aws-sdk-net/blob/main/sdk/src/Core/Amazon.Runtime/ExceptionHandler.cs), [IPipelineHandler.cs](https://github.com/aws/aws-sdk-net/blob/main/sdk/src/Core/Amazon.Runtime/Pipeline/IPipelineHandler.cs), [RuntimePipelineCustomizerRegistry.cs](https://github.com/aws/aws-sdk-net/blob/main/sdk/src/Core/Amazon.Runtime/Internal/RuntimePipelineCustomizerRegistry.cs), [ClientConfig.cs (netstandard)](https://github.com/aws/aws-sdk-net/blob/main/sdk/src/Core/Amazon.Runtime/_netstandard/ClientConfig.cs); [AWS dev guide: retries and timeouts](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/retries-timeouts.html)
- [Learn: Build resilient HTTP apps](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience); dotnet/extensions: [HttpClientResiliencePredicates.cs](https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.Http.Resilience/Polly/HttpClientResiliencePredicates.cs), [HttpRetryStrategyOptions.cs](https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.Http.Resilience/Polly/HttpRetryStrategyOptions.cs), [HttpRetryStrategyOptionsExtensions.cs](https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.Http.Resilience/Polly/HttpRetryStrategyOptionsExtensions.cs); [nuget.org: Microsoft.Extensions.Http.Resilience 10.8.0 (TFM list)](https://www.nuget.org/packages/Microsoft.Extensions.Http.Resilience); [Learn: HttpClient guidelines — resilience with static clients](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines)
- [Learn: gRPC interceptors on .NET](https://learn.microsoft.com/en-us/aspnet/core/grpc/interceptors); [Learn: Transient fault handling with gRPC retries](https://learn.microsoft.com/en-us/aspnet/core/grpc/retries)
- openai/openai-dotnet: [README](https://github.com/openai/openai-dotnet/blob/main/README.md), [OpenAIClientOptions.cs](https://github.com/openai/openai-dotnet/blob/main/OpenAI/src/Custom/OpenAIClientOptions.cs); octokit/octokit.net: [README](https://github.com/octokit/octokit.net/blob/main/README.md), [Connection.cs](https://github.com/octokit/octokit.net/blob/main/Octokit/Http/Connection.cs); stripe/stripe-dotnet: [README](https://github.com/stripe/stripe-dotnet/blob/master/README.md)
- [Learn: Kiota middleware](https://learn.microsoft.com/en-us/openapi/kiota/middleware); microsoft/kiota-dotnet: [RetryHandler.cs](https://github.com/microsoft/kiota-dotnet/blob/main/src/http/httpClient/Middleware/RetryHandler.cs)

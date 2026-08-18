# Caller-supplied transport: BYO-HttpClient precedent survey

Date: 2026-08-14

> Dated evidence and decision history, not current policy. Q91 below was superseded by Q92;
> ADR-0010 owns the current construction and transport decision.
>
> Research snapshot, 2026-08-14. Primary sources only, retrieved fresh at pinned release
> tags: Azure SDK for .NET source (Azure.Core 1.61.0, System.ClientModel 1.15.0,
> Microsoft.Extensions.Azure 1.14.0), openai/openai-dotnet 2.13.0, aws/aws-sdk-net v4
> (AWSSDK.Core 4.0.101), stripe/stripe-dotnet 52.3.0, octokit/octokit.net 14.0.0,
> Refit 15.0.0, Microsoft.Kiota.Http.HttpClientLibrary 2.0.0, Grpc.Net.Client 2.83.0,
> Elastic.Clients.Elasticsearch 9.5.0, ModelContextProtocol.Core 2.2.0, dotnet/runtime
> v10.0.11, learn.microsoft.com, nuget.org. Informs Q91 (research log Session 23); the
> maintainer sealed the decision — §§5–6 record analysis, not law.

## 1. Question and stakes

Review blocker #1 exposed the public `(HttpClient, options)` constructor's default-header
ambiguity: with v2's optional auth, `Password == null` writes no `Authorization`, and the
BCL then copies a caller client's default `Authorization` onto the nominally anonymous
request. Q91 asked whether the constructor stays public (with a first-class resolution of
the ambiguity) or transport injection narrows to the DI companion, with no-DI needs met by
concrete option knobs.

## 2. Census

| SDK | Caller-supplied transport | DI-gated? | Header-conflict guard | No-DI proxy/TLS |
|---|---|---|---|---|
| Azure.Core 1.61 / SCM 1.15 | Public: `options.Transport = new HttpClientTransport(httpClient)` (options property + wrapper; never a client ctor) | No — the DI companion has zero transport API | None; BCL merge decides silently | Pass-through is the documented proxy recipe; Azure.Core adds concrete TLS knobs (`HttpPipelineTransportOptions`) |
| OpenAI 2.13 | Public: inherited SCM `Transport` property; `HttpClient` absent from its public API | No | None; per-request `Set` wins silently | Pass-through only (README mTLS recipe); zero knobs by in-source design comment |
| AWS v4 (Core 4.0.101) | Public: `ClientConfig.HttpClientFactory` abstraction (factory, never a raw instance); absent on net472 | No — v4 removed the knob from its DI options type (issue aws-sdk-net#3790) | None; SigV4 per-request wins | Concrete-knobs-first (ProxyHost/Port/Credentials…), factory escape hatch; zero TLS knobs |
| Stripe.net 52.3 | Public: ctor/options param carrying a wrapper (`new SystemNetHttpClient(httpClient)`) | No — no DI package exists | None; in-source comment claims headers per-message "even when a custom HTTP client is used" | Pass-through only via the wrapper |
| Octokit 14.0 | Public: `IHttpClient` abstraction + `Func<HttpMessageHandler>`; a live `HttpClient` cannot be passed | No — no DI package | Structurally unrepresentable in the stock path | Handler factory; proxy ergonomics admittedly poor |
| Refit 15 / Kiota 2.0 / MCP 2.2 | Public raw `HttpClient` (method param / optional ctor param / ctor param + `ownsHttpClient` flag) | No | None | Pass-through (Kiota adds one `IWebProxy` convenience) |
| Grpc.Net.Client 2.83 | Public: `GrpcChannelOptions.HttpClient` / `HttpHandler` | No | Transport-shape guards only (throws when both are set; refuses credentials over insecure channels) | `HttpHandler` pass-through |
| Elastic 9.5 | No raw `HttpClient` anywhere — transport abstraction + full concrete knob set | No (architectural, not packaging) | N/A structurally; explicit in-source precedence rule | The lone full-knobs design; pays with an internal client factory keyed on config hashes |
| opencode JS v2 (our pin, `packages/client`) | Public `fetch` option on the generated promise client | n/a | Code-defined: SDK/per-request headers overwrite client-level `headers` | n/a — a custom `fetch` is stateless; the ambient-header hazard does not exist in JS |

## 3. Load-bearing findings

1. **DI-gating transport injection has zero precedent.** No surveyed SDK restricts
   transport to a DI companion; Azure's companion composes over the public core knob and
   owns nothing; AWS v4 deliberately deleted `HttpClientFactory` from its DI options type
   and pointed users back at core. Internalize-with-IVT would be an ecosystem-first.
2. **Nobody guards the header conflict, and the anonymous leak is ecosystem-universal.**
   dotnet/runtime v10.0.11: default headers are copied onto a request only for names the
   request does not already set (`HttpHeaders.AddHeaders` — "we don't try to merge").
   Every SDK sets auth per-request, so caller default `Authorization` is inert on
   authenticated calls; in every surveyed anonymous mode (Azure credential-less clients,
   AWS `AnonymousAWSCredentials`, Kiota's anonymous provider, Refit) the caller default
   flows onto the wire, undocumented and unguarded.
3. **"Anonymous by omission" is impossible over a caller client.** The BCL has no
   suppress-default-header mechanism; a request lacking `Authorization` inherits the
   client default. An SDK with optional auth must refuse the conflict or document the
   leak — and the same leak exists on the DI path (`ConfigureHttpClient` can set default
   headers on the factory client), so the Pipeline guard is required regardless of where
   Q91 lands.
4. **The stock typed-client pattern hard-requires a public `(HttpClient, …)` ctor.**
   `AddHttpClient<TClient>()` resolves constructors via `ActivatorUtilities` →
   `Type.GetConstructors()` (public only); an internal ctor fails at first
   `CreateClient` with `InvalidOperationException`. The sanctioned route to an internal
   ctor is `AddTypedClient(Func<HttpClient, TClient>)` — usable by our own Extensions,
   invisible to consumers' stock registrations.

## 4. Alternatives weighed

- **Internalize + IVT to Extensions** (the pre-survey leaning): rejected on findings 1,
  3, and 4 — precedent-free gating, safety not actually gained (the guard is needed for
  the factory path anyway), stock `AddHttpClient<OpenCodeClient>()` forecloses, and the
  standalone handler/proxy/TLS door closes with no knob replacement shipped.
- **Handler seam on options** (SDK always owns the client, consumers hand
  `DelegatingHandler`s — the Octokit/Kiota family): rejected on the Q90 options
  contract — `OpenCodeClientOptions` is bindable data snapshotted once at construction,
  and a live handler graph breaks binding, snapshotting, and the `IOptions<>` flow.
  (Honest note: ASP.NET Core itself puts handlers on options-pattern types —
  `JwtBearerOptions.BackchannelHttpHandler` — so the shape exists; it conflicts with our
  own sealed options semantics, not with the ecosystem.) A ctor-parameter variant would
  keep options pure but still break the stock typed-client pattern.
- **Transport-wrapper options property** (Azure/OpenAI shape): rejected — it pays off
  only atop a pipeline/transport abstraction this SDK deliberately does not have; for us
  it is the same raw `HttpClient` with the same hazard and more public surface.

## 5. Decision at Session 23 (superseded by Q92 / ADR-0010)

The `(HttpClient, options)` constructor stays public. Anonymous mode fails closed:
`Password == null` while the injected client's `DefaultRequestHeaders` carry
`Authorization` is refused at construction and before every send (construction-only would
miss legal post-construction mutation of default headers). With `Password` set, the SDK's
per-request `Authorization` deterministically wins (BCL request-wins merge) and the
precedence contract is documented on the constructor and options. Standalone
handler/proxy/TLS composition rides the BYO client; concrete convenience knobs may be
added additively if demand appears. Executes as review blocker #1's fix.

## 6. Sources

- Azure: [ClientOptions.cs](https://github.com/Azure/azure-sdk-for-net/blob/3830815f87881cce7af68dd9dd4126cbd90e197b/sdk/core/Azure.Core/src/ClientOptions.cs), [HttpClientTransport.cs](https://github.com/Azure/azure-sdk-for-net/blob/3830815f87881cce7af68dd9dd4126cbd90e197b/sdk/core/Azure.Core/src/Pipeline/HttpClientTransport.cs), [HttpClientTransport.Request.cs](https://github.com/Azure/azure-sdk-for-net/blob/3830815f87881cce7af68dd9dd4126cbd90e197b/sdk/core/Azure.Core/src/Pipeline/HttpClientTransport.Request.cs); [Microsoft.Extensions.Azure on nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Azure)
- OpenAI: [OpenAIClient.cs](https://github.com/openai/openai-dotnet/blob/0aafb4c006c69db476607940b10c26fc07de8607/OpenAI/src/Custom/OpenAIClient.cs), [OpenAIClientOptions.cs](https://github.com/openai/openai-dotnet/blob/0aafb4c006c69db476607940b10c26fc07de8607/OpenAI/src/Custom/OpenAIClientOptions.cs), [OpenAIClientUtilities.cs](https://github.com/openai/openai-dotnet/blob/0aafb4c006c69db476607940b10c26fc07de8607/OpenAI/src/Utility/OpenAIClientUtilities.cs), [README](https://github.com/openai/openai-dotnet/blob/0aafb4c006c69db476607940b10c26fc07de8607/README.md)
- AWS: [ClientConfig.cs (netstandard)](https://github.com/aws/aws-sdk-net/blob/36107286c1ac8dc5dee970e5b34584a7046e6231/sdk/src/Core/Amazon.Runtime/_netstandard/ClientConfig.cs), [HttpRequestMessageFactory.cs](https://github.com/aws/aws-sdk-net/blob/36107286c1ac8dc5dee970e5b34584a7046e6231/sdk/src/Core/Amazon.Runtime/Pipeline/HttpHandler/_netstandard/HttpRequestMessageFactory.cs), [AWSConfigs.netstandard.cs](https://github.com/aws/aws-sdk-net/blob/36107286c1ac8dc5dee970e5b34584a7046e6231/sdk/src/Core/_netstandard/AWSConfigs.netstandard.cs)
- Stripe: [StripeClient.cs](https://github.com/stripe/stripe-dotnet/blob/19fb69ecd290b671b64a18c9008152bdc882cc5b/src/Stripe.net/Infrastructure/Public/StripeClient.cs), [SystemNetHttpClient.cs](https://github.com/stripe/stripe-dotnet/blob/19fb69ecd290b671b64a18c9008152bdc882cc5b/src/Stripe.net/Infrastructure/Public/SystemNetHttpClient.cs)
- Octokit: [Connection.cs](https://github.com/octokit/octokit.net/blob/7fa5b0fe4a18c9b981b21290c3ca9320b2d6415b/Octokit/Http/Connection.cs), [HttpClientAdapter.cs](https://github.com/octokit/octokit.net/blob/7fa5b0fe4a18c9b981b21290c3ca9320b2d6415b/Octokit/Http/HttpClientAdapter.cs), [HttpMessageHandlerFactory.cs](https://github.com/octokit/octokit.net/blob/7fa5b0fe4a18c9b981b21290c3ca9320b2d6415b/Octokit/Http/HttpMessageHandlerFactory.cs)
- Typed-client ecosystem: [RestService.cs](https://github.com/reactiveui/refit/blob/74cbb64d51b705fb52a7bc0e7bd5f4e4b62bfb5e/src/Refit/RestService.cs), [HttpClientRequestAdapter.cs](https://github.com/microsoft/kiota-dotnet/blob/2aa504cc9b566ab92f93a4f1a3a2ba2c5048a9a4/src/http/httpClient/HttpClientRequestAdapter.cs), [KiotaClientFactory.cs](https://github.com/microsoft/kiota-dotnet/blob/2aa504cc9b566ab92f93a4f1a3a2ba2c5048a9a4/src/http/httpClient/KiotaClientFactory.cs)
- BCL/guidance: [HttpClient.cs L741-779](https://github.com/dotnet/runtime/blob/v10.0.11/src/libraries/System.Net.Http/src/System/Net/Http/HttpClient.cs#L741-L779), [HttpHeaders.cs L617-665](https://github.com/dotnet/runtime/blob/v10.0.11/src/libraries/System.Net.Http/src/System/Net/Http/Headers/HttpHeaders.cs#L617-L665), [DefaultTypedHttpClientFactory.cs](https://github.com/dotnet/runtime/blob/v10.0.11/src/libraries/Microsoft.Extensions.Http/src/DefaultTypedHttpClientFactory.cs), [ActivatorUtilities.cs L679-711](https://github.com/dotnet/runtime/blob/v10.0.11/src/libraries/Microsoft.Extensions.DependencyInjection.Abstractions/src/ActivatorUtilities.cs#L679-L711), [HttpClient guidelines](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines)
- Upstream parity: `external/opencode` at the pin — `packages/client/src/promise/generated/client.ts` (`baseUrl` + optional `fetch` + `headers`; merge order at lines 265–269)

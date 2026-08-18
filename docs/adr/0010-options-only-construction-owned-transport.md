# Options-only construction; the SDK owns a singleton-friendly transport

Date: 2026-08-15

`OpenCodeClient(OpenCodeClientOptions)` is the only public way to build a client. The SDK
owns its transport — `SocketsHttpHandler` with `PooledConnectionLifetime` on modern TFMs
(the BCL-documented long-lived-singleton pattern), `ServicePointManager` connection-lease
hardening on net472 (a GA gate under this posture) — and there is no public transport
injection: the `(HttpClient, options)` constructor is internal, IVT-visible to this
repository's tests and benchmarks only. `OpenCode.Sdk.Extensions` registers one singleton
client (sub-clients resolved from the same instance) without `IHttpClientFactory` or a
`Microsoft.Extensions.Http` dependency. Premise: a local-first daemon SDK ships to
production simple — transport extensibility is not built before a concrete consumer need,
and the asymmetry favors omission (re-adding a public constructor is additive; removing
one post-GA is a breaking major). This reverses the same-day Q91 seal on a changed
premise, not on evidence against it: doc 16's grounds for rejecting internalize+IVT
(stock `AddHttpClient<TClient>()` support, the factory-path guard) both dissolve once
neither surface exists, and Q91's anonymous-mode and `BaseAddress` guard machinery
deletes with the doors it defended. Evidence: research doc 16, research log Q90–Q92.

## Consequences

- No consumer composition seam (proxy/TLS/resilience/telemetry handlers) is public today; the
  common proxy case rides the ambient `HttpClient.DefaultProxy`. Adding a seam requires a concrete
  consumer need and a deliberate design. Its absence is an accepted position, not an oversight.
- The mocking constructor is the consumer substitution point for testing.
- The factory-era DI lifetime hazards (#31) resolve by construction — singletons
  end-to-end, one pipeline, no transient-disposable tracking; a roster contract test
  keeps sub-client registrations complete as families grow.
- Reversal trigger: a concrete consumer demand that ambient mechanisms cannot meet — not
  ecosystem parity alone (doc 16 already documents that parity).

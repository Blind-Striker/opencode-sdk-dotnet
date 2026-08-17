# Error model: typed exception spine carrying tagged error data

Date: 2026-08-13

API failures throw through a typed exception spine (`OpenCodeException` →
`OpenCodeApiException`) by default; per-call `NoThrow` returns them on the response spine.
**Streaming operations carry no per-call options at all**: a stream yields an
`IAsyncEnumerable<T>` rather than a response envelope, so there is nothing for `NoThrow` to
answer on, and the only member the per-call options type carries is that choice. Offering
the parameter and refusing it at run time would leave the compiler unable to prevent a
mistake it can see, so the parameter is absent from the streaming surface instead. Reversal
trigger: M6's retry/telemetry/hooks work, if it gives a stream call something real to carry
— adding the parameter then is additive, and cheap while the packages are pre-1.0.
Transport and protocol failures always throw `OpenCodeTransportException`. The spec's tagged
error payloads are generated as typed models under an `OpenCodeError` base and ride **as data**
on either channel, pattern-matchable without string sniffing. There is no client-level switch. Throwing API errors
retain the raw body on `OpenCodeApiException`; `NoThrow` responses retain it on their shared
response spine, including when a typed error cannot be parsed. This
deliberately renders upstream's own idiom — tagged structural domain failures consumed
as values — through .NET's nominal idiom instead; nothing in the taxonomy is lost, and
the per-call `NoThrow` path preserves errors-as-values consumption where a call site
wants it. Do not "fix" this back toward a Result-first design without revisiting the
four mechanisms in the API design spec §4.2: the open error set (upstream regenerates
the spec continually — a closed union can never hold), the single error channel, the
stream plane's structural need to throw, and .NET ecosystem convention.

"Protocol failure" here means status/framing/dispatch failure, malformed JSON, or failure to
materialize the declared non-null .NET response shape. A representable value that merely violates
an OpenAPI constraint is not reclassified as transport failure; the server owns that validation
(ADR-0014). `NoThrow` therefore remains unrelated to duplicate schema checks.

## Considered options

- **Result/DU-style returns** — rejected on the four mechanisms; C# 14 has no unions,
  and if a later C# ships them, Result/Try companions can be added additively (the
  reverse migration would be a breaking major).
- **Upstream-mirroring structural guards** (type guards over exception identity) —
  rejected: exception identity is reliable in .NET; the JS constraint doesn't transfer.
- **Client-level error-behavior switch** — rejected on research doc 11: no
  throw-default SDK ships one (Azure/System.ClientModel/OpenAI are per-call only;
  AWS/gRPC/Octokit/Stripe have no switch; Elastic's client-level switch is
  escalation-only in the opposite direction; FDG: "DO NOT have public members that can
  either throw or not based on some option"). Recorded reversal trigger: an additive
  scoped no-throw sub-view if MCP-server dogfooding demonstrates the need.

Evidence: research doc 02 (upstream error contract), doc 11, API design spec §4;
upstream call-site survey (hey-api `{data, error}` default with generator-shipped
`throwOnError` at per-call and client level; ~76 non-generated opt-in sites in
`packages/app`/CLI; the TUI reads `result.error.data` fields directly). MA0053's
`exceptions_should_be_sealed` option is off by default, so the unsealed exception
hierarchy costs no analyzer arbitration.

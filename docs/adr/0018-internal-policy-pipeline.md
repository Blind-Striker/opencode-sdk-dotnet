# The hand-written runtime is an internal policy pipeline

Date: 2026-08-24

The Behavior core behind every operation is composed as an Azure-style internal policy
pipeline: a sealed `PipelineMessage` carries one operation's trip, an abstract
`PipelinePolicy` processes it through slice-passing `ValueTask ProcessAsync(message,
remaining)`, and the day-one roster is `RequestDecorationPolicy` →
`ResponseBufferingPolicy` → `TransportPolicy`, with a post-pipeline `ResponseMaterializer`
owning decode and adapter dispatch for both planes. The composed class keeps the
`Pipeline` name and the generated-facing entry points, and everything stays `internal` —
ADR-0010's options-only construction stands: no public extensibility, no per-call policy
splicing, no mutation API. Premise: the previous shape concentrated ~13 lifecycle policies
in two orchestration methods — four duplicated failure-classification cascades, three
duplicated body-read sites, no framer or transport seam — so every cross-cutting change
edited the same two methods, and M6's retry/telemetry/hook stages had no named place to
stand; a retry policy now slots into the roster without reshaping either plane. Both peer
runtimes read at source (Azure.Core `470fcf3`, AWS SDK for .NET `3cd03c5`) hide their
runtime mass behind small policy/handler stages, send with response-headers-read, and
classify cancel-versus-timeout by inspecting the caller's token first —
`FailureClassification.Map(exception, phase, token)` centralizes that rule here.
Redirects remain `TransportPolicy`'s refusal: a 3xx is a protocol invariant no operation
can declare, so it is transport's rule, never an operation table's. Evidence: research
log Q126–Q129 and the 2026-08-24 architecture scans recorded there.

## Consequences

- Standing principles sealed with the composition: a seam gets a name (interface or
  abstract class), never a delegate parameter; every `PipelineMessage` member names its
  writing and reading policy (no property bag; pipeline-written members `internal set`);
  every centralized policy module declares its knowledge source (`pin-derived` /
  `BCL-derived` / `upstream-observed`) in its doc comment.
- The stream plane frames through the named `IEventStreamFramer` seam
  (`ServerSentEventFramer` constructs one stateful reader per body), and frame dispatch
  lives beside `IStreamAdapter`, so plane sequencing is testable with a scripted framer
  and dispatch with plain `ServerSentEvent` values — no HTTP involved.
- M6 capabilities (retry, telemetry, hooks) land as new policies in the roster, not as
  new branches inside a plane. Stream retry stays out of scope by canon: a live stream
  is not replayable.
- Companion seals recorded in research log Session 40 land with their own increments and
  canon edits: the generated `StatusVerdict Classify(int)` as the single status
  authority, and the progress-based network timeout on Azure's machinery.
- Reversal triggers: a demand for public pipeline extensibility or per-call policy
  splicing reopens ADR-0010's premise deliberately, never this composition silently; if
  the A6 configuration/transport split triggers (M6 transport handlers or a concrete
  `IHttpClientFactory` need), the transport stage is re-cut first.

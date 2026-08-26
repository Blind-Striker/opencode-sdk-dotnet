# Protocol and Generation Architecture

Date: 2026-08-26

Canonical current rules for the protocol surface, generator, generated models, and runtime
materialization boundary. ADRs record why these decisions were made; dated research records the
evidence and may contain superseded positions.

## Protocol authority and surface

- The sole protocol-semantic input is the pinned `spec/openapi.json`, produced from an exact
  commit of upstream's `packages/protocol/openapi.json` on the active `v2` branch. Snapshot
  production and refresh policy are receipt-governed (ADR-0020); `spec/SNAPSHOT.md` owns the
  exact identity and the current procedure (ADR-0005, ADR-0013, ADR-0020).
- Upstream implementation source is provenance and diagnostic evidence only. It never supplies a
  missing wire type, constraint, format, status, media type, or validation rule (ADR-0013).
- The public SDK covers the v2 protocol surface only. Public identifiers strip the `v2.` operation
  ID prefix; `V2` never appears merely because upstream used that transport prefix (ADR-0005).

## Construction pipeline

- The SDK has a hand-written behavior core. Generated models and operation methods come from the
  repository's own generator (ADR-0003, ADR-0008).
- The pinned `Microsoft.OpenApi` reader owns OpenAPI parsing. The generator owns a minimal,
  fail-closed semantic projection into SpecIR; it does not maintain a second OpenAPI parser
  (ADR-0003).
- Roslyn syntax trees own emission. The generator is repository tooling under `tools/`; output is
  committed under `src/OpenCode.Sdk`, reviewed as source, and regeneration-verified (ADR-0003).
- Generated output passes the analyzer wall on merit. The same tool owns deliberate spec refreshes
  and their generated diffs (ADR-0003).
- Generated files are changed through the generator, never by hand. The output manifest, not a
  folder or filename convention, identifies generator-owned files.

## Curation boundary

Curation may:

- choose .NET names and placement for represented OpenAPI constructs — handle placement
  follows ADR-0019, and every curation row, group rows included, carries its reason;
- collapse OpenAPI shapes proven structurally equivalent;
- fingerprint exclusions already evidenced by the pinned document; and
- map an operation whose upstream identity violates upstream's own conventions onto its intended
  identity through a reason-bearing operation-identity row carrying the upstream report; the row
  retires when upstream's fix makes it stale (ADR-0013).

Curation may not add a wire type, format, constraint, cross-field rule, or runtime validation
absent from the pin. Descriptions generate documentation, not executable semantics. Projection
loss remains faithful or fails closed and is reported upstream rather than repaired from private
implementation knowledge (ADR-0013).

## Generated model shape

- Models are sealed records with `init`-only properties (ADR-0004).
- OpenAPI presence and represented null are independent. A required schema member emits C#
  `required`; an optional property is nullable so omission and explicit JSON null share one absent
  state (ADR-0004, ADR-0014).
- A required or present value uses nullable C# only when the selected representation needs CLR null
  to materialize JSON null. A source-generation-proven in-band null carrier remains non-nullable;
  `JsonElement` currently carries JSON null through `JsonValueKind.Null` (ADR-0004, ADR-0014).
- Optional collections remain nullable. Generated collection properties expose shallow
  `IReadOnlyList<T>` or `IReadOnlyDictionary<string, T>` references without defensive copies,
  read-only wrappers, empty normalization, or recursive child validation. Callers retain ownership
  of supplied collections (ADR-0004, ADR-0014).
- Only literals used to dispatch a union become constants or get-only properties. Other fixed
  values remain ordinary primitives so a representable server value is preserved rather than
  revalidated locally (ADR-0004, ADR-0014).

## Runtime materialization boundary

The runtime validates transport and framing, parses JSON, and performs the checks needed to
materialize the declared .NET shape or dispatch a protocol union. It does not replay server-side
OpenAPI validation for a value already representable in that shape (ADR-0014).

Required members, top-level payload presence, represented token conversion, strict enum parsing,
union dispatch, response-status selection, SSE framing, and source-generated serializer metadata
remain hard walls. Declared no-content responses drain and ignore unexpected bodies; ordinary one-shot JSON
success decoding preserves `HttpContent` charset, BOM, and replacement-decoding behavior while
materializing valid UTF-8 directly (ADR-0014).

The exact standalone `not: {}` applicator is the dialect's never schema; other `not` shapes refuse
at their applicator pointer rather than approximating general JSON Schema negation. A required never
member makes its object branch uninhabitable. Tagged unions preserve such a branch as a known
impossible tag in their bound plan, emit no dead public variant for it, and refuse that tag during
dispatch instead of routing it through ADR-0009's unknown carrier (ADR-0015).

## Version-skew tolerance

- Every tagged union deserializes an unrecognized discriminator into that union's explicit unknown
  carrier, preserving the tag and raw `JsonElement` payload. Unknown frame names are not payload
  variants and remain framing failures (ADR-0009).
- Known tagged-union arms scan a copied UTF-8 reader for the last top-level discriminator and then
  materialize once through source-generated metadata; the scan remains safe when serializer stream
  entry points supply a partial reader, and it does not build a JSON DOM. Unknown arms alone retain
  the DOM needed by their lossless carrier. Public unknown-carrier construction requires the
  payload's discriminator (and any fixed outer marker) to agree with the exposed marker properties;
  serialization remains payload-only replay (ADR-0009).
- A marked union is emitted as an interface. One wire schema remains one sealed record implementing
  every marked union to which it belongs (ADR-0011).
- A token-distinct structural union emits one sealed carrier record with a `Kind`, guarded typed
  accessors and factories, an explicit raw unknown arm for unclaimed non-null value tokens, and a
  source-generated converter. Pinned
  branch order resolves subset overlap; competing branches with the same remaining token kind fail
  binding. Same-primitive refinements collapse without emitting dead branch models (ADR-0016).
- Known objects skip additive unmapped fields, including when the pinned schema is closed. Required
  shape and represented token types remain materializable. Pure dictionaries retain their value
  schema; a named object combined with schema-valued additional properties fails binding until both
  sides can be represented without loss (ADR-0012, ADR-0014).

## Operations, streams, and exclusions

- Every HTTP operation method, including streaming operations, is generated as a short delegation
  into the hand-written behavior core (ADR-0008).
- A cursor-list operation emits an additive `Enumerate*Async` companion only when binding proves the
  admitted `ListRequest` query, `ListCursor` response, item collection, and operation signature.
  Generated response adapters project page items, the opaque next cursor, and the continuation
  request into one hand-written traversal core. Method names, descriptions, and upstream source do
  not independently confer pagination semantics (ADR-0013, ADR-0017).
- The SSE engine remains hand-written runtime behavior; generated stream methods bind their route,
  payload, frame, declared failure event, typed cause, and statuses from the pin. Cause models,
  converters, array metadata, and adapter metadata pass through the same emitted registry and
  System.Text.Json source-generation compile proof as payload models (ADR-0008, ADR-0015).
- Exclusion is reserved for transports the HTTP pipeline cannot carry, such as a WebSocket upgrade.
  Every excluded operation is fingerprint-pinned so protocol drift forces review (ADR-0008).
- Unknown response media types fail generation. Supported non-JSON response bodies follow the
  fail-closed content-type-to-payload mapping recorded by ADR-0008.

## Serialization and Native AOT

System.Text.Json source generation is mandatory. The generator emits the single serializer
registry used by product code; reflection fallback is not a product path. `IsAotCompatible` is
enabled on net10 and later targets where the platform supports that contract (ADR-0003).

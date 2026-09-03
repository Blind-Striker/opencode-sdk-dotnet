# Protocol and Generation Architecture

Date: 2026-09-03

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
- Derived model names strip Effect's encode-side `*Encoded` component suffix unless the unsuffixed
  component itself exists in the document — a mechanical projection-artifact rule beside the `v2.`
  strip, owned by `ProjectionArtifactNamePolicy`, never a per-row curation act (maintainer-sealed
  2026-08-27, research log Q150).

## Snapshot production

- The accepted snapshot is produced by the `refresh-spec` synchronizer (ADR-0020): prepare
  resolves a moving reference once to a full SHA and writes only scratch artifacts with a
  reviewed receipt; verify reproduces the committed identity observationally; apply is a human
  act over one reviewed receipt that refuses time-of-check/time-of-use drift, updates only
  `spec/openapi.json`, `spec/SNAPSHOT.md`, `spec/receipt.json`, `spec/source-watch.json`
  (re-pinned hashes only), and the submodule checkout, and never stages, commits, or pushes.
- With an empty patch list, production is an identity transform over upstream's committed
  artifact; upstream generation is never run merely to copy the document. Restore patches —
  ordered and hash-pinned under `spec/patches/` with a manifest carrying the upstream report,
  touched files, repair predicate, and retirement condition — run through the exact pinned
  upstream generator, and prepare refuses a patch whose repair predicate raw upstream already
  satisfies, forcing an empty-patch retirement refresh.
- The committed receipt records the exact inputs, hashes, patch preimages, operation-set digest,
  and operation delta of the accepted snapshot; `refresh-spec --verify` is its standing check.
- The source watch (`spec/source-watch.json`) pins, by path, SHA-256 and one content anchor, the
  upstream files the hand-written doors read as inputs; it is a refresh-time review trigger only
  and never reaches ingestion, curation, or emission (ADR-0013).

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
- Pending (unselected and not transport-owned) operations are recorded in the committed
  `.generation-incomplete` marker, each carrying the bindability the same selection-path binder
  finds today — `[bindable]`, or `[refused: <verbatim wall messages>]` — so wall-free drift among
  pending operations surfaces as a reviewed diff instead of accumulating silently; the marker also
  lists the fingerprint-pinned transport-owned operations beside them while it exists.

## Curation boundary

Curation may:

- choose .NET names and placement for represented OpenAPI constructs — handle placement
  follows ADR-0019, and every curation row, group rows included, carries its reason;
- declare that a family emits an internal raw layer rather than a public surface, where
  hand-written code owns that family's public doors (ADR-0021);
- collapse OpenAPI shapes proven structurally equivalent;
- fingerprint exclusions already evidenced by the pinned document; and
- map an operation whose upstream identity violates upstream's own conventions onto its intended
  identity through a reason-bearing operation-identity row carrying the upstream report; the row
  retires when upstream's fix makes it stale (ADR-0013).

The structurally-equivalent collapse is mechanical wherever upstream's stabilize suffix names it:
a reachable `<base>_<N>` component folds into `<base>` when the two are structurally identical and
refuses by name when they are not, so a `schemaAliases` row remains only for a duplicate no
convention recognizes.

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
- A string declaring `contentEncoding: base64` materializes as `ReadOnlyMemory<byte>` — a
  represented token conversion the serializer performs natively; any other content encoding fails
  closed (ADR-0014).
- Optional collections remain nullable. Generated collection properties expose shallow
  `IReadOnlyList<T>` or `IReadOnlyDictionary<string, T>` references without defensive copies,
  read-only wrappers, empty normalization, or recursive child validation. Callers retain ownership
  of supplied collections (ADR-0004, ADR-0014).
- Only literals used to dispatch a union become constants or get-only properties. A prefix-tagged
  arm's discriminator is not a literal: it stays a required string property, proven on read to
  carry the prefix. Other fixed values remain ordinary primitives so a representable server value
  is preserved rather than revalidated locally (ADR-0004, ADR-0014).
- An envelope payload is accepted when its ingested schema binds to a supported type plan: named
  models, lists, dictionaries, inline objects promoted under deterministic operation-scoped
  names (reasoned naming rows override), and represented-nullable payloads — distinguished
  from the error path by response state, never by treating CLR null as an unset backing field.
  Location-envelope data may reference an array component or promote an inline object; the
  cursor-list item stays nominal (ADR-0017). Unsupported nodes fail closed, and no
  family-specific shape exception exists. A success body the operation declares inline as an
  object requiring exactly one property that is not `data` is envelope spine too: the payload
  flattens onto the response under that key's PascalCase name and the wrapper is never emitted
  as a model, while a named component with one property keeps its own identity. Requiredness is
  the binder's wall, not the classifier's (final review S6/P4): an inline object whose sole
  non-`data` property is optional still classifies this way but refuses at bind time
  (`EnvelopeFacetBinder.SingleKeyMember`, "must reference an object requiring exactly one
  property") rather than falling back to a promoted bare-body model — fail-closed, and not
  exercised by any operation in the pin today.

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
  carrier, preserving the tag and raw `JsonElement` payload. A discriminator is recognized when it
  equals a declared literal tag or, where the union carries a prefix-tagged arm, starts with that
  arm's prefix; the carrier refuses a tag the prefix claims. Unknown frame names are not payload
  variants and remain framing failures (ADR-0009).
- Known tagged-union arms scan a copied UTF-8 reader for the last top-level discriminator and then
  materialize once through source-generated metadata; the scan remains safe when serializer stream
  entry points supply a partial reader, and it does not build a JSON DOM. Unknown arms alone retain
  the DOM needed by their lossless carrier. Public unknown-carrier construction requires the
  payload's discriminator (and any fixed outer marker) to agree with the exposed marker properties;
  serialization remains payload-only replay (ADR-0009). Dispatch order is fixed: declared literal
  tags, then the single prefix-tagged arm, then the unknown carrier.
- A union whose branches are tagged by more than one wire dialect declares those marker properties
  in a fixed scan order; a payload dispatches on the first of them it carries, one property is
  scanned per attempt with no JSON DOM for known arms, and a payload carrying none of them is a
  protocol failure. Its unknown carrier agrees with a payload when any one declared marker
  property carries the carrier's marker. The error union is the only such union today: `_tag` then
  `name` (ADR-0007, ADR-0009).
- A tagged union may carry at most one prefix-tagged arm: a direct component object branch whose
  discriminator property is a string constrained by exactly Effect's TemplateLiteral projection
  `^<prefix>[\s\S]*?$`. The arm is the branch whose prefix marker sits on the union's
  discriminating property; a prefix marker on any other property — a templated identifier
  beside a literal tag — is inert, and that branch stays a literal variant. The literal-tagged
  branches fix the discriminator, so a marked union needs at least one; a union of prefix arms
  alone refuses as sharing no discriminating marker property. No literal tag of the union may
  start with the prefix, and the arm may not sit inside a nested union, join a multi-dialect
  union, or be uninhabited; every other shape refuses by name. The live event union's `rpc.` arm
  is the only such arm today.
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
- An internal-raw family emits its clients sealed and internal — internal operation methods and
  handle factory, no mocking constructor — under raw type names, while the root client keeps the
  public family accessor its hand-written door answers. Only such a family may carry a
  document-declared request header: it emits as an optional trailing method parameter and travels
  the declared-header channel into the pipeline. The header value stays a caller concern and never
  enters curation or generated code, and a header-bearing operation forgoes the cursor-enumeration
  companion (ADR-0013, ADR-0021).
- A cursor-list operation emits an additive `Enumerate*Async` companion only when binding proves the
  admitted `ListRequest` query, `ListCursor` response, item collection, and operation signature.
  Generated response adapters project page items, the opaque next cursor, and the continuation
  request into one hand-written traversal core. Method names, descriptions, and upstream source do
  not independently confer pagination semantics (ADR-0013, ADR-0017).
- A query parameter binds by shape: a required parameter becomes a `required`, non-nullable
  request property and makes the request itself a required method and route argument; an optional
  parameter binds nullable and is omitted from the wire when unset, whether or not its schema
  admits JSON null; a string enum outside the spine's own value sets becomes a generated C# enum
  whose wire spelling the route builder writes through a generated switch; every other query
  shape refuses by name.
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

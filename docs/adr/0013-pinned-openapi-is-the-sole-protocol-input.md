# Pinned OpenAPI is the sole protocol-semantic input

Date: 2026-08-17

The generator derives wire types, constraints, statuses, schemas, media types, and protocol
extensions exclusively from the pinned `spec/openapi.json`. Upstream TypeScript and Effect schema
source is provenance and diagnostic evidence, never a generation input: consuming it would create
a second contract, require executing or reimplementing upstream's schema runtime, and make an
internal refactor look like HTTP drift. A construct the OpenAPI projection cannot represent stays
faithful to the document or fails closed; it is not silently repaired from implementation source.

Curation may choose .NET names and placement, collapse OpenAPI shapes proven equivalent, and
fingerprint exclusions already evidenced by the document. It may not add a wire type, format,
range, cross-field rule, or runtime validation absent from the pinned artifact. Descriptions remain
documentation, not executable constraints. A projection discrepancy is rechecked against current
upstream, recorded as research, and reported upstream rather than becoming a hidden local override.

## Consequences

- Naming, grouping, envelope-property naming, validated aliases, and operation exclusions remain
  legitimate curation because they organize the represented contract without enriching it.
- Property/parameter type overrides, name-derived semantics, and behavior-premised validation are
  outside curation. Existing rows and heuristics in those categories must be removed or replaced by
  faithful OpenAPI representation.
- The generated .NET surface can be less ergonomic than upstream's first-party client when
  upstream's OpenAPI projection loses a decode transform. Deterministic single-source fidelity
  wins over a nicer API maintained from private implementation knowledge.

## Reversal Trigger

Reconsider only if upstream publishes a richer versioned machine-readable contract, or this
project deliberately adopts a second pinned, reproducible protocol artifact through a new ADR.

Evidence: research log Q107–Q108.

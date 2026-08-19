# Typed stream failures preserve inhabitable causes and refuse impossible known tags

Date: 2026-08-19

The pinned `x-effect-stream` contract specializes Effect's generic cause as
`Cause<Never, Defect>`: `not: {}` is the JSON Schema never shape, so the required
`Fail.error` member makes the declared `Fail` branch uninhabitable while `Die` and `Interrupt`
remain valid. The generator preserves that evidence through ingestion and binding, emits only
inhabitable public cause variants, and records the impossible tag in the generated converter so a
`Fail` payload is a protocol failure rather than an ADR-0009 unknown variant. A schema-valid
failure frame throws `OpenCodeStreamFailureException`, a transport-exception subtype carrying the
typed cause through source-generated JSON metadata; malformed, null, or impossible causes remain
plain `OpenCodeTransportException` failures.

## Considered Options

- Emitting a public `Fail` variant with a never-valued property was rejected because C# has no
  bottom type and the resulting model would be dead or constructibly dishonest.
- Routing `Fail` through the unknown carrier was rejected because the tag is declared and
  schema-invalid, not evidence of a newer server variant.
- Adding a nullable cause to every `OpenCodeTransportException` was rejected in favor of a subtype
  that separates valid declared stream failure data from malformed transport/protocol input.

## Consequences

Hand-written machinery branches only on semantic schema nodes and bound plan data. It contains no
operation-ID, generated-type-name, or literal-tag condition; the pin-derived `Fail` tag appears only
in generated converter output. A future pin with a different typed stream-error shape must fail
the current profile closed and receive its own bounded design rather than reuse this specialization
silently.

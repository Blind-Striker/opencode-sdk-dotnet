# Unknown-variant tolerance: every union deserializes unknowns into an explicit carrier

Date: 2026-08-09

A consumer's SDK being older than the server it talks to is normal operation (upstream
ships hourly betas), so an unknown discriminator value must never kill a stream or a
call and must never be silently dropped: every generated union deserializes
unrecognized tags into that union's explicit `Unknown*` variant carrying the tag string
and the raw payload (`JsonElement`). One mechanical generator rule, no curation; it
applies to events, error unions, and every other tagged union alike. Mechanism: a
generator-emitted custom converter per union base — System.Text.Json's
`UnknownDerivedTypeHandling` is serialization-side only and cannot express
deserialization fallback (codegen spike: unknown discriminator throws, research doc
08) — buffering the element, reading the tag position-independently, and dispatching
through the source-generated context so the AOT commitment holds. This resolves the
forward-compatibility question the codegen spike parked; the tolerance is a recorded
runtime exception to the fail-closed default (API design spec §2.1/§14).

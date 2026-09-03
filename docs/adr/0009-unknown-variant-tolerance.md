# Unknown-variant tolerance: every union deserializes unknowns into an explicit carrier

Date: 2026-08-09

A consumer's SDK being older than the server it talks to is normal operation (upstream
ships hourly betas), so an unknown discriminator value must never kill a stream or a
call and must never be silently dropped: every generated union deserializes tags that are
neither declared literals nor claimed by the union's prefix-tagged arm into that union's
explicit `Unknown*` variant carrying the tag string and the raw payload (`JsonElement`). One
mechanical generator rule, no curation; it
applies to events, error unions, and every other tagged union alike. Mechanism: a
generator-emitted custom converter per union base — System.Text.Json's
`UnknownDerivedTypeHandling` is serialization-side only and cannot express
deserialization fallback (codegen spike: unknown discriminator throws, research doc
08) — scanning a copied reader for the tag and dispatching known values directly through
the source-generated context so the AOT commitment holds. Only an unknown value buffers
the element needed by its raw carrier. This resolves the
forward-compatibility question the codegen spike parked; the tolerance is a recorded
runtime exception to the fail-closed default (API design spec §2.1/§14).

The tolerance covers **payload** discriminators, not framing. An event stream's frame name
is the channel that says whether a frame is a payload at all, so an unrecognized name is
refused rather than carried: tolerating it would route a frame of unknown meaning into a
payload carrier and report a server-side stream failure as an ordinary event. The unknown
this ADR protects is a new variant inside a frame whose kind is understood.

Known variants may scan a copied reader for position-independent dispatch and deserialize the
original reader directly; only an actually unknown variant pays for the retained `JsonElement`.
Because an unknown carrier writes that payload verbatim rather than synthesizing markers, its public
constructor refuses a missing or disagreeing discriminator, a disagreeing fixed outer marker, and a
discriminator the union's prefix-tagged arm claims.
Wire-read carriers already derive their marker from the same payload and remain unchanged.

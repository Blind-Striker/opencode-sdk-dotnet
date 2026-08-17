# Known objects tolerate additive unmapped fields

Date: 2026-08-17

Generated readers deliberately skip unmapped fields on a known object even when the pinned
schema declares `additionalProperties: false`: runtime version skew is normal, and an additive
optional server field must not terminate an older client's call or event stream. This tolerance
is field-only — required members, fixed literals, represented types, and nullability remain
strict; pure dictionaries keep their value schema, while an object combining named properties
with schema-valued additional properties fails binding until both sides can be represented
without loss. This is the known-object counterpart to ADR-0009's unknown-variant tolerance, not
a general relaxation of fail-closed framing or schema projection.

Evidence: research log Q103.

## Reversal trigger

Reconsider strict unmapped-member rejection only if server and SDK versions become lockstep, or
wire/security evidence shows that ignoring an additive field is unsafe for a specific contract.

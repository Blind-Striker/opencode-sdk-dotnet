# Known objects tolerate additive unmapped fields

Date: 2026-08-17

Generated readers deliberately skip unmapped fields on a known object even when the pinned
schema declares `additionalProperties: false`: runtime version skew is normal, and an additive
optional server field must not terminate an older client's call or event stream. This tolerance
is field-only: required shape, token conversion, and union dispatch remain materializable, while
representable fixed/null values are not independently revalidated (ADR-0014). Pure dictionaries
keep their value schema, while an object combining named properties with schema-valued additional
properties fails binding until both sides can be represented without loss. This is the
known-object counterpart to ADR-0009's unknown-variant tolerance, not a relaxation of build-time
OpenAPI projection walls (ADR-0013).

Evidence: research log Q103.

## Reversal trigger

Reconsider strict unmapped-member rejection only if server and SDK versions become lockstep, or
wire/security evidence shows that ignoring an additive field is unsafe for a specific contract.

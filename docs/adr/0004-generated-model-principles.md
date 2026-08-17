# Generated models: init-only, required-mirroring, schema-shaped nullability

Date: 2026-08-17

Generated models are sealed records with `init`-only properties. Schema presence and represented
nullability are independent: a required member emits the C# `required` modifier; a property emits
nullable C# when it is optional or its schema admits null. Optional omission and explicit null
collapse to that one nullable state. Required-nullable properties remain `required T?`, accept an
explicit null, and write it back rather than omitting the member.

Collections follow the same outer-property rule. Optional lists/dictionaries are nullable;
required collections remain required; item/value schemas shape nested nullable annotations. The
public surface uses `IReadOnlyList<T>` / `IReadOnlyDictionary<string, T>` without defensive copies,
read-only wrappers, empty normalization, or recursive item/value validation. `IReadOnly*` is an API
view, not a deep-immutability guarantee; caller-supplied collection ownership stays with the caller
(ADR-0014).

A literal used for union dispatch emits as a constant/get-only property because successful
dispatch already proved it. Other boolean, numeric, and string literals remain ordinary primitive
properties so the SDK preserves the wire value instead of validating or silently normalizing a
representable server contradiction.

Evidence for the model/nullability decision: research log Q106–Q108.

## Generator policy

- **`Uri` only for OpenAPI `format: uri`;** a property name or upstream implementation source
  cannot supply a missing format through curation (ADR-0013). Filesystem paths stay `string`.
- **Acronyms use ordinary PascalCase regardless of length** (`Id`, `Io`, `Ip`, `Url`,
  `Api`, `Pty`, `Mcp`, `Tui`); brand spellings use curated exceptions (`OAuth`).
- **Identifier mapping is mechanical** — every wire name maps to PascalCase with
  `[JsonPropertyName]` carrying wire fidelity (`_tag`, snake_case, dotted schema
  names).
- **`WhenWritingNull` only on optional properties;** required-nullable members must retain an
  explicit JSON null. Optional schema-non-null and schema-nullable values intentionally share the
  same C# representation.
- **Guard emission** — every generated method opens with BCL throw-helper guards;
  CA1062 at `error` guards the contract; invariants otherwise live in the type system
  (NRT, `required`, init-only assignment, guarded getters).
- **Fixed-literal emission** — union discriminators are constants; non-discriminator literals
  remain ordinary primitive properties with no duplicate server-schema validation.
- **XML documentation emission** — generated docs come from spec
  `summary`/`description`; operation methods emit `<exception cref>` lists from
  declared error responses; CS1591 becomes `error` (hand-written surface documented by
  hand).

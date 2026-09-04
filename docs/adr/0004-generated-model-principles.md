# Generated models: init-only, required-mirroring, schema-shaped null representation

Date: 2026-08-18

Generated models are sealed records with `init`-only properties. Schema presence and represented
null are independent: a required member emits the C# `required` modifier; an optional property emits
nullable C# so omission and explicit null collapse to one absent state. A required property whose
selected representation uses CLR null emits `required T?` when its schema admits null, accepts an
explicit null, and writes it back rather than omitting the member. When a representation carries
JSON null in-band, its canonical non-null CLR state represents wire null instead of `Nullable<T>`.
`JsonElement` is the current carrier through `JsonValueKind.Null`.

Collections follow the same outer-property rule. Optional lists/dictionaries are nullable;
required collections remain required. Present list slots and dictionary entries have no omission
state, so their item/value types use nullable C# only when the selected representation requires CLR
null. A source-generation-proven in-band JSON-null carrier remains non-nullable. The public surface
uses `IReadOnlyList<T>` / `IReadOnlyDictionary<string, T>` without defensive copies, read-only
wrappers, empty normalization, or recursive item/value validation. `IReadOnly*` is an API view, not
a deep-immutability guarantee; caller-supplied collection ownership stays with the caller
(ADR-0014).

A literal used for union dispatch emits as a constant/get-only property because successful
dispatch already proved it; a prefix-tagged discriminator emits as a required string because
dispatch proved only its prefix. Constructing the arm directly with a value outside its prefix, or
deserializing such a value through the arm's own context rather than the union, surfaces that guard
as an `ArgumentException`. Other boolean, numeric, and string literals remain ordinary primitive
properties so the SDK preserves the wire value instead of validating or silently normalizing a
representable server contradiction.

Evidence for the model/nullability decision: research log Q106–Q109.

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
- **In-band JSON-null carriers stay non-nullable;** admission requires source-generated
  serialization evidence that JSON null materializes as a canonical non-null CLR state and writes
  back as JSON null in every supported runtime context. This is a representation capability, never
  an endpoint/property curation rule.
- **Guard emission** — every generated method opens with BCL throw-helper guards;
  CA1062 at `error` guards the contract; invariants otherwise live in the type system
  (NRT, `required`, init-only assignment, guarded getters).
- **Fixed-literal emission** — union discriminators are constants; non-discriminator literals
  remain ordinary primitive properties with no duplicate server-schema validation.
- **XML documentation emission** — generated docs come from spec
  `summary`/`description`; operation methods emit `<exception cref>` lists from
  declared error responses; CS1591 becomes `error` (hand-written surface documented by
  hand).

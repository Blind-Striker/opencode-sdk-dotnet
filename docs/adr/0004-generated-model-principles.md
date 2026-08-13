# Generated models: immutable, required, empty-not-null

Date: 2026-08-13

Generated models are immutable by default (records, `init`-only properties, read-only
collections) and mirror the spec's `required` with the C# `required` modifier. Nullability is
a last resort: absent optional collections deserialize to empty instead of null, while an
explicit wire `null` is rejected unless the schema admits null; nullable annotations appear
only where null carries meaning in the contract. This trades missing-vs-empty fidelity where
the distinction carries no meaning for consumer ergonomics, thread-safety, and AOT-friendly
serialization without weakening the schema's null contract. These principles are generator
policy: they apply mechanically to every emitted type.

## Generator policy

- **`Uri` for URL-semantic properties** (spec `format: uri` or curation-marked);
  filesystem paths stay `string`. CA1056/CA1054 firing on a generated `*Url` string
  property is the fail-closed detector; a per-property fallback to `string` is the
  recorded escape for version-skew malformed URLs.
- **Acronyms use ordinary PascalCase regardless of length** (`Id`, `Io`, `Ip`, `Url`,
  `Api`, `Pty`, `Mcp`, `Tui`); brand spellings use curated exceptions (`OAuth`).
- **Identifier mapping is mechanical** — every wire name maps to PascalCase with
  `[JsonPropertyName]` carrying wire fidelity (`_tag`, snake_case, dotted schema
  names).
- **`WhenWritingNull`** on nullable properties; the spec's `anyOf`-null fields where
  null carries meaning are curatable to explicit-null per property; an unmapped
  `anyOf`-null fails generation.
- **Guard emission** — every generated method opens with BCL throw-helper guards;
  CA1062 at `error` guards the contract; invariants otherwise live in the type system
  (NRT, `required`, immutability, guarded getters).
- **XML documentation emission** — generated docs come from spec
  `summary`/`description`; operation methods emit `<exception cref>` lists from
  declared error responses; CS1591 becomes `error` (hand-written surface documented by
  hand).

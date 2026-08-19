# Token-distinct structural unions use generated carriers

Date: 2026-08-19

An untagged structural union whose branches are distinguishable by JSON token kind emits one sealed
record carrier plus a `Kind` enum, guarded typed accessors, factories, and a custom converter. The
converter follows pinned branch order, dispatches through source-generated metadata, preserves an
unclaimed valid non-null value token as the carrier's raw `Unknown` arm, and refuses malformed content after
a known token selects an arm. Branches competing for the same token kind fail binding rather than
speculatively parsing. This is separate from ADR-0011: marked object schemas implement membership
interfaces, while values such as `string | number | boolean | string[]` require a carrier because CLR
primitives and collections cannot implement a generated interface.

For overlapping `string | special-number`, the earlier broad string branch owns the named
`"NaN"`, `"Infinity"`, and `"-Infinity"` spellings; the remaining number arm admits ordinary JSON
numbers. A non-finite `double` constructed through that number arm cannot write a JSON number and is
refused by `Utf8JsonWriter`; callers use the text arm for those exact wire spellings. Collapsed
same-primitive refinements produce no carrier and no dead branch models.

## Considered options

- Plain `JsonElement` was smaller but discarded deterministic typed branches the pin exposes.
- One wrapper record per arm made pattern matching direct but multiplied public types for every
  primitive, collection, and future `Model | Model[]` union.
- Reusing the marked-union interface shape was impossible without synthetic wrappers because
  `string`, `double`, and `IReadOnlyList<T>` cannot implement an SDK interface.

The decision was compile- and round-trip-probed with reflection fallback disabled. Reconsider if C#
gains a package-compatible native union representation or if a selected pin first requires two
untagged object branches that cannot be distinguished by token kind.

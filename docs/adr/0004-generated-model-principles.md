# Generated models: immutable, required, empty-not-null

Date: 2026-08-08

Generated models are immutable by default (records, `init`-only properties, read-only
collections) and mirror the spec's `required` with the C# `required` modifier. Nullability is
a last resort: absent collections deserialize to empty instead of null, and nullable
annotations appear only where absence carries meaning in the contract. This trades wire-shape
fidelity (null vs missing vs empty collapses where the distinction carries no meaning) for
consumer ergonomics, thread-safety, and AOT-friendly serialization — a deliberate deviation a
reader might otherwise "fix" back toward spec-literal emission. These principles are generator
policy: they apply mechanically to every emitted type.

# Model layer: own generator, Roslyn emission, packaged as repo tooling

Date: 2026-08-08

The generated model layer comes from our own generator — not Kiota, NSwag, or OpenAPI
Generator. All three were run against the real pinned spec under the repo's strict-analyzer
regime and fail structurally: the spec's union dialect (discriminator-free `anyOf` plus
required single-value literal markers) is invisible to their `discriminator`-keyed union
handling, and none can emit the locked single source-generated `JsonSerializerContext`
registry. System.Text.Json's name-based polymorphism matches the dialect exactly. Full verdict
matrix and construct counts: `docs/research/08-codegen-spike.md`.

## Emission: Roslyn syntax trees — decided against the slice's cost evidence

The spike implemented the same slice twice behind a shared parser/IR. Template/string emission
measured cheaper at slice scale: no dependency, ~4× faster, exact formatting control — and the
Roslyn variant still pushes doc-comment/directive trivia through parsed strings. The maintainer
chose Roslyn syntax trees anyway: at full-generator scale (request/response wrappers,
converters, the serializer registry, possibly Result-shaped methods) type-safe semantic
construction and refactorability across many emitted shapes are judged to dominate maintenance
cost. The measured costs are accepted with mitigations: a `dotnet format` post-step owns
formatting; trivia is emitted as parsed strings (standard practice).

Reversal framing: the parser/IR boundary contains the choice — both spike emitters sat behind
the same IR — so if Roslyn emission proves a net burden, the emitter half is swapped without
touching spec parsing, union analysis, or surface filtering.

## Packaging: repo tooling under `tools/`

The emission engine is a library behind a thin file-based `.cs` entry, bound to the repo build
rules; output is committed into the SDK project and CI regen-verifies; the same tool owns spec
refresh (submodule pin bump, `spec/` copy, `SNAPSHOT.md` stamp). The Roslyn
incremental-source-generator shape is structurally blocked, not merely costed: Roslyn
generators never see each other's output, so a compile-time-emitted `[JsonSerializable]`
registry would be invisible to the System.Text.Json source generator and the AOT commitment
would silently degrade to reflection. Reversal triggers: Roslyn ships generator chaining; the
spec becomes a live per-commit input; the generator becomes a shipped product for third-party
specs.

## Consequences

- **Generated code passes the analyzer wall on merit.** No blanket generated-code exemption:
  the model layer is most of the shipped public surface, and every diagnostic family found by
  the spike's on-merit probe is mechanically fixable in our own emitter — the capability that
  helped own-generator win. Rules that genuinely cannot apply go through the existing per-rule
  arbitration pattern. Accepted cost: the emitter tracks the analyzer wall permanently — a new
  rule firing on generated code forces an emitter fix or a recorded arbitration.
- File naming and exemption-disabling mechanics (`.g.cs` with `generated_code=false` vs plain
  `.cs`, the fate of per-file `#nullable` directives) are settled at generator build-out
  (ROADMAP).

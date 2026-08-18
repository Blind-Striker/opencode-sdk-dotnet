# Engineering Quality Gates

Date: 2026-08-18

Canonical quality policy for product code, generated output, repository tooling, tests, analyzers,
performance evidence, and completion claims.

## Assurance posture

The repository is defensive and fail-closed by default. Guard public preconditions and local
representability, assert internal invariants, and fail loudly rather than guess. Do not turn that
posture into a second validator for server-owned OpenAPI constraints; the runtime boundary is
canonical in `../architecture/protocol-and-generation.md` and ADR-0014.

Assurance intensity follows blast radius:

1. Shipped SDK runtime receives the strongest transport, materialization, multi-TFM, and behavioral
   evidence.
2. Committed generated output is protected by reviewed diffs, the analyzer wall, regeneration,
   public-API locks, and contract tests.
3. Repository tooling internals receive targeted evidence for their actual consumers and known
   lossy seams. Every additional mechanism must name a consumer or a concrete failure it prevents.

The testing posture is deliberately strict: observation-based gates, deterministic tests, fakes
only for published contracts, TUnit on Microsoft.Testing.Platform, real-process integration where
the process boundary is the product, and three-OS launcher acceptance.

## Analyzer policy

The analyzer wall is fail-closed and final. Do not relitigate or broadly weaken it to make a change
pass. Redundant rules are deliberate where their coverage differs or supplies defense in depth.
When a rule misfires on valid code, use a narrowly scoped per-rule arbitration comment that names
the winning rule or contract; never roll back the policy globally. `.editorconfig` contains the
established pattern.

- `LangVersion=14.0` and `AnalysisLevel=10.0` are deliberate numeric pins. Never replace either
  with `latest`.
- C# 14 on net472 is unsupported by the platform documentation but deliberate repository practice,
  enabled by source polyfills. Future language changes move the numeric pin intentionally.
- `GenerateDocumentationFile=true` is load-bearing because CLI builds do not report IDE0005
  consistently without it. Keep the adjacent guard comment in `Directory.Build.props`.
- Analyzer package currency is manual. Since SDK 9, the build does not warn reliably when the
  pinned NetAnalyzers package falls behind the SDK; deliberate analyzer-package updates own that
  comparison explicitly.
- Product code uses `ConfigureAwait(false)`, triple-enforced by CA2007, MA0004 in `Always` mode,
  and VSTHRD111. Tests are exempt from all three rules.

Research doc 07 records the claim verification, community comparison, and rule arbitration behind
this policy.

## Completion gate

"Builds" and "works" are different claims. Before reporting a repository change complete, run the
base gate from the repository root; a current handoff may add scope-specific checks but cannot
weaken it:

```bash
dotnet tool run slopwatch analyze --exclude ".scratchpad/**,external/**" --fail-on warning
dotnet build --configuration Release
dotnet format --verify-no-changes --no-restore
dotnet test --configuration Release --no-build
```

Changes to the protocol, generator, curation, or generated output also run:

```bash
dotnet run --file tools/opencode-tool.cs -- --help
dotnet run --file tools/opencode-tool.cs -- generate --verify
```

A current development handoff may require the Unix direct-invocation smoke
`./tools/opencode-tool.cs --help` in addition to the cross-platform form above.

Slopwatch excludes local scratch work and checked-out upstream submodules because neither is
repository-authored product surface. A failed, skipped, weakened, or unrun gate must be reported
honestly.

## Performance

Performance is a standing concern weighted by artifact. Shipped hot paths target speed and zero
avoidable allocation; tooling also avoids obvious redundant parsing, copying, buffering, and
enumeration. Do not speculate: performance claims are settled by benchmarks in
`tests/OpenCode.Sdk.Performance.Tests` with `MemoryDiagnoser` enabled.

Performance changes carry exact before/after evidence. Intentional contract costs, such as raw body
retention on API errors, are not disguised as optimization opportunities.

# Engineering Quality Gates

Date: 2026-08-20

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

The analyzer wall is fail-closed and final. Build owns compiler, SDK CA, third-party analyzer,
source-generator, and build-enforceable IDE diagnostics; the warning-level style formatter owns
Roslyn rules classified `EnforceOnBuild.Never` plus deterministic import organization. Do not
relitigate or broadly weaken either side to make a change pass. Redundant rules are deliberate where
their coverage differs or supplies defense in depth. When a rule misfires on valid code, use a
narrowly scoped per-rule arbitration comment that names the winning rule or contract; never roll
back the policy globally. `.editorconfig` contains the established pattern.

- `LangVersion=14.0` and `AnalysisLevel=10.0` are deliberate numeric pins. Never replace either
  with `latest`.
- C# 14 on net472 is unsupported by the platform documentation but deliberate repository practice,
  enabled by source polyfills. Future language changes move the numeric pin intentionally.
- `GenerateDocumentationFile=true` is load-bearing because CLI builds do not report IDE0005
  consistently without it. Keep the adjacent guard comment in `Directory.Build.props`.
- Analyzer package currency is manual. Since SDK 9, the build does not warn reliably when the
  pinned NetAnalyzers package falls behind the SDK; deliberate analyzer-package updates own that
  comparison explicitly.
- Repository-wide analyzer packages inherit into every project, and global warning escalation has
  no `WarningsNotAsErrors` escape. The few project `NoWarn` rows apply equally to build and format,
  so build mechanically owns enabled warning/error diagnostics from every third-party analyzer.
- Product code uses `ConfigureAwait(false)`, triple-enforced by CA2007, MA0004 in `Always` mode,
  and VSTHRD111. Tests are exempt from all three rules.
- `CA1502` attributes local functions declared in top-level statements to the compiler-generated
  `Main`, so a top-level `Program.cs` accumulates every mode's dispatch complexity onto one
  method. Split the dispatch into a named class instead of arbitrating the rule;
  `tests/OpenCode.Sdk.Sandbox/SandboxRunner.cs` records the precedent.

Research doc 07 records the claim verification, community comparison, and rule arbitration behind
this policy.

## Completion gate

"Builds" and "works" are different claims. Before reporting a repository change complete, run the
base gate from the repository root; a current handoff may add scope-specific checks but cannot
weaken it:

```bash
dotnet tool run slopwatch analyze --exclude ".scratchpad/**,external/**" --fail-on warning
dotnet build --configuration Release
dotnet format whitespace --verify-no-changes --no-restore
dotnet format style --verify-no-changes --no-restore --severity warn
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

The build is the semantic analyzer wall: compiler diagnostics, build-enforceable IDE rules, SDK CA
rules, third-party analyzers, and source-generator diagnostics run there for every target
compilation. The whitespace pass separately retains UTF-8, LF, final-newline, trailing-whitespace,
and syntax-whitespace enforcement. The warning-level style pass retains deterministic import
organization and every configured Roslyn style error, including rules classified
`EnforceOnBuild.Never`, without rerunning the SDK CA and third-party analyzer set. It deliberately
uses no diagnostic allow-list so future warning/error style rules remain covered automatically.
Repository generation is a separate mutating path: after writing generated source, it deliberately
runs project-scoped full format over only generator-owned paths. That canonicalization step is not
the solution-wide CI lint gate this split optimizes.

## Performance

Performance is a standing concern weighted by artifact. Shipped hot paths target speed and zero
avoidable allocation; tooling also avoids obvious redundant parsing, copying, buffering, and
enumeration. Do not speculate: performance claims are settled by benchmarks in
`tests/OpenCode.Sdk.Performance.Tests` with `MemoryDiagnoser` enabled.

Performance changes carry exact before/after evidence. Intentional contract costs, such as raw body
retention on API errors, are not disguised as optimization opportunities.

The permanent suite reports exact allocated bytes (not rounded KiB) beside each case's wire bytes,
item count, payload bytes per item, allocated bytes per item, and allocation amplification
(`allocated / wire`), and exports the full BenchmarkDotNet JSON; a claim quotes those columns, not a
rounded `KB`. Each benchmark class owns one operation family and decomposes it as a component ladder
(complete operation, pipeline without materialization, generated adapter, source-generated
materialization) over wire-shaped fixtures whose sizes scale from the common case to very large, so
a regression attributes to a component rather than merely registering end to end. Every
`GlobalSetup` refuses a fixture that does not materialize the generated type it claims to measure.
Benchmark cadence follows scope. An increment-level check is targeted and cheap: filter to the
component ladders the change touches and run `--job short` (`--job Dry` first when fixtures or
benchmark code changed); its exact allocation columns are the before/after comparison, and its
timings are indicative only — never quoted as evidence. The full suite under the default job runs
as milestone evidence when an arc of work completes, from a clean copy outside the repository
(BenchmarkDotNet locates the project by name from the solution root and refuses duplicate copies
such as scratch clones). Whatever the tier, before/after runs stay within one environment, and
allocation is the primary comparison. The performance project adds a net472 target on Windows only;
net472 numbers come from the `--runtimes` net472 leg of the canonical invocation below, never from
net10 or source-equivalent probes.

The canonical comparable run, and the comparison it feeds:

```bash
dotnet run --project tests/OpenCode.Sdk.Performance.Tests -c Release -f net10.0 -- --filter '*' --runtimes net10.0 net472 --artifacts .benchmarks/<run-name>
dotnet run --file tools/opencode-tool.cs -- compare-benchmarks .benchmarks/<before-run> .benchmarks/<after-run> --output .benchmarks/<comparison>.csv
```

`--runtimes` is load-bearing: it labels each case's leg with the runtime name (`.NET 10.0`,
`.NET Framework 4.7.2`) in the exports, and `compare-benchmarks` joins the two runs on exactly
(case, runtime). A run launched without it is labelled by its job name (`DefaultJob`) instead and
joins nothing against the archived runs — the comparison then fails naming both label sets, but
only after the run's hours are already spent. An increment-level check narrows `--filter` and adds
`--job short`. `--artifacts` names the run's folder in the git-ignored `.benchmarks/` store;
`compare-benchmarks` reads each folder's `results/*-report-full.json`, so those exports are the
part of a run that must be preserved.

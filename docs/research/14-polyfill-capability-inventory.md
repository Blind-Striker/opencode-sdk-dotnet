# Polyfill capability inventory

Date: 2026-08-12

> Dated evidence and decision history, not current policy. Follow current canon through
> `AGENTS.md`.

This inventory answers only what the pinned Polyfill package contributes to this repository,
which parts are actually enabled, and what that means for M1's minimal HTTP transport. It is
not a general catalogue of every API the package can emit.

## Q1: What package is wired into the repository, and how does it reach product code?

### Finding

`Directory.Packages.props` pins Polyfill `11.0.2`. `Directory.Build.props` references it for
every project and every target framework with `PrivateAssets=all`; the product matrix remains
`netstandard2.0;net472;net8.0;net9.0;net10.0`.

Polyfill is a source-only development dependency. Its NuGet package contains target-specific
C# files under `contentFiles/cs/<TFM>/` plus `build/Polyfill.targets`; NuGet adds those files as
hidden `Compile` items with `Pack=false`, and the target removes disabled optional groups. No
Polyfill runtime assembly is loaded and no Polyfill package dependency flows to an SDK
consumer. The selected source is compiled into each `OpenCode.Sdk` target instead.

A restored-project query after optional-group removal reported these Polyfill compile-item
counts for `OpenCode.Sdk`:

| Target | Polyfill source items |
|---|---:|
| `netstandard2.0` | 257 |
| `net472` | 256 |
| `net8.0` | 85 |
| `net9.0` | 53 |
| `net10.0` | 36 |

The counts describe pinned package source inputs, not public SDK APIs and not an evergreen
test assertion. A source item may still contain target- or reference-dependent `#if` blocks.

### Decision

Keep the package repository-wide and private. Treat its selected source as part of each built
SDK assembly, not as a runtime dependency or a substitute for multi-TFM verification.

## Q2: Which optional capabilities are actually enabled?

### Finding

The repository sets only `PolyArgumentExceptions=true` globally. The package target makes the
other optional groups opt-in by removing their source when the corresponding property is not
`true`.

| Capability | Repository setting | Effective state |
|---|---|---|
| Core target-specific polyfills | Always selected by the package | Enabled |
| `PolyArgumentExceptions` | `true` | Enabled where the target lacks a BCL overload |
| `PolyEnsure` | Unset | Disabled |
| `PolyGuard` | Unset | Disabled |
| `PolyNullability` | Unset | Disabled |
| `PolyStringInterpolation` | Unset | Disabled |
| `PolyPublic` | Unset | Polyfill types remain internal |
| `PolyUseEmbeddedAttribute` | `true` in the SDK and tooling projects | Both declare friend test assemblies that compile their own Polyfill copy; embedded types stay hidden from them |
| `AllowUnsafeBlocks` | Unset | Unsafe-only overload bodies are not enabled |

`tests/Directory.Build.props` also disables TUnit's separate polyfill injection because the
repository-wide reference already supplies the needed sources. This prevents duplicate package
items; it does not disable the root Polyfill reference.

On `netstandard2.0` and `net472`, the enabled argument-exception group supplies modern throw
helpers for `ArgumentException`, `ArgumentNullException`, `ArgumentOutOfRangeException`, and
`ObjectDisposedException`. On `net8.0`, the package contains only the unsafe pointer form of
`ArgumentNullException.ThrowIfNull`, whose body is excluded here because unsafe blocks are off.
The normal modern targets use their BCL implementations.

### Decision

Use BCL throw-helper syntax uniformly in hand-written Arc B code. Do not enable another
optional Polyfill group without a concrete product-code consumer and a multi-TFM build proving
that the added source is necessary.

## Q3: Which language and runtime gaps relevant to the SDK are covered per target?

### Finding

Polyfill contributes two different kinds of compatibility:

1. Compiler-recognized metadata types make modern C# source legal on older reference
   assemblies. The downlevel sets include `IsExternalInit`, `RequiredMemberAttribute`,
   `CompilerFeatureRequiredAttribute`, and `SetsRequiredMembersAttribute`, enabling records,
   `init`, and `required` metadata on `netstandard2.0` and `net472`.
2. Executable extension/helper code implements newer BCL-shaped APIs over older primitives.
   Examples relevant to transport include `Task.WaitAsync`, cancellation-token overloads on
   `HttpContent`, and memory-based stream overloads.

The first category does not install a newer CLR or emulate new runtime semantics. It supplies
the metadata names expected by the compiler. The second category is real code in the SDK
assembly and can have implementation costs or weaker cancellation than a native API.

After `ResolveAssemblyReferences`, the current project graph produces the following Arc B
capabilities:

| Target | Language metadata | Guard helpers | Async/stream bridge | HTTP bridge |
|---|---|---|---|---|
| `netstandard2.0` | Downlevel `init`/`required` types | Full enabled group | `Task.WaitAsync`; memory/value-task stream APIs are enabled through the `System.Text.Json` dependency graph | `FeatureHttp` is active; `HttpClient`/`HttpContent` compatibility extensions compile |
| `net472` | Downlevel `init`/`required` types | Full enabled group | `Task.WaitAsync`; memory/value-task stream APIs are enabled through the `System.Text.Json` dependency graph | HTTP source files are present, but `FeatureHttp` is currently inactive because `System.Net.Http` is absent from `ReferencePath` |
| `net8.0` | Native BCL/compiler support | Native for the safe overloads | Native for Arc B needs | Only the package's cancellation-aware `HttpContent.LoadIntoBufferAsync` wrappers remain |
| `net9.0` | Native | Native | Native for Arc B needs | No Arc B-specific HTTP compatibility source |
| `net10.0` | Native | Native | Native for Arc B needs | No Arc B-specific HTTP compatibility source |

Polyfill detects capabilities from the assemblies actually passed to the compiler. It does not
provide `System.Net.Http` itself. The current `net472` reference graph therefore cannot compile
the forthcoming `HttpClient` transport merely because `Polyfill_HttpClient.cs` exists in the
package. M1 Arc B must add the .NET Framework `System.Net.Http` reference and verify that
`FeatureHttp` becomes active where a selected compatibility overload is used.

### Decision

Continue writing the generated model and client surface with the pinned C# language features.
For Arc B, add and verify the missing `net472` framework HTTP reference; do not add a second
HTTP implementation package merely to obtain APIs already present in the framework plus the
source polyfills.

## Q4: Are the polyfilled async and HTTP signatures behaviorally identical to native APIs?

### Finding

Not always. The package implements missing APIs using the primitives available on each target:

- Downlevel `HttpContent.ReadAsStreamAsync(CancellationToken)` calls the older parameterless
  method and then waits through the polyfilled `Task.WaitAsync`.
- The downlevel `Task.WaitAsync` races the original task against cancellation/timeout. It stops
  the caller's wait but cannot cancel an underlying operation that did not accept the token.
- Memory-based stream overloads may adapt through arrays or pooled buffers. Their signatures
  improve source compatibility but do not guarantee the allocation profile of a native modern
  runtime implementation.
- The polyfilled synchronous `HttpClient.Send` blocks over `SendAsync`; the SDK has no sync
  operation surface and has no reason to use it.
- The polyfilled `HttpClient.Get*Async` conveniences own status handling themselves, which is
  incompatible with the SDK's typed success/error envelopes and explicit response ownership.

These are valid compatibility implementations, but API-shape parity is not proof of transport
policy parity.

### Decision

Arc B should use `HttpClient.SendAsync(HttpRequestMessage,
HttpCompletionOption.ResponseHeadersRead, CancellationToken)` as its primitive, pass the token
to the actual send, and own request/response disposal. It should not build the behavior core on
Polyfill's synchronous send or `Get*Async` convenience methods. If a downlevel content API can
only cancel the wait, response disposal and cancellation tests must prove the SDK's observable
contract.

## Q5: What does Polyfill not provide for M1 Arc B?

### Finding

Polyfill closes API availability gaps only. It does not implement any opencode SDK policy:

- endpoint validation or URI construction;
- route-parameter escaping;
- owned versus injected `HttpClient` lifetime;
- password/environment resolution or Basic authentication;
- User-Agent and `x-opencode-directory` decoration;
- source-generated `System.Text.Json` selection or reflection-fallback prevention;
- success-envelope construction;
- status-to-generated-error mapping and unknown-error preservation;
- `OpenCodeException`, `OpenCodeApiException`, or `OpenCodeTransportException` classification;
- throw-by-default versus per-call `NoThrow` behavior;
- cancellation preservation, response disposal, retry, telemetry, hooks, or SSE semantics;
- Native AOT compatibility guarantees.

`System.Text.Json` `10.0.11`, not Polyfill, supplies the serializer and brings several downlevel
support assemblies that incidentally activate memory/value-task feature flags in Polyfill.

### Decision

Keep all listed behavior in the hand-written runtime core and generated operation layer defined
for M1. Polyfill is an implementation aid at the BCL boundary; it must never become an
architectural collaborator or a reason to weaken transport/error tests.

## Q6: What bounded risks remain?

### Finding

- Source-only means no runtime package dependency, but it does not mean zero assembly impact;
  selected implementations are compiled into each target.
- Upstream recommends targeting every framework consumers are expected to run for best
  performance. The locked repository matrix intentionally uses bridge targets rather than every
  intermediate TFM, so a newer consumer may select an older asset carrying extra internal
  implementations.
- Package updates can change implementation behavior as well as source availability. The
  package target also warns against replacing `DefineConstants` from the command line because
  doing so can erase its feature flags.
- `netstandard2.0` has no runtime of its own. Its Polyfill-assisted compile path still receives
  runtime proxy coverage through the locked `net472` legs.

### Decision

Do not change the TFM matrix or analyzer policy from this inventory. Review Polyfill behavior
when its pinned version changes, append rather than replace `DefineConstants`, and let Arc B's
multi-TFM build plus `net472` behavior tests arbitrate the compatibility path.

## Evidence

- Repository: `Directory.Build.props`, `Directory.Packages.props`,
  `src/OpenCode.Sdk/OpenCode.Sdk.csproj`, `tools/OpenCode.Sdk.Tools/OpenCode.Sdk.Tools.csproj`,
  and `tests/Directory.Build.props`.
- Restored Polyfill `11.0.2` package: `polyfill.nuspec`, `build/Polyfill.targets`, and the
  target-specific files under `contentFiles/cs/`.
- MSBuild evaluation: `dotnet msbuild src/OpenCode.Sdk/OpenCode.Sdk.csproj
  -p:TargetFramework=<TFM> -getItem:Compile` and feature resolution after
  `ResolveAssemblyReferences` for every repository target.
- [Polyfill 11.0.2 on NuGet](https://www.nuget.org/packages/Polyfill/11.0.2).
- [Polyfill source and documentation](https://github.com/SimonCropp/Polyfill/tree/11.0.2).

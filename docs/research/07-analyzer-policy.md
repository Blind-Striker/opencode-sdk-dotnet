# Analyzer & .editorconfig policy: claim verification + community survey

Date: 2026-08-08

> Dated evidence and decision history, not current policy. Follow current canon through
> `AGENTS.md`; current analyzer rules live in `../engineering/quality-gates.md`.
>
> Research snapshot, 2026-08-08. Verifies 13 claims (from an external ChatGPT
> conversation, treated as unverified input) against primary sources, then surveys
> how 11 prominent OSS .NET repos actually configure analyzers. Sources: Microsoft
> Learn, dotnet/sdk + dotnet/roslyn sources, analyzer repos' own docs, nuget.org
> package contents, plus **local inspection of installed SDKs 8.0.423 / 9.0.316 /
> 10.0.302 and extracted nupkgs** (NetAnalyzers 9.0.0 & 10.0.302, CSharpier 1.3.0)
> — noted inline as "verified locally". Facts only; trade-offs stated, no
> "turn-it-off" recommendations. Community survey collected by two sub-agents
> reading the repos' actual build files on GitHub; all links point at the files.

## Part I — Claim-by-claim verification

### Verdict summary

| # | Claim (abbreviated) | Verdict |
|---|---|---|
| 1 | CA1812 off by default; docs bless suppressing for DI/reflection/etc. | **CONFIRMED** (list is slightly narrower than claimed) |
| 2 | NetAnalyzers package precedence + build warning + "don't combine with EnableNETAnalyzers" | **NUANCED** — docs say so, but the mechanism changed per SDK era; warning is codeless and gone in SDK 9/10 |
| 3 | `AnalysisLevel=latest` is a CI moving target; package pins it | **NUANCED** — package pins rule *implementations*, not the enabled-set globalconfig |
| 4 | `CodeAnalysisTreatWarningsAsErrors=false` exempts only CAxxxx | **CONFIRMED** — exact CA-ID list verified in shipped props |
| 5 | `AnalysisMode` + per-category `AnalysisMode<Category>` is supported | **CONFIRMED** |
| 6 | `generated_code = true` official; affects analyzers not compiler; auto-detection heuristics | **CONFIRMED** — exact heuristics extracted from Roslyn source |
| 7 | CSharpier current, .NET 10-ready, formats XML/csproj, `check` for CI | **CONFIRMED** (1.3.0); "recommended division of labor with dotnet format" has no official Microsoft source |
| 8 | StyleCopAnalyzers stale; Microsoft positions IDE rules as alternative | **CONFIRMED** (staleness) / **NUANCED** (no explicit Microsoft positioning statement) |
| 9 | MA0048, IDE0130 + `dotnet_style_namespace_match_folder`, MA0051 options exist | **CONFIRMED** — incl. a CLI-build caveat for IDE0130 that the current SDK already solves |
| 10 | CA2007 / MA0004 / VSTHRD111 triple | **CONFIRMED** — one genuine contradiction scenario (JoinableTaskFactory), one divergence (MA0004 `DetectContext`) |
| 11 | Overlap groups (async-equivalents, sealing, dead code, globalization) | **CONFIRMED with corrections** — CA1849's Meziantou twin is MA0042 not MA0045; the dead-code trio is complementary, not redundant |
| 12 | `LangVersion=latest` discouraged; default C# mapping; net472 stance | **CONFIRMED** — explicit "Don't set LangVersion to latest" warning in docs |
| 13 | CA1801 deprecated (→ IDE0060); list of removed CA rules | **CONFIRMED** — removed: CA1801, IL3000, IL3001 (v6), CA2109, CA2229 (v8) |

---

### 1. CA1812 — off by default, suppression sanctioned

**CONFIRMED.** The rule page shows **"Enabled by default in .NET 10: No"** and opens
its suppression section with *"It is safe to suppress a warning from this rule"*,
recommending suppression when the class is created through late-bound reflection
(`Activator.CreateInstance`), registered in an IoC container as part of dependency
injection, created automatically by the runtime or ASP.NET, or used as a generic
type parameter with a `new()` constraint
([CA1812](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1812)).
Two precision notes vs. the claim: (a) *serialization* and *source generators* are
not literally in the list (serializers fall under the reflection bullet in
practice; generator-emitted types are normally excluded as generated code, see §6);
(b) the rule **auto-disables itself when the assembly has
`InternalsVisibleTo`** — re-enable via
`dotnet_code_quality.CA1812.ignore_internalsvisibleto = true` (option available
since .NET 8, same page). Relevant here because test projects usually get IVT.

### 2. NetAnalyzers NuGet package vs. SDK built-ins

**NUANCED — the docs' story is true for the ≤9.x era; the artifacts changed in 10.x.**

What the docs say ([code-analysis overview](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview),
page dated 2025-11-05): installing the package *"turns off the built-in SDK
analyzers"*; *"You'll get a build warning if the SDK contains a newer analyzer
assembly version than that of the NuGet package"*, suppressible via
`_SkipUpgradeNetAnalyzersNuGetWarning=true`; and *"If you install the
Microsoft.CodeAnalysis.NetAnalyzers NuGet package, you should not add the
EnableNETAnalyzers property... a build warning is generated."*

What the shipped artifacts actually do (verified locally):

- **≤9.x packages**: `buildTransitive/DisableNETAnalyzersForNuGetPackage.props`
  inside the nupkg hard-sets `<EnableNETAnalyzers>false</EnableNETAnalyzers>` and
  `_NETAnalyzersNuGetAssemblyVersion` (verified in the extracted
  [Microsoft.CodeAnalysis.NetAnalyzers 9.0.0 nupkg](https://www.nuget.org/packages/Microsoft.CodeAnalysis.NetAnalyzers/9.0.0)).
  That is the whole "precedence" mechanism: the package disables the SDK copy.
- **The upgrade warning** is a target `_ReportUpgradeNetAnalyzersNuGetWarning` with
  text *"The .NET SDK has newer analyzers with version 'X' than what version 'Y'
  of 'Microsoft.CodeAnalysis.NetAnalyzers' package provides. Update or remove this
  package reference..."* — it is a **codeless MSBuild `<Warning>`** (no CAxxxx/NETSDKxxxx
  ID). Verified present in SDK 8.0.423's
  `Sdks/Microsoft.NET.Sdk/analyzers/build/Microsoft.CodeAnalysis.NetAnalyzers.targets`
  and **absent from SDK 9.0.316 and 10.0.302** (local grep across all three).
  Origin: [dotnet/roslyn-analyzers#3977](https://github.com/dotnet/roslyn-analyzers/issues/3977).
- **The 10.0.302 package ships no MSBuild logic at all** — extracted nupkg contains
  only `analyzers/dotnet/**.dll`, documentation, and legacy install scripts; no
  `build/`/`buildTransitive/` folder (verified locally;
  [package](https://www.nuget.org/packages/Microsoft.CodeAnalysis.NetAnalyzers/10.0.302)).
  With no props to disable the SDK copy, duplicate analyzer DLLs (SDK + package)
  are reconciled by the SDK's package-file conflict resolution: target
  `_HandlePackageFileConflicts` feeds `Analyzers="@(Analyzer)"` through the
  `ResolvePackageFileConflicts` task and swaps in `AnalyzersWithoutConflicts`
  ([Microsoft.NET.ConflictResolution.targets](https://github.com/dotnet/sdk/blob/main/src/Tasks/Microsoft.NET.Build.Tasks/targets/Microsoft.NET.ConflictResolution.targets),
  verified locally in SDK 10.0.302).
- **UNVERIFIED**: the docs' claim that setting `EnableNETAnalyzers=true` alongside
  the package *generates a build warning*. No implementing target was found in SDK
  8/9/10 targets nor in the 9.x/10.x package MSBuild files. Treat as stale docs
  wording until proven otherwise.

Microsoft's documented guidance for decoupling from SDK updates is exactly what
this repo does: *"Install the Microsoft.CodeAnalysis.NetAnalyzers NuGet package to
decouple rule updates from .NET SDK updates"*
([overview → Latest updates](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview#latest-updates)).
Trade-off surfaced by the artifact archaeology: since SDK 9 there is **no warning
when the pinned package falls behind the SDK** — keeping the package current is
now entirely on the repo (Renovate/Dependabot), and with a 10.x package the
"who wins" question is decided silently by conflict resolution instead of an
explicit disable.

### 3. `AnalysisLevel=latest` semantics and determinism

**NUANCED.** Mechanics, from the shipped targets (verified locally in SDK 10.0.302,
source: [Microsoft.NET.Sdk.Analyzers.targets](https://github.com/dotnet/sdk/blob/main/src/Tasks/Microsoft.NET.Build.Tasks/targets/Microsoft.NET.Sdk.Analyzers.targets)):

- `latest` is resolved to a **hardcoded per-SDK constant**:
  `_LatestAnalysisLevel = 10.0` in SDK 10.0.302 (`_PreviewAnalysisLevel = 11.0`;
  the dotnet/sdk `main` branch already carries 11.0/12.0). So `latest` is stable
  *within* a major SDK line and jumps only when you move to the next major SDK.
- The resolved `EffectiveAnalysisLevel` + `AnalysisMode` select a globalconfig
  **shipped inside the SDK**, e.g.
  `Sdks/Microsoft.NET.Sdk/analyzers/build/config/analysislevel_10_all.globalconfig`
  (verified locally; the `AddGlobalAnalyzerConfigForPackage_MicrosoftCodeAnalysisNetAnalyzers`
  target does the mapping). This matches the docs' description of the
  `analysislevel_[level]_[mode].globalconfig` files
  ([overview](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview#enable-additional-rules)).
- **When the NuGet package is referenced (10.x era), the package does *not* win
  the level selection.** It only supplies analyzer DLLs (§2); the enabled-set /
  default-severity globalconfig still comes from whatever SDK performs the build.
  In the ≤9.x era, the package's own buildTransitive targets replicated the
  mapping against config files inside the package, so the package *did* own the
  rule set then.
- `AnalysisLevel` also drives the **compiler warning wave** (`WarningLevel` is
  derived next to it in the same targets file) and, unless overridden, defaults to
  `latest` for .NET 5+ TFMs
  ([msbuild-props § AnalysisLevel](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#analysislevel)).

Determinism with this repo's `global.json` (`rollForward: latestFeature`): that
policy selects *"the highest installed feature band and patch level that matches
the requested major and minor"*
([global.json overview](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json)) —
e.g. 10.0.302 may build with 10.0.4xx on another machine. Across feature bands,
`latest` still resolves to 10.0, but the **SDK-bundled analyzer binaries, the
globalconfig contents, the IDExxxx code-style analyzers (always SDK-shipped), and
warning waves all track the actual SDK installed**. So: with the NetAnalyzers
package pinned, the CA rule *implementations* are deterministic whenever the
package version ≥ SDK's bundled version; the *enabled set* and everything
style-side remain a (slow-moving) function of the SDK that CI happens to resolve.
The docs' own lever for full lock-down is a numeric `AnalysisLevel` (e.g. `10.0`)
instead of `latest`
([overview → Latest updates](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview#latest-updates)).

### 4. `CodeAnalysisTreatWarningsAsErrors` scope

**CONFIRMED — CA-only, by exact ID list.** Docs: the property governs whether
*"code quality analysis warnings (CAxxxx)"* break the build under `-warnaserror`
([msbuild-props § CodeAnalysisTreatWarningsAsErrors](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#codeanalysistreatwarningsaserrors)).
Implementation, verified locally in SDK 10.0.302's
`analyzers/build/Microsoft.CodeAnalysis.NetAnalyzers.props`: a property
`CodeAnalysisRuleIds` enumerates **every CA ID shipped by NetAnalyzers**
(CA1000…CA5405) and, when `EffectiveCodeAnalysisTreatWarningsAsErrors == false`
and `TreatWarningsAsErrors == true`, appends that list to `WarningsNotAsErrors`:

```xml
<WarningsNotAsErrors Condition="'$(EffectiveCodeAnalysisTreatWarningsAsErrors)' == 'false'
    and '$(TreatWarningsAsErrors)' == 'true'">$(WarningsNotAsErrors);$(CodeAnalysisRuleIds)</WarningsNotAsErrors>
```

Consequences: **third-party diagnostics (MA*, S*, RCS*, VSTHRD*, xUnit*, IDE*) are
not exempted** — they stay errors under `TreatWarningsAsErrors=true` regardless of
this property. Conversely, when the property is **true** (this repo's setting),
the SDK selects the `*_warnaserror.globalconfig` variant, escalating enabled CA
rules to `error` at analyzer-config level even without global TWAE (same targets;
`_warnaserror` suffix logic, verified locally). The `Effective*` indirection
exists to bypass a .NET 7-era bug
([dotnet/roslyn-analyzers#6281](https://github.com/dotnet/roslyn-analyzers/issues/6281)).

### 5. `AnalysisMode` + per-category `AnalysisMode<Category>`

**CONFIRMED.** [msbuild-props § AnalysisMode\<Category\>](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#analysismodecategory)
documents the twelve category properties (`AnalysisModeDesign`,
`AnalysisModeDocumentation`, `AnalysisModeGlobalization`,
`AnalysisModeInteroperability`, `AnalysisModeMaintainability`,
`AnalysisModeNaming`, `AnalysisModePerformance`, `AnalysisModeSingleFile`,
`AnalysisModeReliability`, `AnalysisModeSecurity`, `AnalysisModeStyle`,
`AnalysisModeUsage`): *"If you omit this property for a particular category of
rules, it defaults to the AnalysisMode value."* The per-category targets exist in
the shipped SDK (`AddGlobalAnalyzerConfigForPackage_MicrosoftCodeAnalysisNetAnalyzers{Design,Documentation,Globalization,…}`,
verified locally in 10.0.302). `AnalysisLevel<Category>` compounds
(`latest-all` etc.) work the same way
([msbuild-props § AnalysisLevel\<Category\>](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#analysislevelcategory)).
Two adjacent facts worth keeping: (a) `AnalysisMode=All` / `latest-all` still
excludes legacy rules CA1017, CA1045, CA1005, CA1014, CA1060, CA1021 and the code
metrics rules CA1501/1502/1505/1506/1509 — enable individually if wanted
([overview](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview#enable-additional-rules));
(b) when MSBuild bulk props are used, bulk `dotnet_analyzer_diagnostic.*`
editorconfig entries are **ignored** — Microsoft explicitly says per-category
enabling is better done via `AnalysisMode<Category>=All`
([configuration-options](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-options#scope)).

### 6. `generated_code = true` and auto-detection

**CONFIRMED.** Official option; docs state the boundary precisely: *"Generated
code files are excluded only from code analysis diagnostics. Other diagnostics,
such as those from the C# compiler, aren't affected by this setting."*
([configuration-options § Exclude generated code](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-options#exclude-generated-code)).
The built-in heuristics, from Roslyn's
[GeneratedCodeUtilities.cs](https://github.com/dotnet/roslyn/blob/main/src/Compilers/Core/Portable/SourceGeneration/GeneratedCodeUtilities.cs):

- **File name**: begins with `TemporaryGeneratedFile_`, or the name (before
  `.cs`) ends with `.designer`, `.generated`, `.g`, or `.g.i` — case-insensitive.
- **Header comment**: leading trivia containing `<auto-generated` or
  `<autogenerated`.
- **Attribute**: `[GeneratedCode]` on the symbol.
- **Config**: the `generated_code` analyzer-config key overrides the heuristics in
  either direction (`true` → treated generated, `false` → treated user code).

Implication for the planned model-layer generator: emitting files as `*.g.cs`
**and** with the `<auto-generated/>` header satisfies two independent heuristics,
and a `[*.g.cs] generated_code = true` editorconfig section is the belt-and-braces
third layer — but none of them silence *compiler* warnings (nullable, obsolete
API, etc.) in generated files; those need clean emission or `#pragma`s in the
template.

### 7. CSharpier

**CONFIRMED on the tool facts; the "official division of labor" part has no
Microsoft source.**

- **Current stable: 1.3.0**, released 2026-06-07
  ([nuget.org version list](https://www.nuget.org/packages/CSharpier),
  [GitHub release 1.3.0](https://github.com/belav/csharpier/releases/tag/1.3.0)).
- **.NET 10**: the 1.3.0 dotnet-tool package ships `tools/net8.0`, `tools/net9.0`,
  `tools/net10.0` payloads (verified locally from the extracted nupkg).
- **XML/csproj**: yes — configuration exposes `xmlWhitespaceSensitivity`
  (strict default for everything except `xaml`/`axaml`; `.csproj` named in the
  docs), XML uses `indentSize` (default 2 for XML)
  ([Configuration docs](https://csharpier.com/docs/Configuration)); the 1.3.0
  release notes are largely about XML formatting behavior (strict whitespace,
  `csharpier-ignore` in XML, invalid-XML-as-error)
  ([release notes](https://github.com/belav/csharpier/releases/tag/1.3.0)).
- **CI**: `dotnet csharpier check` — *"Outputs any files that have not already
  been formatted. By default this will return exit code 1 if there are unformatted
  files which is useful for CI pipelines"*; 1.3.0 added `--use-cache` for `check`
  ([CLI docs](https://csharpier.com/docs/CLI)). Config lives in
  `.csharpierrc(.json|.yaml)` or `.editorconfig` (csharpierrc wins)
  ([Configuration docs](https://csharpier.com/docs/Configuration)).
- **dotnet format overlap**: `dotnet format` is *"a code formatter that applies
  style preferences and static analysis recommendations"* driven by .editorconfig,
  with `whitespace` / `style` / `analyzers` subcommands and `--verify-no-changes`
  for CI ([dotnet format docs](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-format)).
  It fixes rule violations; it is not an opinionated line-wrapping printer.
  **No Microsoft document prescribes a division of labor with CSharpier** — mark
  that part of the claim UNVERIFIED as official guidance. The working community
  pattern exists though: riok/mapperly runs `csharpier check` **plus**
  `dotnet format style` **plus** `dotnet format analyzers` with the workflow
  comment *"don't run dotnet format for whitespace formatting as this is done by
  csharpier"* ([lint.yml](https://github.com/riok/mapperly/blob/main/.github/workflows/lint.yml)),
  and CSharpier's own repo cedes whitespace by setting
  `dotnet_diagnostic.IDE0055.severity = none`
  ([.editorconfig](https://github.com/belav/csharpier/blob/main/.editorconfig)).

### 8. StyleCopAnalyzers staleness

**CONFIRMED (staleness) / NUANCED (positioning).** Last stable release **1.1.118
on 2019-04-29** ([GitHub release](https://github.com/DotNetAnalyzers/StyleCopAnalyzers/releases/tag/1.1.118));
newest prerelease **1.2.0-beta.556 on 2023-12-20** — nothing since
([nuget.org](https://www.nuget.org/packages/StyleCop.Analyzers/1.2.0-beta.556)).
The README's own support table tops out at *"1.2.0-beta — C# 8.0 — VS2019"*
([README](https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/master/README.md)) —
i.e., no claimed support for C# 9-14. It remains massively used (~264M downloads,
~52M on the beta alone, same nuget page) and Polly/OTel still run the beta (Part
II). On positioning: Microsoft docs merely list StyleCop among *"third party
analyzers"* ([overview](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview#third-party-analyzers));
the maintained first-party style mechanism is IDExxxx code-style analysis +
`EnforceCodeStyleInBuild`
([overview § code-style analysis](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview#code-style-analysis)),
but no doc explicitly says "use IDE rules instead of StyleCop".

### 9. MA0048, IDE0130, MA0051

**CONFIRMED, all three, with exact option keys.**

- **MA0048 "File name must match type name"** — warning, enabled by default
  ([rule index](https://github.com/meziantou/Meziantou.Analyzer/blob/main/docs/README.md)).
  Options: `MA0048.mode = Exact | Prefix | LongestCommonPrefix` (default Exact),
  `MA0048.exclude_file_local_types` (default true),
  `MA0048.only_validate_first_type` (default false),
  `MA0048.allow_oft_for_all_generic_types`,
  `dotnet_diagnostic.MA0048.excluded_symbol_names`; generic arity via
  `` Foo`1.cs `` also accepted
  ([MA0048.md](https://github.com/meziantou/Meziantou.Analyzer/blob/main/docs/Rules/MA0048.md)).
- **IDE0130 "Namespace does not match folder structure"** with option
  `dotnet_style_namespace_match_folder` (default `true`); expected namespace =
  `RootNamespace` + folder path
  ([IDE0130](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/ide0130)).
  The docs warn it needs `CompilerVisibleProperty` entries for `RootNamespace` /
  `ProjectDir` to work in CLI builds — **the current SDK adds those automatically
  for all C#/VB projects** (comment *"Used for analyzer to match namespace to
  folder structure"*,
  [Microsoft.NET.Sdk.Analyzers.targets](https://github.com/dotnet/sdk/blob/main/src/Tasks/Microsoft.NET.Build.Tasks/targets/Microsoft.NET.Sdk.Analyzers.targets);
  verified locally in SDK 10.0.302), so the docs' manual step is obsolete on
  modern SDKs. Enforcement at build still requires `EnforceCodeStyleInBuild=true`
  plus a warning/error severity (already the skeleton's setup).
- **MA0051 "Method is too long"** — warning, enabled by default; defaults 60
  lines / 40 statements; exact keys `MA0051.maximum_lines_per_method`,
  `MA0051.maximum_statements_per_method`, `MA0051.skip_local_functions`
  ([MA0051.md](https://github.com/meziantou/Meziantou.Analyzer/blob/main/docs/Rules/MA0051.md)).

### 10. ConfigureAwait enforcement: CA2007 vs MA0004 vs VSTHRD111

| | CA2007 | MA0004 | VSTHRD111 |
|---|---|---|---|
| Default | **Disabled** ("Enabled by default in .NET 10: No") | **Warning, enabled** | **Hidden (off)** |
| Trigger | async method awaits a `Task` directly | any `await` without explicit `ConfigureAwait(...)`, *filtered by context detection* | any await on **`Task` or `ValueTask`** without `.ConfigureAwait(bool)` |
| Config | `dotnet_code_quality.CA2007.exclude_async_void_methods`, `dotnet_code_quality.CA2007.output_kind` | `MA0004.report = DetectContext` (default) \| `Always` | none documented |
| Source | [CA2007](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca2007) | [MA0004.md](https://github.com/meziantou/Meziantou.Analyzer/blob/main/docs/Rules/MA0004.md) | [VSTHRD111](https://microsoft.github.io/vs-threading/analyzers/VSTHRD111.html) |

**CONFIRMED**, with these load-bearing details for enabling more than one:

- CA2007's docs are explicit that it is **library-targeted**: *"It is generally
  appropriate to suppress the warning entirely for projects that represent
  application code"*; either `true` or `false` argument satisfies it. Its
  `output_kind` option can restrict it to `DynamicallyLinkedLibrary` outputs.
- MA0004's default `DetectContext` mode *"report[s] only if it considers
  ConfigureAwait is needed"* — i.e., in code Meziantou detects as
  context-dependent (UI/Blazor), MA0004 goes quiet while CA2007 keeps firing.
  **Not a contradiction (adding `ConfigureAwait(true)` satisfies both), but a
  coverage divergence**: with both enabled at error, CA2007 is the effective
  superset; `MA0004.report = Always` aligns MA0004 with it. Meziantou himself
  resolves the duplicate the other way — his build SDK sets CA2007 to `none` with
  the literal comment `# Superseeded by MA0004`
  ([Meziantou.Net.Sdk config](https://github.com/meziantou/Meziantou.Net.Sdk/blob/main/src/configuration/Analyzer.Microsoft.CodeAnalysis.NetAnalyzers.editorconfig)).
- VSTHRD111 is hidden by default because the correct argument is
  environment-dependent, and its docs contain the one **genuinely contradictory**
  guidance in the trio: *"Where JoinableTaskFactory does apply, use of
  `.ConfigureAwait(false)` is not recommended"*
  ([VSTHRD111](https://microsoft.github.io/vs-threading/analyzers/VSTHRD111.html)) —
  i.e., in VS-extension/JTF code, vs-threading wants `ConfigureAwait(true)` where
  a blanket "always false" policy (the common CA2007 fix) is actively wrong. For
  this SDK (no JTF, no UI thread), that scenario is theoretical; the practical
  overlap cost is only duplicate diagnostics per await (up to three per site with
  all three enabled). VSTHRD111 is also the only one of the three whose docs
  explicitly cover `ValueTask` awaits.

### 11. Overlap groups

- **CA1849 / VSTHRD103 / MA0042-MA0045 (sync-over-async in async context).**
  Overlap CONFIRMED, one correction: the claim named MA0045 as the third twin, but
  Meziantou splits the concern into **MA0042 "Do not use blocking calls when the
  calling method is async"** (Info, enabled) and **MA0045 "Do not use blocking
  calls, even when the calling method must become async"** (Info, **disabled by
  default** — it demands signature changes)
  ([rule index](https://github.com/meziantou/Meziantou.Analyzer/blob/main/docs/README.md)).
  CA1849 (disabled by default) fires on sync calls with Async-suffixed
  equivalents plus `Task.Wait()` / `Task<T>.Result` / `GetAwaiter().GetResult()`
  from Task-returning methods
  ([CA1849](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1849));
  VSTHRD103 covers the same core set and adds a per-repo exclusion file
  (`vs-threading.SyncMethodsToExcludeFromVSTHRD103.txt`)
  ([VSTHRD103](https://microsoft.github.io/vs-threading/analyzers/VSTHRD103.html)).
- **CA1852 vs MA0053 (sealing).** Overlap CONFIRMED; scopes differ. CA1852:
  internal-only, "Enabled by default: No", auto-disabled under
  `InternalsVisibleTo` unless `ignore_internalsvisibleto = true`
  ([CA1852](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1852)).
  MA0053 (Info, enabled): classes *and records*, extendable to public types
  (`MA0053.public_class_should_be_sealed`), exceptions
  (`MA0053.exceptions_should_be_sealed`), and virtual-member types
  (`MA0053.class_with_virtual_member_should_be_sealed`) — a configurable superset
  ([MA0053.md](https://github.com/meziantou/Meziantou.Analyzer/blob/main/docs/Rules/MA0053.md)).
  For a NuGet library, `public_class_should_be_sealed = true` is the
  API-design-relevant extra CA1852 cannot do.
- **IDE0051/IDE0052 vs CA1812 vs S1144/S3459 (dead code).** These are
  **complementary, not redundant** — different axes: IDE0051 = unused *private
  members*, IDE0052 = private members *written but never read*
  ([IDE0051](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/ide0051),
  [IDE0052](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/ide0052));
  CA1812 = uninstantiated *internal types* (§1); Sonar S1144 = unused private
  types **and** members (Code Smell, default severity Major,
  [RSPEC-1144](https://rules.sonarsource.com/csharp/RSPEC-1144/) /
  [rspec json](https://github.com/SonarSource/sonar-dotnet/blob/master/analyzers/rspec/cs/S1144.json));
  S3459 = members that are read but never assigned (Code Smell, Minor,
  [RSPEC-3459](https://rules.sonarsource.com/csharp/RSPEC-3459/) /
  [rspec json](https://github.com/SonarSource/sonar-dotnet/blob/master/analyzers/rspec/cs/S3459.json)).
  Real-world arbitration exists: Polly disables CA1812 with the generated-config
  comment *"S1144 finds more cases and has no false positives"*
  ([Library.globalconfig](https://github.com/App-vNext/Polly/blob/main/eng/analyzers/Library.globalconfig)).
- **CA130x vs MA0074/75/76 (globalization).** Overlap CONFIRMED. NetAnalyzers:
  CA1304 "Specify CultureInfo", CA1305 "Specify IFormatProvider", CA1307/CA1310
  "Specify StringComparison for clarity/correctness", CA1311 "Specify a culture or
  use an invariant version"
  ([globalization rules index](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/globalization-warnings)).
  Meziantou: MA0074 "Avoid implicit culture-sensitive methods" (warning), MA0075
  "Do not use implicit culture-sensitive ToString" (Info), MA0076 "…in
  interpolated strings" (Info), plus MA0011 "IFormatProvider is missing" (warning)
  as the CA1305 twin ([rule index](https://github.com/meziantou/Meziantou.Analyzer/blob/main/docs/README.md)).
  The MA0075/76 pair flags implicit `ToString` in string concatenation and
  interpolation — call sites the CA rules do not cover; the redundancy is
  asymmetric in both directions, so "keep both" costs duplicate diagnostics only
  on the intersection.

### 12. `LangVersion=latest` and defaults

**CONFIRMED.** Microsoft's C# docs (updated 2026-01) carry an explicit warning:
*"Don't set the `LangVersion` element to `latest`. The `latest` setting means the
installed compiler uses its latest version. The value of `latest` can change from
machine to machine, making builds unreliable. In addition, it enables language
features that might require runtime or library features not included in the
current SDK."*
([Configure language version](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/configure-language-version)).
Defaults table ([C# language versioning](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-versioning)):
.NET 10.x → **C# 14**, .NET 9.x → C# 13, .NET 8.x → C# 12, **.NET Framework (all,
incl. net472) → C# 7.3**, .NET Standard 2.0 → C# 7.3, .NET 11 → C# 15. Stance on
forcing newer versions onto old TFMs: *"Using a C# language version newer than the
version associated with your target TFM is unsupported"* and *"Choosing a language
version newer than the default can cause hard-to-diagnose compile-time and runtime
errors"* (both pages). In practice most polyglot-TFM libraries do exactly this
unsupported thing with polyfills (Polly targets net462 with modern C#; Serilog
ships PolySharp — Part II); the docs' position is "unsupported", not "impossible".
Fact for the local audit: this repo currently sets `LangVersion=latest` in
`Directory.Build.props`, which combines both flagged properties (`latest` + net472
in the planned matrix); the deterministic alternative endorsed by the docs is an
explicit number (e.g. `14.0`).

### 13. CA1801 deprecation + removed-rule list

**CONFIRMED.** CA1801's page: *"This rule has been deprecated in favor of
[IDE0060]"*, and it shows "Enabled by default in .NET 10: No"
([CA1801](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1801)).
The authoritative removal ledger is the shipped-analyzer changelog
([AnalyzerReleases.Shipped.md](https://github.com/dotnet/sdk/blob/main/src/Microsoft.CodeAnalysis.NetAnalyzers/src/Microsoft.CodeAnalysis.NetAnalyzers/AnalyzerReleases.Shipped.md)),
"Removed Rules" sections (verified by grep of the raw file):

| Removed in | Rules |
|---|---|
| Release 6.0 | **CA1801** (ReviewUnusedParameters), IL3000, IL3001 (moved to mono/linker) |
| Release 8.0 | **CA2109** (ReviewVisibleEventHandlers), **CA2229** (serialization constructors) |

For the .editorconfig zombie-check, add the never-enabled-by-`All` legacy set from
§5 (CA1017, CA1045, CA1005, CA1014, CA1060, CA1021, CA1501/1502/1505/1506/1509 —
*"These legacy rules might be deprecated in a future version"*,
[overview](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview#enable-additional-rules)).
Any `dotnet_diagnostic.CA1801/CA2109/CA2229.severity` line in our config is
configuring a rule that no longer ships.

---

## Part II — Community survey (11 repos, default branches, 2026-08-08)

Legend: TWAE = TreatWarningsAsErrors; CATWAE = CodeAnalysisTreatWarningsAsErrors.
Survey targets skew toward NuGet libraries multi-targeting old TFMs. All cells
come from the repos' own committed files (links in the notes below).

| Repo | Analyzer packages (beyond SDK) | TWAE | CATWAE | AnalysisLevel/Mode | ConfigureAwait | Formatter (CI gate?) | Test exemption |
|---|---|---|---|---|---|---|---|
| [dotnet/runtime](https://github.com/dotnet/runtime) (libs) | NetAnalyzers (pinned, prerelease), CSharp.CodeStyle, StyleCop, Microsoft.DotNet.CodeAnalysis | via Arcade (external) | absent | `preview` / per-rule globalconfig | **CA2007 = warning** (src), none (test) | none (style via CodeStyle pkg at build) | src↔test globalconfig swap |
| [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk) | none (own generator analyzers only) | **true (props)** | absent | absent (defaults) | none | none | tests NoWarn only |
| [open-telemetry/opentelemetry-dotnet](https://github.com/open-telemetry/opentelemetry-dotnet) | StyleCop beta, BannedApi, PublicApi, MSTest.Analyzers | true **(Release only)** + MSBuildTWAE | absent | **`latest-All`** + EnforceCodeStyleInBuild | CA2007 via latest-All; `[*Tests.cs]` → none | **dotnet format --verify-no-changes (CI gate)** | test ruleset + editorconfig section |
| [App-vNext/Polly](https://github.com/App-vNext/Polly) | **SonarAnalyzer.CSharp**, StyleCop beta, BannedApi, PublicApi | Cake build: `TreatAllWarningsAs=Error` (CI-side) | absent | `latest` + EnforceCodeStyleInBuild | **CA2007 = warning** (Library.globalconfig), none (Test), NoWarn (samples) | none (StyleCop in-compiler) | `ProjectType` → per-type generated globalconfig |
| [npgsql/npgsql](https://github.com/npgsql/npgsql) | PublicApi; NUnit.Analyzers (tests) | **true (props)** | absent | `latest` | none (v11 is net10-only) | none | test NoWarn |
| [serilog/serilog](https://github.com/serilog/serilog) | none (PolySharp only) | **true (props)** | absent | absent | none (despite net462/net471/ns2.0) | none | n/a |
| [MassTransit/MassTransit](https://github.com/MassTransit/MassTransit) | NUnit.Analyzers (tests only) | absent | absent | absent | none (despite ns2.0/net472) | none (ReSharper DotSettings) | tests NoWarn |
| [meziantou/Meziantou.Framework](https://github.com/meziantou/Meziantou.Framework) | Meziantou.Analyzer + BannedApi via custom [Meziantou.NET.Sdk](https://github.com/meziantou/Meziantou.Net.Sdk) | true when CI/Release/**LLM-agent detected** | absent | `latest-all` | **CA2007 = none ("Superseeded by MA0004"); MA0004 = silent** (net10+ only) | none | tests/.editorconfig + `RunAnalyzers=false` during `dotnet test` |
| [dotnet/roslynator](https://github.com/dotnet/roslynator) | own Roslynator packages (dogfood) | CI env var `TreatWarningsAsErrors: true` | absent | absent | **RCS1090 + `roslynator_configure_await = true`**; off in Tests | **dotnet format --verify-no-changes --severity info (CI gate)** | src/Tests/.editorconfig |
| [belav/csharpier](https://github.com/belav/csharpier) | PublicApi, PolySharp | CI job `-p:TreatWarningsAsErrors=true` | absent | **AnalysisMode=Recommended** + EnforceCodeStyleInBuild | none (despite ns2.0) | **csharpier check (CI gate)** + husky pre-commit; IDE0055 → none | per-project only |
| [riok/mapperly](https://github.com/riok/mapperly) | Meziantou.Analyzer (src); NSubstitute.Analyzers (tests) | CI `/p:TreatWarningsAsErrors=true` | absent | absent | none (despite ns2.0) | **csharpier check + dotnet format style + dotnet format analyzers (CI gate)** | test props + Verify-file exemptions |

Notable per-repo evidence (file-level sources):

- **dotnet/runtime** wires everything through
  [eng/Analyzers.targets](https://github.com/dotnet/runtime/blob/main/eng/Analyzers.targets)
  and swaps [CodeAnalysis.src.globalconfig](https://github.com/dotnet/runtime/blob/main/eng/CodeAnalysis.src.globalconfig)
  (`dotnet_diagnostic.CA2007.severity = warning`) for
  [CodeAnalysis.test.globalconfig](https://github.com/dotnet/runtime/blob/main/eng/CodeAnalysis.test.globalconfig)
  (`= none`) when `IsTestProject`. Cross-analyzer arbitration is commented in
  place (e.g. SA1206 disabled because StyleCop lags the language while IDE0036
  still enforces modifier order).
- **Polly** is the closest structural precedent to this repo (Sonar + StyleCop +
  BannedApi + PublicApi, old TFMs down to net462 in
  [Polly.Core.csproj](https://github.com/App-vNext/Polly/blob/main/src/Polly.Core/Polly.Core.csproj)):
  every project declares `<ProjectType>Library|Test|Benchmark</ProjectType>` and
  [eng/Analyzers.targets](https://github.com/App-vNext/Polly/blob/main/eng/Analyzers.targets)
  injects the matching machine-generated globalconfig; duplicates are resolved
  with explicit comments (*"StyleCop handles this"* → CA1707 none; *"S1144 finds
  more cases"* → CA1812 none).
- **Meziantou.Framework** (the Meziantou.Analyzer dogfood) is the only surveyed
  repo whose policy engine ships as a reusable MSBuild SDK; it also flips
  warnings-as-errors on when it detects an **LLM coding agent** via env vars
  ([Common.props](https://github.com/meziantou/Meziantou.Net.Sdk/blob/main/src/common/Common.props)),
  and disables analyzers during `dotnet test` for speed because the Release
  packaging job re-enforces them
  ([Tests.targets](https://github.com/meziantou/Meziantou.Net.Sdk/blob/main/src/common/Tests.targets)).
- **OTel .NET** documents the classic test friction verbatim in
  [.editorconfig](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/.editorconfig):
  *"CA2007 … It is not working with xunit"* under `[*Tests.cs]`.
- **mapperly** is the only surveyed library running the full three-layer gate
  (CSharpier for whitespace, `dotnet format style`, `dotnet format analyzers`) and
  pins CSharpier 1.3.0 with `preprocessorSymbolSets` for multi-TFM formatting
  ([.csharpierrc.yaml](https://github.com/riok/mapperly/blob/main/.csharpierrc.yaml)).

### Synthesis

**(a) ConfigureAwait in multi-TFM libraries.** Where it is enforced at all, the
ecosystem answer is uniform: **CA2007 as warning on product code, escalated to
error by the warnings-as-errors layer, and `none` for tests** (dotnet/runtime,
Polly, OTel). Nobody surveyed uses MA0004 or VSTHRD111 for this — except the
Meziantou ecosystem, which uses MA0004 *instead of* CA2007 and documents the
duplicate kill (`# Superseeded by MA0004`). Roslynator uses its own RCS1090.
A surprising number of old-TFM libraries enforce nothing (Serilog, MassTransit,
MCP C# SDK, mapperly, CSharpier) and rely on review. For a net472-shipping SDK,
the runtime/Polly pattern (CA2007 error in lib, off in tests) is the established
strict-lane choice; MA0004 alongside it duplicates rather than contradicts
(§10), provided `MA0004.report=Always` if identical coverage is wanted.

**(b) Strictness model.** Two clear findings. First, **`CodeAnalysisTreatWarningsAsErrors`
is used by 0 of 11 repos** — the ecosystem either makes everything an error or
nothing, it does not carve the CA subset out. Second, most repos do *not* hard-set
`TreatWarningsAsErrors=true` unconditionally in props: 3 do (MCP, Npgsql,
Serilog — all otherwise light on analyzers); the analyzer-heavy repos gate it on
CI/Release instead (Polly via Cake, Roslynator via env var, CSharpier/mapperly via
`-p:` flags, OTel via `Configuration==Release`, Meziantou via CI/Release/LLM
detection) so local Debug loops stay fast and unblocked. Nobody uses
`AnalysisMode=All`; the strictest observed are `latest-All` (OTel, Meziantou SDK),
`preview` + per-rule globalconfig (runtime), and `Recommended` (CSharpier). This
repo's `AnalysisMode=All` + unconditional TWAE is therefore stricter than every
surveyed repo — consistent with the owner's stated preference, with the known
trade-off being local-build friction that the surveyed repos engineered around.

**(c) Formatter in 2026.** Split, but structured: `dotnet format
--verify-no-changes` as CI gate (OTel, Roslynator); **CSharpier as CI gate in the
newer/Go-style-minded repos** (CSharpier itself, mapperly — the latter layering
`dotnet format style`/`analyzers` on top with an explicit "csharpier owns
whitespace" comment); in-compiler style enforcement only (Polly, runtime, both via
StyleCop-beta and/or CodeStyle analyzers); nothing (MassTransit, Serilog, Npgsql,
MCP). No surveyed repo runs StyleCop *and* CSharpier together. The
CSharpier-as-gate pattern requires ceding whitespace rules (IDE0055) to it (§7).

**(d) Overlap handling.** Where multiple analyzers coexist, every mature repo
resolves duplicates **explicitly, per rule, with a comment naming the winner**
(Polly's generated configs, Meziantou.NET.Sdk, runtime's SA-vs-IDE notes,
CSharpier's IDE0055) rather than leaving both on. The observed rationale is not
noise reduction but determinism: one canonical diagnostic per defect class, with
the disabled twin's line documenting *why* it is off and which rule covers it.
That is compatible with a maximalist posture: "keep both" survives where rules
genuinely differ (e.g. MA0075/76 vs CA1305, §11), and the comment-driven disable
is reserved for true 1:1 duplicates.

---

## Part III — Facts most relevant to this repo's current skeleton

Neutral restatement of findings that intersect the existing
`Directory.Build.props` (no recommendations; the local audit decides):

1. `TreatWarningsAsErrors=true` + `CodeAnalysisTreatWarningsAsErrors=true` means
   the `*_warnaserror.globalconfig` path is active and the CA-exemption mechanism
   (§4) is unused — the second property is currently redundant-but-harmless; its
   only effect is escalating enabled CA rules to error even where TWAE is off.
2. The pinned NetAnalyzers 10.0.302 package ships no MSBuild logic (§2): rule
   selection follows the building SDK's globalconfig, and **no warning will fire
   if the SDK's bundled analyzers overtake the pinned package** (warning removed
   in SDK 9+). Package hygiene has to come from dependency automation.
3. `AnalysisLevel=latest` resolves to `10.0` for every 10.x SDK (§3); combined
   with `global.json rollForward: latestFeature` the CA enabled-set is stable
   within the 10.x line, while IDExxxx style rules and warning waves track the
   resolved SDK.
4. `LangVersion=latest` is explicitly warned against in current docs (§12), and
   C# >7.3 on net472 is formally unsupported (though common practice with
   polyfills).
5. The generator plan (§6) has a three-layer path to analyzer exemption
   (`*.g.cs` name + `<auto-generated/>` header + `generated_code=true`), none of
   which exempts compiler diagnostics.
6. Config lines for CA1801, CA2109, CA2229 would be zombies (§13); CA1017,
   CA1005, CA1014, CA1021, CA1045, CA1060 and CA1501-1509 metrics rules are not
   enabled even by `AnalysisMode=All` and need per-rule opt-in if wanted (§5).

---

## Part IV — Local audit and decisions (2026-08-08, same day)

The parallel local audit of `.editorconfig` (662 lines; 260 explicit
`dotnet_diagnostic` severities before the changes below: 204 error / 34
suggestion / 21 none / 1 warning). The severity profile matched the file the
ChatGPT conversation reviewed (258/203/33/21/1) — that file was this one's direct
ancestor, and part of the conversation's advice (CA1812 → suggestion, comment
included) had already been absorbed into it.

### Audit findings beyond the parked items

- The "[OVERLAP GROUP] … both kept" comments promised MA-side enforcement that was
  never explicit: MA0001/MA0011/MA0074 had no severity lines (riding Meziantou
  defaults, silently escalated by TreatWarningsAsErrors). Same pattern in §10.6:
  MA0018 disabled "in favor of the CA version" while CA1000 had no line either.
- §7.4 "Deprecated/Removed Rules" was doubly wrong: CA1801 = error (rule removed
  in NetAnalyzers v6 → dead setting) and CA1031 = none filed under "deprecated"
  although it is an active, deliberately-disabled rule.
- `dotnet_diagnostic.CA1500.severity = error` configured a rule that never shipped
  in NetAnalyzers (legacy FxCop only; learn.microsoft.com quality-rules page 404s —
  verified 2026-08-08).
- Sonar was 98% implicit: 9 of ~470 rules configured; every SonarWay
  default-warning became a build error via TreatWarningsAsErrors without that ever
  being decided rule-by-rule.
- `S2437 = warning` was the file's only `warning` (effectively error under TWAE) —
  notation inconsistency.
- The VSTHRD111 = none comment "[conflicts with modern guidance]" applied
  app-code guidance to a net472-multi-targeting library.
- No `generated_code = true` section existed.
- Minor: ReSharper-only keys (`resharper_wrap_*`,
  `csharp_max_attribute_length_for_same_line`), duplicate indent-glob sections in
  Section 1, `CS1591 = none` alongside `GenerateDocumentationFile=true` (public-API
  XML-doc policy undecided).

### Decisions (D1–D9, applied 2026-08-08)

| # | Decision |
|---|---|
| D1 | **ConfigureAwait: triple enforcement.** CA2007 + MA0004 (`MA0004.report = Always`) + VSTHRD111 all `error` in product code; all three `none` in the test section. One fix (`ConfigureAwait(false)`) satisfies all three; VSTHRD111 adds explicit ValueTask coverage (relevant: SSE/`IAsyncEnumerable` awaits are ValueTask-based). Ecosystem standard is CA2007-only (Part II a); the redundancy is the owner's deliberate fail-closed choice. |
| D2 | **Zombie cleanup.** CA1801 and CA1500 lines removed; CA1031 re-homed to §6.1 with an honest "[opinionated]" comment; §7.4 deleted. |
| D3 | **Implicit → explicit.** MA0001/MA0011/MA0074/MA0075/MA0076 and CA1000 now have explicit `error` lines; S2437 `warning` → `error`. |
| D4 | **Sonar: strictness kept, now chosen.** Behavior unchanged (SonarWay defaults + TWAE escalation), documented as policy in the section header; misfires get per-rule arbitration comments naming the winner (community pattern, Part II d). |
| D5 | **Sealing superset.** MA0053 `none` → `error` with `MA0053.public_class_should_be_sealed = true` — public API ships sealed-by-default (extend-only design); CA1852 keeps covering internal. |
| D6 | **Determinism pins.** `LangVersion` latest → `14.0`, `AnalysisLevel` latest → `10.0` (§3, §12). `AnalysisMode=All` + unconditional TWAE + CATWAE **kept** — see rationale below. |
| D7 | **Generated code.** `[*.{g.cs,generated.cs,designer.cs}] generated_code = true` section added; folder-based patterns and clean-compile requirements go to the codegen spike's acceptance criteria. |
| D8 | **Go-style.** MA0048 → `error` (file name = type name); explicit `dotnet_style_namespace_match_folder = true` next to IDE0130. CSharpier: decided in principle, wired together with the first `.csproj` (mapperly pattern: `csharpier check` owns whitespace, IDE0055 ceded to it, `max_line_length`/MA0051 limits finalized then). |
| D9 | **Docs generation.** `GenerateDocumentationFile=true` kept with a guard comment — IDE0005 (unused usings) only fires in CLI builds when XML doc generation is on. `CS1591 = none` stays; public-surface XML-doc enforcement is deferred to the API design session. |

### Why `AnalysisMode=All` stays despite 0/11 community adoption

1. **The moving-target risk is pinned away**: `latest`→`10.0` resolution is
   constant within the SDK 10 line (§3), so the enabled-set changes only when the
   repo deliberately moves `global.json` to a new major.
2. **Fail-closed beats fail-open for this repo**: All-as-baseline + explicit
   per-rule downgrades (the file's existing 21×none/34×suggestion layer) records a
   decision for every silenced rule; Recommended-as-baseline lets new rules pass
   silently.
3. **The community's reason to soften doesn't apply**: surveyed repos gate TWAE to
   CI/Release to protect *human* inner loops; this repo's inner loop is mostly
   LLM agents, for which hard gates are immediate feedback (Meziantou.NET.Sdk even
   *enables* TWAE when it detects an agent). If human contributors join later, the
   CI-gated TWAE pattern (Part II b) is the ready fallback.

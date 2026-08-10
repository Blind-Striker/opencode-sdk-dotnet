# Slice 0 — Tooling Skeleton Implementation Plan

Date: 2026-08-10

> **For agentic workers:** REQUIRED SUB-SKILL: Use deniz-process:subagent-driven-development
> (recommended) or deniz-process:executing-plans to implement this plan task-by-task. Steps
> use checkbox (`- [ ]`) syntax for tracking.

**Goal:** stand up the `tools/` skeleton — file-based entry, `OpenCode.Sdk.Tools` library
with a testable `ToolApp` composition root and a fail-loud `generate` stub, its TUnit test
project — and prove the generator spec §3.3 verification list (strict-props build, cache
staleness, invocation form) with a CI smoke step.

**Architecture:** generator spec §3.1's sealed layout — a 3-line file-based entry
delegating into a PathSmith-style csproj library whose `CommandApp` wiring lives in a
`ToolApp` factory so `CommandAppTester` can exercise it. No generator pipeline code in
this slice; `generate` exists only as a fail-loud stub replaced in slice 3.

**Tech Stack:** Spectre.Console.Cli + Spectre.Console.Cli.Extensions.DependencyInjection +
Spectre.Console.Cli.Testing (all verified present on NuGet; stable line 0.55.0 / 0.26.0),
Microsoft.Extensions.DependencyInjection, TUnit (already pinned).

## Global Constraints

- `LangVersion=14.0`, `AnalysisLevel=10.0` — deliberate numeric pins; never "fix" to
  `latest` (AGENTS.md Hard Rules).
- Full analyzer wall + `TreatWarningsAsErrors=true` applies to `tools/` (product-rule
  scope): `ConfigureAwait(false)` on every await, guards on public inputs (CA1062
  contract), MA0048 one type per file, IDE0130 folder = namespace.
- Test naming: `{Symbol}_Should_{Expected_Behavior}[_When_{Condition}]`; test classes
  `{Sut}Tests` (AGENTS.md Engineering Conventions). Test code is exempt from the
  ConfigureAwait triple (.editorconfig §15).
- All artifacts in English; Conventional Commits; per-task commits on the slice branch are
  part of the agreed development loop (no per-commit approval; master merges via PR only).
- Everything temporary goes to `.scratchpad/` (gitignored).
- Central package management: new packages get a `PackageVersion` row in
  `Directory.Packages.props`; before pinning, check for a newer **stable** version
  (`dotnet package search <id> --exact-match`) and pin that instead if one exists — the
  versions below are the newest stable known at plan time.
- Contradictions with a spec: stop and classify per `docs/agents/deviation-protocol.md`.
- Work happens on branch `feature/slice-00-tooling-skeleton` in a worktree
  (deniz-process:using-git-worktrees).

---

### Task 1: Tools library project skeleton

**Files:**
- Modify: `Directory.Packages.props`
- Create: `tools/OpenCode.Sdk.Tools/OpenCode.Sdk.Tools.csproj`
- Modify: `OpenCode.slnx`

**Interfaces:**
- Consumes: repo build infrastructure (`Directory.Build.props` strict props inherit into
  `tools/` automatically).
- Produces: an empty net10.0 library project that later tasks fill; the `/tools/` solution
  folder.

- [x] **Step 1: Add package pins to `Directory.Packages.props`**

Under `<!-- microsoft packages -->` add:

```xml
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="10.0.1"/>
```

Replace the empty `<!-- third-party packages -->` comment section with:

```xml
    <!-- third-party packages -->
    <PackageVersion Include="Spectre.Console.Cli" Version="0.55.0"/>
    <PackageVersion Include="Spectre.Console.Cli.Extensions.DependencyInjection" Version="0.26.0"/>
```

Under `<!-- test packages -->` add:

```xml
    <PackageVersion Include="Spectre.Console.Cli.Testing" Version="0.55.0"/>
```

- [x] **Step 2: Create `tools/OpenCode.Sdk.Tools/OpenCode.Sdk.Tools.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>$(DefaultTargetFramework)</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection"/>
    <PackageReference Include="Spectre.Console.Cli"/>
    <PackageReference Include="Spectre.Console.Cli.Extensions.DependencyInjection"/>
  </ItemGroup>

</Project>
```

(`$(DefaultTargetFramework)` is `net10.0` from `Directory.Build.props`; the tool is
single-TFM by design — testing spec §4.)

- [x] **Step 3: Add the project to `OpenCode.slnx`**

Insert after the `/tests/` folder (folders are kept alphabetical):

```xml
  <Folder Name="/tools/">
    <Project Path="tools/OpenCode.Sdk.Tools/OpenCode.Sdk.Tools.csproj" />
  </Folder>
```

- [x] **Step 4: Build**

Run: `dotnet build --configuration Release`
Expected: success, zero warnings (the empty library must pass the wall — if a repo-wide
analyzer fires on the bare project, that is a finding, not something to suppress; classify
per the deviation protocol).

- [x] **Step 5: Commit**

```bash
git add Directory.Packages.props tools/OpenCode.Sdk.Tools/OpenCode.Sdk.Tools.csproj OpenCode.slnx
git commit -m "build(tools): scaffold OpenCode.Sdk.Tools library project"
```

---

### Task 2: ToolApp composition root + fail-loud generate stub (TDD)

**Files:**
- Create: `tests/OpenCode.Sdk.Tools.Tests/OpenCode.Sdk.Tools.Tests.csproj`
- Create: `tests/OpenCode.Sdk.Tools.Tests/ToolAppTests.cs`
- Create: `tools/OpenCode.Sdk.Tools/ToolApp.cs`
- Create: `tools/OpenCode.Sdk.Tools/Commands/GenerateCommand.cs`
- Modify: `OpenCode.slnx`

**Interfaces:**
- Consumes: Task 1's library project.
- Produces (later slices depend on these exact shapes):
  - `static DependencyInjectionRegistrar ToolApp.CreateRegistrar(Action<IServiceCollection>? overrideServices = null)`
  - `static void ToolApp.Configure(IConfigurator configurator)`
  - `static Task<int> ToolApp.RunAsync(string[] args)` — called by the file-based entry.
  - `GenerateCommand` registered as `generate` (slice 3 replaces its body and adds
    `--verify` / `--update-fingerprints` settings).

API note: `CommandAppTester` (ctor taking `ITypeRegistrar`, `Configure`, `RunAsync`,
`CommandAppResult.ExitCode`/`.Output`) was verified against the 0.55.0 assembly. If the
pinned version's signatures differ in detail (e.g. an added `CancellationToken` on
`AsyncCommand.ExecuteAsync`), adapt in place — that is a level-0 deviation.

- [x] **Step 1: Create the test project**

`tests/OpenCode.Sdk.Tools.Tests/OpenCode.Sdk.Tools.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>$(DefaultTargetFramework)</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="TUnit"/>
    <PackageReference Include="Spectre.Console.Cli.Testing"/>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../tools/OpenCode.Sdk.Tools/OpenCode.Sdk.Tools.csproj"/>
  </ItemGroup>

</Project>
```

Add to `OpenCode.slnx` under the existing `/tests/` folder:

```xml
    <Project Path="tests/OpenCode.Sdk.Tools.Tests/OpenCode.Sdk.Tools.Tests.csproj" />
```

- [x] **Step 2: Write the failing tests**

`tests/OpenCode.Sdk.Tools.Tests/ToolAppTests.cs`:

```csharp
using Spectre.Console.Cli.Testing;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class ToolAppTests
{
    [Test]
    public async Task RunAsync_Should_Return_Nonzero_When_Command_Is_Unknown()
    {
        var tester = new CommandAppTester(ToolApp.CreateRegistrar());
        tester.Configure(ToolApp.Configure);

        var result = await tester.RunAsync("does-not-exist");

        await Assert.That(result.ExitCode).IsNotEqualTo(0);
    }

    [Test]
    public async Task RunAsync_Should_Fail_Loud_When_Generate_Is_Invoked()
    {
        var tester = new CommandAppTester(ToolApp.CreateRegistrar());
        tester.Configure(ToolApp.Configure);

        var result = await tester.RunAsync("generate");

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.Output).Contains("not implemented");
    }
}
```

(`OpenCode.Sdk.Tools.Tests` sits inside the `OpenCode.Sdk.Tools` namespace hierarchy, so
`ToolApp` resolves without a using directive — adding one would trip IDE0005.)

- [x] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/OpenCode.Sdk.Tools.Tests`
Expected: build FAILS with CS0246 (`ToolApp` not found) — the red state for scaffolding.

- [x] **Step 4: Implement `ToolApp`**

`tools/OpenCode.Sdk.Tools/ToolApp.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using OpenCode.Sdk.Tools.Commands;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Extensions.DependencyInjection;

namespace OpenCode.Sdk.Tools;

/// <summary>Composition root for the repo tool: DI registrar plus the command surface.</summary>
public static class ToolApp
{
    /// <summary>Builds the DI registrar; tests inject service overrides.</summary>
    public static DependencyInjectionRegistrar CreateRegistrar(
        Action<IServiceCollection>? overrideServices = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(AnsiConsole.Console);
        overrideServices?.Invoke(services);
        return new DependencyInjectionRegistrar(services);
    }

    /// <summary>Registers the command surface; shared by the app and <c>CommandAppTester</c>.</summary>
    public static void Configure(IConfigurator configurator)
    {
        ArgumentNullException.ThrowIfNull(configurator);
        configurator.SetApplicationName("opencode-tool");
        configurator.AddCommand<GenerateCommand>("generate")
            .WithDescription("Regenerate the SDK model layer from spec/openapi.json.");
    }

    /// <summary>Entry point used by tools/opencode-tool.cs.</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        using var registrar = CreateRegistrar();
        var app = new CommandApp(registrar);
        app.Configure(Configure);
        return await app.RunAsync(args).ConfigureAwait(false);
    }
}
```

`tools/OpenCode.Sdk.Tools/Commands/GenerateCommand.cs`:

```csharp
using Spectre.Console;
using Spectre.Console.Cli;

namespace OpenCode.Sdk.Tools.Commands;

/// <summary>Fail-loud stub; the generator pipeline replaces the body in a later slice.</summary>
public sealed class GenerateCommand : AsyncCommand
{
    private readonly IAnsiConsole _console;

    /// <summary>Creates the command; the console is injected so tests capture output.</summary>
    public GenerateCommand(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);
        _console = console;
    }

    /// <inheritdoc/>
    public override Task<int> ExecuteAsync(CommandContext context)
    {
        _console.MarkupLine(
            "[red]generate is not implemented yet[/] — the generator pipeline has not landed.");
        return Task.FromResult(1);
    }
}
```

- [x] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/OpenCode.Sdk.Tools.Tests`
Expected: PASS (both tests, net10.0 leg).

- [x] **Step 6: Full gate check**

Run: `dotnet build --configuration Release` then `dotnet format --verify-no-changes --no-restore`
Expected: both clean.

- [x] **Step 7: Commit**

```bash
git add tools/OpenCode.Sdk.Tools tests/OpenCode.Sdk.Tools.Tests OpenCode.slnx
git commit -m "feat(tools): ToolApp composition root with fail-loud generate stub"
```

---

### Task 3: File-based entry + executable bit (§3.3 items 1 and 3)

**Files:**
- Create: `tools/opencode-tool.cs`
- Modify: `OpenCode.slnx`

**Interfaces:**
- Consumes: `ToolApp.RunAsync(string[])`.
- Produces: the committed entry `tools/opencode-tool.cs` with index mode 100755; the
  pinned invocation forms (`dotnet run --file tools/opencode-tool.cs -- <args>` on any
  OS; `./tools/opencode-tool.cs <args>` on Unix).

- [ ] **Step 1: Write the entry**

`tools/opencode-tool.cs` — exactly this content, LF line endings, UTF-8 **without BOM**
(a BOM breaks the shebang):

```csharp
#!/usr/bin/env -S dotnet --
#:project ./OpenCode.Sdk.Tools/OpenCode.Sdk.Tools.csproj

return await OpenCode.Sdk.Tools.ToolApp.RunAsync(args).ConfigureAwait(false);
```

Add to the `/tools/` folder in `OpenCode.slnx`:

```xml
    <File Path="tools/opencode-tool.cs" />
```

- [ ] **Step 2: Run through the entry — §3.3 item 1 (strict-props build) + item 3 (invocation form)**

Run: `dotnet run --file tools/opencode-tool.cs -- generate`
Expected: exit code 1 and the stub message — proving the entry builds **clean under the
inherited strict props** (any analyzer/TWAE diagnostic fails this run) and that the
`--file` invocation form works from the repo root.

Run: `dotnet run --file tools/opencode-tool.cs -- --help`
Expected: exit 0, help output listing `generate`.

- [ ] **Step 3: Commit the executable bit into the index**

```bash
git add tools/opencode-tool.cs OpenCode.slnx
git update-index --chmod=+x tools/opencode-tool.cs
git ls-files -s tools/opencode-tool.cs
```

Expected: `git ls-files -s` prints mode `100755`. (The bit is meaningless on NTFS and
effective on the Linux CI checkout — generator spec §3.1.)

- [ ] **Step 4: Commit**

```bash
git commit -m "feat(tools): file-based entry dogfooding ToolApp"
```

---

### Task 4: Cache-staleness experiment (§3.3 item 2) + findings documentation

**Files:**
- Modify: `tools/OpenCode.Sdk.Tools/Commands/GenerateCommand.cs` (temporarily — reverted)
- Modify: `docs/research/00-research-log.md`

**Interfaces:**
- Consumes: Task 3's entry.
- Produces: the recorded §3.3 verdict (research log, question→finding→decision) and, if
  stale, the mitigation wired into CI/docs in Task 5.

- [ ] **Step 1: Baseline run**

Run: `dotnet run --file tools/opencode-tool.cs -- generate` — note the message text.

- [ ] **Step 2: Mutate the referenced library without an explicit build**

Edit the stub message in `GenerateCommand.cs` to append ` (cache-probe)`. Do **not** run
`dotnet build`. Re-run: `dotnet run --file tools/opencode-tool.cs -- generate`

- [ ] **Step 3: Read the verdict**

- Marker appears → `#:project` changes trigger rebuilds: **no mitigation needed**.
- Marker absent → the stale-tool hazard is real: **mitigation** = every documented/CI
  entry invocation is preceded by
  `dotnet build tools/OpenCode.Sdk.Tools/OpenCode.Sdk.Tools.csproj` (generator spec §3.3's
  routed-build option — a level-1 recorded fallback; Task 5's CI step gains that line). If
  the routed build *also* fails to refresh, the two-condition fallback (console-app
  promotion + one-line ADR-0003 correction) triggers — **stop; maintainer decision**
  (level 2).

- [ ] **Step 4: Revert the probe edit**

Revert `GenerateCommand.cs` to Task 2's exact content; re-run the entry once to confirm
the original message.

- [ ] **Step 5: Record the findings**

Append a session entry to `docs/research/00-research-log.md` (next session number,
`# Session N — <date>: slice 0 build-out — file-based entry verification`) with one
question block: **Q: Does the file-based entry survive the §3.3 verification list?** How
researched (the three checks above), found (per item: strict-props build verdict,
staleness verdict, invocation forms), decision (mitigation on/off; fallback not
triggered / triggered).

- [ ] **Step 6: Commit**

```bash
git add docs/research/00-research-log.md
git commit -m "docs(research): file-based entry verification results (slice 0)"
```

---

### Task 5: CI smoke step + final gates

**Files:**
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: the entry + executable bit (Task 3), the staleness verdict (Task 4).
- Produces: the Linux-leg smoke step slice 3 will upgrade to `generate --verify`.

- [ ] **Step 1: Add the smoke step**

After the `Verify formatting` step in `.github/workflows/ci.yml`:

```yaml
      # Smoke-runs the repo tooling entry; exercises the committed executable bit and the
      # shebang, which only take effect on a Linux checkout.
      - name: Tooling entry smoke
        if: runner.os == 'Linux'
        run: ./tools/opencode-tool.cs --help
```

If Task 4 recorded the staleness mitigation, prepend the routed build:

```yaml
        run: |
          dotnet build tools/OpenCode.Sdk.Tools/OpenCode.Sdk.Tools.csproj
          ./tools/opencode-tool.cs --help
```

- [ ] **Step 2: Local full-gate sweep**

Run, in order: `dotnet build --configuration Release`, `dotnet test --configuration Release --no-build`,
`dotnet format --verify-no-changes --no-restore`
Expected: all clean/green.

- [ ] **Step 3: Commit and push the branch**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: smoke the tooling entry on the Linux leg"
git push -u origin feature/slice-00-tooling-skeleton
```

- [ ] **Step 4: Verify CI**

Open the PR (`gh pr create`), watch the three legs; the Linux leg must run the smoke step
green. Slice exit: PR review + merge closes the slice issue.

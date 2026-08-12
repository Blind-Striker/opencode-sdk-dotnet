# M1 Walking Skeleton Implementation Plan

Date: 2026-08-11

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> `superpowers:subagent-driven-development` (recommended) or
> `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox
> (`- [ ]`) syntax for tracking.

**Goal:** Land a callable, pre-release SDK surface for `v2.health.get` and
`v2.session.message`, demonstrated once against a real `opencode serve`.

**Architecture:** `ISpecIngestion` remains the only OpenAPI boundary. A selection-scoped
Binder turns `SpecDocument` plus sparse curation into role-specific EmitPlan records; dumb
Roslyn emitters produce committed source, and generated methods delegate once into a
hand-written HTTP behavior core. Arc A lands the compiler/models; Arc B lands the client.

**Tech Stack:** .NET SDK 10.0.302, C# 14, Microsoft.OpenApi 3.9.0,
Microsoft.CodeAnalysis.CSharp 5.6.0, System.Text.Json 10.0.11, CliWrap 3.10.4, TUnit,
Verify.TUnit, Testably.Abstractions, and PublicApiGenerator 11.5.4.

## Global Constraints

- Binding decisions are `AGENTS.md` plus ADRs 0001-0009; design specs are reference only.
- Emit exactly the two selected modern operations. Pending operations are reported, not
  fingerprinted, and keep `dotnet pack` blocked by an owned `.generation-incomplete` marker.
- Clients, options, responses, routes, and exceptions use `OpenCode.Sdk`; generated domain and
  error models use `OpenCode.Sdk.Models`; serializer infrastructure is internal.
- The first payload members are `SessionMessageResponse.Message` and
  `HealthResponse.Healthy`; health's one-value boolean enum emits as `bool`.
- Every marked union has a position-independent converter and explicit `Unknown*` carrier with
  its tag and a cloned raw `JsonElement`; serialization uses only generated metadata.
- Generated output is deterministic, LF-only, manifest-owned, analyzer-visible plain `.cs`.
- Product code targets `netstandard2.0;net472;net8.0;net9.0;net10.0` and uses
  `ConfigureAwait(false)`. Tests obey `docs/engineering/testing-style.md`.
- Exclude retry, telemetry, hooks, DI Extensions, launcher, SSE, process harness, coverage/CI
  legs, hashes, fingerprints, full curation, and unselected operation support.

## File Map

- Inputs: `tools/generation-profile.txt`, `tools/curation.json`.
- Compiler: `tools/OpenCode.Sdk.Tools/Generator/{Binding,Emission,Output}/` and
  `Generator/GenerationCoordinator.cs`.
- Product: generated `src/OpenCode.Sdk/{Models,Serialization,Internal}/` plus root clients and
  responses; hand-written root spines plus `Internal/Pipeline.cs`.
- Tests: matching `Generator/` areas in Tools.Tests and
  `{Serialization,Contract,Support,Fixtures}/` in Sdk.Tests.

---

## Arc A - Selected Compiler And Committed Models

Use `feature/m1-compiler-models`; each task is one reviewable commit. Merge Arc A before Arc B.

### Task 1: Selection, Curation, And Binder

**Files:** Create the two input files; `Binding/Abstractions/ISpecBinder.cs`;
`Binding/{SpecBinder,OperationSelectionLoader,CurationLoader,ReachableSchemaCollector,
CSharpNamePolicy}.cs`; role records under `Binding/Models/`; matching loader/Binder tests.

**Interface:** `ISpecBinder.Bind(SpecDocument, OperationSelection, GenerationCuration)` returns
`EmitPlan` containing only model, marked-union, registry, and pending-operation plans.

- [ ] Red-test missing/duplicate IDs, unknown config fields, orphan/pending curation rows,
  selected coverage gaps, batched errors, closure, duplicate-ref collapse, and determinism.
- [ ] Check in the exact two-operation profile and sparse curation: root `health`; `Sessions` +
  `SessionClient`; payload `Message`; reasoned `Uri` overrides for
  `PromptFileAttachment.uri` and `ToolFileContent.uri`.
- [ ] Bind only the selected closure, require every reached union to be marked, apply FDG names
  (`Llm*`), and derive pending modern/legacy sets without full-pin count assertions.
- [ ] Run `dotnet test tests/OpenCode.Sdk.Tools.Tests/OpenCode.Sdk.Tools.Tests.csproj
  --configuration Release`; commit `feat(generator): bind the M1 operation selection`.

### Task 2: Model, Union, And Registry Emitters

**Files:** Run `dotnet add tools/OpenCode.Sdk.Tools/OpenCode.Sdk.Tools.csproj package
Microsoft.CodeAnalysis.CSharp --version 5.6.0`; create
`Emission/{GeneratedSource,SourceEmitter,ModelEmitter,UnionEmitter,RegistryEmitter}.cs` and
matching tests with one Verify micro-snapshot per emitter family.

**Interface:** `SourceEmitter.Emit(EmitPlan)` returns an ordinal-sorted
`IReadOnlyList<GeneratedSource>`; each item carries a manifest-relative path and UTF-8 source.
Emitters consume no SpecIR, curation, or filesystem.

- [ ] Red-test immutable records, `required`, empty optional collections, wire names, XML docs,
  error inheritance, marked dispatch, unknown round-trip, and registry membership.
- [ ] Emit Roslyn trees for models, `OpenCodeError`, concrete errors, union bases/variants,
  `Unknown*` carriers, converters, and internal `OpenCodeJsonContext`.
- [ ] Compile emitted snippets, approve only the three micro-snapshots, run
  `dotnet test tests/OpenCode.Sdk.Tools.Tests/OpenCode.Sdk.Tools.Tests.csproj --configuration
  Release`, and commit `feat(generator): emit selected models and unions`.

### Task 3: Writer, Generate Command, And Arc A Gate

**Files:** Run `dotnet add src/OpenCode.Sdk/OpenCode.Sdk.csproj package System.Text.Json
--version 10.0.11` and `dotnet add tools/OpenCode.Sdk.Tools/OpenCode.Sdk.Tools.csproj package
CliWrap --version 3.10.4`; create `Generator/{GenerationCoordinator,GenerationRequest,
GenerationReport}.cs`,
`Output/Abstractions/{IGenerationWriter,IProjectFormatter}.cs`, and
`Output/{GenerationWriter,CliWrapProjectFormatter,GenerationManifest,WriteResult}.cs`; modify
`GenerateCommand.cs`, `ToolApp.cs`, and `OpenCode.Sdk.csproj`; add product serialization tests
and embedded fixtures; remove `SmokeTests.cs` after real tests cover each test TFM.

- [ ] Unit-test manifest path safety, stale cleanup, unrelated-file preservation, repeat output,
  clean/drift `--verify`, report/marker behavior, and command exits using MockFileSystem and a
  substituted formatter.
- [ ] Implement write-then-format ownership. `--verify` snapshots old/new owned paths, writes,
  formats the SDK project, byte-compares owned output, and reports changed/created/deleted paths
  without Git.
- [ ] Generate and commit `Models/`, internal converters/context, `.generated-manifest.json`,
  and `.generation-incomplete`; product-test known/unknown outer, assistant-content, and
  tool-state variants, arbitrary marker order, semantic round-trip, and context-only metadata.
- [ ] Run `dotnet run --file tools/opencode-tool.cs -- generate`, repeat with `--verify`, then
  run Release build/tests and format. Run
  `dotnet pack src/OpenCode.Sdk/OpenCode.Sdk.csproj --configuration Release` separately and
  require failure solely from the partial marker.
- [ ] Commit `feat(generator): commit the M1 model closure`; open and merge the Arc A PR.

---

## Arc B - Callable Client

Use `feature/m1-callable-client` from merged Arc A.

### Task 4: Hand-Written Behavior Core

**Files:** Create root `{OpenCodeClientOptions,OpenCodeRequestOptions,ErrorBehavior,
OpenCodeResponse,OpenCodeException,OpenCodeApiException,OpenCodeTransportException}.cs`;
`Internal/Abstractions/IEnvironmentProvider.cs`;
`Internal/{SystemEnvironmentProvider,Pipeline,ResponseAdapter}.cs`; SDK friend assembly metadata;
recording-handler/environment test support and focused runtime tests.

**Interface:** `Pipeline.ExecuteAsync<TResponse>(HttpMethod, string,
ResponseAdapter<TResponse>, OpenCodeRequestOptions?, CancellationToken)` returns
`Task<TResponse>` where `TResponse : OpenCodeResponse`. Generated adapters own typed success/error
deserialization and envelope construction; Pipeline owns transport and throw/`NoThrow` behavior.

- [ ] Red-test endpoint joining; explicit password then one-time
  `OPENCODE_SERVER_PASSWORD` fallback with Basic user `opencode`; directory precedence;
  `x-opencode-directory`; `OpenCode.Sdk/<informational-version>` User-Agent; cancellation;
  transport/protocol errors; response/HttpClient disposal.
- [ ] Implement only those behaviors. Preserve `OperationCanceledException`; expose status,
  typed `OpenCodeError`, and raw body on `OpenCodeApiException`; never dispose BYO HttpClient.
  Options expose `Endpoint`, `Password`, and client `Directory`; request options expose
  `ErrorBehavior` (`Default`/`NoThrow`), per-call `Directory`, and static `NoThrow`;
  `OpenCodeResponse` exposes `Status`, `IsError`, and `Error`.
- [ ] Run `dotnet test tests/OpenCode.Sdk.Tests/OpenCode.Sdk.Tests.csproj --configuration
  Release`; commit
  `feat(sdk): add the minimal HTTP behavior core`.

### Task 5: Client Emission, Contracts, And M1 Closure

**Files:** Add consumed-only `{EnvelopePlan,ClientPlan,OperationPlan,ErrorMapPlan}` binding records;
create `Emission/{EnvelopeEmitter,RoutesEmitter,ClientEmitter,OperationMethodEmitter,
ResponseAdapterEmitter}.cs`; generate root clients/routes/responses and two internal adapters;
run `dotnet add tests/OpenCode.Sdk.Tests/OpenCode.Sdk.Tests.csproj package PublicApiGenerator
--version 11.5.4`; add `Contract/{HealthContract,
SessionMessageContract}Tests.cs`, API approval, recording support, and embedded payload fixtures.

**Public surface:** `OpenCodeClient` implements `IDisposable`, has `(Uri)`,
`(Uri, OpenCodeClientOptions)`, `(HttpClient, OpenCodeClientOptions)`, and protected mock
constructors, readonly `Sessions`, and `GetHealthAsync(options, cancellationToken)`;
`SessionsClient.GetSessionClient(sessionId)` returns `SessionClient`, whose
`GetMessageAsync(messageId, options, cancellationToken)` returns `SessionMessageResponse`.

- [ ] Emit final clients, guarded/escaped IDs and routes, guarded `Healthy`/`Message` getters and
  `PrintMembers`, one-expression method delegation, and status maps: health 400/401; message
  400/401/404 with both deduplicated not-found variants and `UnknownOpenCodeError` fallback.
- [ ] Contract-test every selected success/declared error through default and `NoThrow`; test
  unknown retention, path escaping, auth/directory/User-Agent, cancellation, transport failure,
  and client ownership through the real generated client.
- [ ] Approve the first API baseline; run `generate --verify`, Release build/tests, format, and
  Slopwatch. Keep the partial marker and pack refusal.
- [ ] Start `OPENCODE_SERVER_PASSWORD=m1-demo opencode serve --port 4096` in a directory with an
  existing message; obtain IDs from `GET /api/session?limit=1` and
  `GET /api/session/{sessionID}/message?limit=1`; use a temporary `.scratchpad/` console to print
  `Healthy`, concrete message type, and message ID; paste output into the PR.
- [ ] Update `docs/ROADMAP.md`, commit `feat(sdk): generate the first callable client`, and open
  the Arc B PR.

## Final Gates And Stop Conditions

Run `dotnet build --configuration Release`, `dotnet test --configuration Release --no-build`,
`dotnet format --verify-no-changes --no-restore`, and `dotnet tool run slopwatch analyze
--exclude ".scratchpad/**,external/**" --fail-on warning`. Stop for maintainer review if the
closure reaches a structural union, Binder needs Microsoft.OpenApi/raw JSON, a temporary public
or transport path would later be replaced, output cannot compile uniformly on all five TFMs,
partial breadth can pack, or excluded M1 capabilities become necessary for either call.

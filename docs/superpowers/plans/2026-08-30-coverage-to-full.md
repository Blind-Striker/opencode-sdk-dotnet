# Coverage to Full — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use deniz-process:subagent-driven-development
> (recommended) or deniz-process:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the pending map at accepted snapshot `b1e3a7b2` (profile 122 selected / 12 pending
/ 2 transport-owned of 136). Nine of the twelve pending operations are admitted — two through
curation rows alone (`vcs.base`, `fs.list`), seven through three generator mechanisms decided per
mechanism (query-parameter shapes for `fs.find`/`vcs.diff`/`session.stats`; location-envelope
inline `data` promotion for `shell.output`; the `{name, data}` error style for
`worktree.create`/`refresh`/`remove`). The remaining three stay pending behind recorded standing
walls (`config.get`, `fs.read`, `experimental.migration.v1.status`). The approved tools touch
rides the first mechanism that reaches `tools/` (Proposal 1 stabilize-duplicate collapse,
Proposal 2 source watch, the `ToolJsonContext` `NewLine` pin, the single-key envelope facet, and a
telltale-completeness rider). The hygiene batch lands as one dispatch with one review, the
`PersistentPtySession` benchmark rung is added, and the session hands off with the release-prep
proposals the maintainer must decide.

**Architecture:** The pipeline is unchanged — SpecIR → binding plans → Roslyn emitters →
committed source under `src/OpenCode.Sdk`, hand-written behavior core, System.Text.Json source
generation. Every mechanism here is a fail-closed *widening* of one binder or emitter: the query
facet binder learns required, non-nullable, and enum-valued parameters; the envelope facet binder
and name resolver learn the location wrapper's single-object `data`; the error-union binder and the
error converter learn the `{name, data}` error dialect beside `_tag`; the envelope classifier learns
a single-required-key bare object. Nothing family-specific: each rule keys on shape, never on an
operation id (ADR-0013, ADR-0014, ADR-0016, ADR-0017).

**Tech stack:** .NET multi-TFM (`netstandard2.0;net472;net8.0;net9.0;net10.0`); the repository
generator under `tools/` (file-based app `tools/opencode-tool.cs` over
`tools/OpenCode.Sdk.Tools`); TUnit on Microsoft.Testing.Platform; PublicApiGenerator baseline;
BenchmarkDotNet performance project.

**Spec / authority:** the maintainer's session directive of 2026-08-30 (goals in order: coverage
to full grouped by mechanism, the tools touch, the hygiene batch as one dispatch, release prep only
after the twelve are closed, the benchmark rung); `docs/agents/handover-prompts/HANDOFF-2026-08-30.md`
(the hygiene list with file:line, the tools-touch items, standing facts); `docs/ROADMAP.md`
§Known Gaps "Approved generator/tooling mechanisms" (Proposal 1's seven guardrails and Proposal
2's file sets, verbatim); canon `docs/architecture/protocol-and-generation.md` and
`client-runtime.md`; ADR-0004, ADR-0007, ADR-0013, ADR-0014, ADR-0016, ADR-0017, ADR-0019,
ADR-0020, ADR-0021; `docs/engineering/coding-style.md`, `testing-style.md`, `quality-gates.md`.
The controller rulings below are provisional maintainer decisions taken under the autonomous
session mandate; each is recorded in the SDD ledger with its cost if wrong and surfaces in the
handoff for the maintainer to confirm or reverse.

## Global Constraints

- **Completion gate per task** (`docs/engineering/quality-gates.md`), from the repository root,
  every command in the foreground with a generous timeout (never a backgrounded `dotnet` run):

  ```bash
  dotnet tool run slopwatch analyze --exclude ".scratchpad/**,external/**" --fail-on warning
  dotnet build --configuration Release
  dotnet format whitespace --verify-no-changes --no-restore
  dotnet format style --verify-no-changes --no-restore --severity warn
  dotnet test --configuration Release --no-build
  ```

  Tasks that touch the generator, curation, the profile, or generated output add:

  ```bash
  dotnet run --file tools/opencode-tool.cs -- --help
  dotnet run --file tools/opencode-tool.cs -- generate --verify
  ```

  Tasks that touch `refresh-spec`, the receipt, or `spec/` add
  `dotnet run --file tools/opencode-tool.cs -- refresh-spec --verify`.

- **Slopwatch stays at zero.** No suppressions, no `NoWarn`, no `Skip`, no empty or comment-only
  catch, no `#pragma warning disable`. The analyzer wall is fail-closed: fix the code, never the
  rule; a genuine misfire takes the narrowly scoped per-rule arbitration comment pattern
  `.editorconfig` records, naming the winning rule or contract. Slopwatch does not consult
  `.gitignore` and is blind inside a git worktree — run it from this main checkout.
- **Generated output changes only through the generator.** Edit `tools/curation.json`,
  `tools/generation-profile.txt`, or the emitters; run `generate`; review the regenerated diff as
  source; commit it. Never hand-edit anything the manifest owns. `generate --verify` must report
  "Generated output is current" at every commit.
- **PublicApi baseline** (`tests/OpenCode.Sdk.Tests/Snapshots/PublicApi.verified.txt`) is
  regenerated and reviewed as a diff. Additive changes are expected; renames are free pre-1.0
  (research log Q131) but every removed or renamed public member is named in the task report.
- **Curation rows carry reasons that state facts true today** — present tense, no "ever", no
  forward-looking claims; a reason that cites a collision must cite a real one. Existing rows whose
  facts a task falsifies are corrected in the same commit.
- **Canonical documents are not edited by this plan** (`AGENTS.md`, `CONTEXT.md`,
  `docs/architecture/**`, `docs/engineering/**`, `docs/adr/**`, `spec/SNAPSHOT.md`). When a task
  finds a canon sentence it would need to add or change, it writes the exact proposed text under a
  "Canon sentence proposed" heading in its report and continues; the controller collects the
  proposals for the maintainer. Operational documents (`docs/ROADMAP.md`, the research log
  `docs/research/00-research-log.md`, the active handoff) are updated in the same commit as the
  code that changes their facts. If implementation would *contradict* current canon, stop and
  report BLOCKED citing the sentence (`docs/agents/deviation-protocol.md`).
- **Tests follow `docs/engineering/testing-style.md`:** wire fixtures live under the owning test
  project's `Fixtures/` and load by name; no inline JSON dumps; typed builders for variation
  families; `{Symbol}_Should_{Expected_Behavior}[_When_{Condition}]`; one test class per file
  mirroring the SUT's folder. A test that starts a real server process takes
  `ParallelConstraintKeys.ServerProcess` (`tests/Shared/ParallelConstraintKeys.cs`); a test whose
  assertion depends on a wall-clock bound takes keyless `[NotInParallel]` (research log Q157).
- **Contract-test floor for every admitted operation** (the sealed no-wall-sweep floor): typed
  success materialized from a recorded wire shape matching the pinned schema, one declared error
  arm, and the `NoThrow` channel — in the family's existing `*ClientContractTests` file or a new
  one mirroring that idiom. A represented-nullable payload additionally proves the `null` arm.
- **Upstream submodule `external/opencode` is read-only evidence.** Never edit it; never move its
  checkout.
- **Commits:** Conventional Commits (`feat`, `fix`, `test`, `refactor`, `docs`, `perf`, `chore`);
  no AI-attribution trailers; never push; never rebase or amend a commit that already exists.
  One task may make several commits; each commit passes the full gate.
- **Implementers do not dispatch subagents** and do not run reviewers; the controller reviews
  every task from its diff. Reports go to the report file named in the dispatch; the final message
  is the short status contract.
- **Naming traps:** the location query member is `workspace` (bodies use `workspaceID`); the
  emitted vcs method is `GetBranchesAsync`, not `ListBranchesAsync`; `OperationNamePolicy` falls
  back to the HTTP method as the verb (`refresh` → `Post…`), so check what the emitter actually
  named a member before writing a test against it.

## Controller rulings (2026-08-30) — provisional maintainer decisions

- **R1 — `config.get` is not curation-only and stays pending.** The telltale showed one wall (the
  `ConfigModelCost` collision) because the probe reports `Errors[0]`; binding it with the real
  binder yields ten errors, three of which are ADR-0016 same-token-kind walls inside
  `Config.InfoEncoded` (`lsp`'s map value `anyOf[{disabled:true}, Config.LSP.ServerEncoded]`,
  `references`' map value `anyOf[string, Git, Local]`) — the same wall class the maintainer let
  stand for `migration.v1.status` on 2026-08-28. Per mechanism, not per operation: the wall
  stands; `config.get` is recorded beside `migration.v1.status`. Cost if wrong: one curation-plus-
  mechanism task later. The two curation-fixable collisions (`ConfigModelCost`, `IMcp`) are recorded
  for that day, not landed now (rows over an unselected operation would be unvalidated).
- **R2 — `fs.read`'s wildcard wall stands**; this plan drafts the upstream report and parks it.
- **R3 — `migration.v1.status` stays behind its wall** (maintainer, 2026-08-28). Not reopened.
- **R4 — Query-parameter mechanism design.** (a) A *required* query parameter emits a C# `required`
  non-nullable request property; an operation whose query request carries any required member
  takes `request` as a required, non-nullable method and route parameter (the shape body-bearing
  operations already use). (b) An *optional* parameter whose schema does not admit JSON null binds
  exactly like one that does: the C# property is nullable and an unset property is omitted from
  the wire — the null-admission wall is dialect ceremony with no representational consequence and
  is dropped. (c) An enum-valued query parameter binds to a generated C# enum: a component `$ref`
  enum reuses that component's model (`Vcs.Mode` → `VcsMode`); an inline enum is promoted into the
  graph under the operation-scoped key its ingestion siblings use and named mechanically
  `{RequestTypeName}{PropertyName}` (`SessionStatsRequestTools`, `FsFindRequestType`) — a
  `schemaNames` row may override with a reason. The wire value is the enum member's
  `JsonStringEnumMemberName`, emitted as a generated switch (no reflection, AOT-safe) that the
  route builder passes to `QueryStringBuilder.AddText`; `ListOrder`/`QueryBoolean` keep their
  spine kinds. Reachability includes query enum schemas so the models emit. Cost if wrong: a
  reviewer or the maintainer prefers string-typed enums; the change is localized to one kind.
- **R5 — Location-envelope single-object `data` promotion** mirrors the Data shape's promotion:
  the name resolver claims the promoted `data` key under `OperationNamePolicy.PayloadTypeName`
  (`{stem}Data`), the envelope binder accepts the claimed promoted key, kind stays
  `DataLocation`. Cost if wrong: a naming row.
- **R6 — The `{name, data}` error style is admitted beside `_tag`.** ADR-0007 requires "tagged
  error payloads … generated as typed models under an `OpenCodeError` base … pattern-matchable
  without string sniffing"; a `name`-literal-tagged error satisfies it, and the `_tag`-only wall
  is a code-level "M1" restriction canon never states (no `_tag` sentence exists in
  `docs/architecture`, `docs/engineering`, `CONTEXT.md`; ADR-0004 only cites `_tag` as a wire
  fidelity example). The record: `IOpenCodeError.Tag` stays the union marker (for a `NameData`
  error it is the `name` literal), the record carries `Data` as a nested generated model, and the
  converter dispatches on `_tag` first, then `name`. No canon edit; research log and ROADMAP
  record the widening. Cost if wrong: the maintainer wants a distinct base for the second dialect —
  a rename.
- **R7 — Single-key envelope facet** applies to every bare success body that is an inline object
  with exactly one required property whose value is nominal or represented-nullable: today
  `persistentPty.handoff` (`{handoff}`) and `server.get` (`{urls}`). Both flatten; the `server.get`
  rename is a pre-1.0 surface change named in the report. Cost if wrong: `server.get` is reverted
  through a curation opt-out row the task defines.
- **R8 — Canon sentences are proposed, not applied** (Global Constraints). Cost if wrong: one
  follow-up docs commit per approved sentence.
- **R9 — Branch and merge.** Work lands on `feature/coverage-to-full`; after a clean final
  whole-branch review the controller fast-forwards `master` (unpushed, reversible via reflog) and
  deletes the branch, following the persistent-PTY arc's precedent. Cost if wrong: `git reset`.
- **R10 — Hygiene batch order:** after the coverage and tools tasks (the maintainer allowed "after
  (1)"); the decoder's null-role item comes forward only if an earlier task touches the decoder —
  none does.
- **R11 — Telltale completeness rider:** the bindability probe lists every independent wall, not
  `Errors[0]`; rides the first tools task.
- **R12 — Benchmarks:** add the `PersistentPtySession` rung and run `--job short` over the two PTY
  ladders as the increment check; the full default-job run is milestone evidence the maintainer
  schedules on a quiet machine.

## Mechanism facts the tasks argue from (verified 2026-08-30 at `b1e3a7b2`)

- **Marker (`src/OpenCode.Sdk/.generation-incomplete`)**: `v2.vcs.base [bindable]`; every other
  pending line carries its first wall. Binding `vcs.base`+`config.get`+`fs.list` with the real
  binder (temporary profile edit, reverted) produced ten errors: `Curation [config]`/`[fs]`
  "selected operation group has no curation row"; `Naming [Config.ModelEncoded#/properties/cost]`
  `ConfigModelCost` collides with `Config.Model.CostEncoded` (the promoted `anyOf[Cost, Cost[]]`
  union vs the `Encoded`-stripped component); `Naming [op:v2.mcp.add#/…/properties/config]`
  `IMcp` collides with `Config.InfoEncoded#/properties/mcp/properties/servers/additionalProperties`
  (structurally identical `anyOf[Mcp.LocalConfigEncoded, Mcp.RemoteConfigEncoded]` unions — a
  `schemaAliases` candidate); three ADR-0016 structural walls in `Config.InfoEncoded` (`lsp`,
  `references`); two consequential "inline nominal schema was not promoted" errors and one
  "merged request body model is absent". `vcs.base` produced no error.
- **`OperationNamePolicy`** (`tools/OpenCode.Sdk.Tools/Generator/Binding/OperationNamePolicy.cs`):
  verb = a known final segment (`create|get|list|remove|rename|timeout`) else the HTTP method;
  subject = segments between group and verb; an empty subject falls back to the group, pluralized
  naively for `list` (words ending in `s/x/z/ch/sh` or consonant+`y` return null → the operation
  refuses "the operation's names cannot be derived mechanically"); `ResponseTypeName` =
  `{Group}{Subject}{Verb unless Get}Response`; `RequestTypeName` likewise with `Request`;
  `PayloadTypeName` = response stem + `Data`; `PayloadName` = subject or group fallback. So
  `fs.list` needs both an `operationNames` row and an `envelopePayloadNames` row (`Pluralize("Fs")`
  is null); `vcs.base` derives `GetBaseAsync`, `VcsBaseResponse`, payload `Base`; `worktree.refresh`
  derives `PostRefreshAsync` (needs a row); `shell.output` derives `GetOutputAsync`,
  `ShellOutputResponse`, payload `Output`, promoted type `ShellOutputData`.
- **Query binding** (`QueryRequestFacetBinder.cs`): refuses required parameters ("must be
  optional"), non-`NullableNode` schemas ("must admit null"), and any inner shape other than plain
  string / `["asc","desc"]` / `["true","false"]` / the parent-filter union ("unsupported schema
  shape"); the cursor-pagination trio derives from `ListRequest`. `QueryValueKind` (Text,
  ListOrder, BooleanText, SessionParentFilter, Location) drives `QueryRequestEmitter.EmitPropertyType`
  and `RoutesEmitter.QueryAddMethod`; `src/OpenCode.Sdk/Internal/QueryStringBuilder.cs` owns the
  per-kind `Add*` methods and omits null. `ReachableSchemaCollector.cs:19-25` deliberately skips
  query parameter schemas ("never generate models"). Generated query-only operations take
  `TRequest? request = null`; body-bearing ones take `TRequest request`
  (`ShellClient.TimeoutShellAsync`). `OperationPlanBinder.cs:364` inspects query kinds.
- **The three query operations:** `fs.find` — `query` required plain string; `type` optional
  non-nullable inline enum `[file, directory]`; `limit` optional nullable string; location. `vcs.diff`
  — `mode` required `$ref Vcs.Mode` (`[working, branch, committed]`); `base` optional
  non-nullable string; `context` optional nullable string; location; 200 `{location, data:
  FileDiff.Info[]}`; errors 400/401/503. `session.stats` — `from|to|project|timezone` optional
  nullable strings; `tools` optional nullable inline enum `[none, summary, detail]`; 200 Data
  envelope `{data: SessionStats.Info}`; errors 400 (`anyOf[InvalidRequestError ×2]`, already
  collapsed by `UnionNormalizer`)/401. `shell.output`'s `cursor`/`limit` are optional non-nullable
  strings with a numeric `pattern` — mechanism (b) covers them; patterns are never validated
  client-side (ADR-0014).
- **Location envelope** (`EnvelopeFacetBinder.BindDataLocationPayload`): named component `data`,
  array-of-named-item `data`, and ref-to-array `data` (C2's `vcs.branches` arm) bind; a promoted
  inline object `data` (`RefNode` whose target contains `#`) refuses "location envelope 'data'
  must reference a named component schema, or be an array of one".
  `SchemaNameResolver.ResolveEnvelopePayloadRootNames` claims promoted payload keys under
  `PayloadTypeName` for Bare roots, Data members, and DataLocation *list items*
  (`ResolveDataLocationListItemKey`) — the DataLocation single-object arm is documented as
  "stays nominal". `shell.output`'s 200 is `{location, data: {output: string, cursor: integer,
  size: integer, truncated: boolean}}` (all required); 404 `ShellNotFoundError` already generated.
- **Error style:** `Ingestion/Projection/ErrorStyleClassifier.cs` already classifies
  `ErrorStyle.NameData` (required literal `name` + required `data`).
  `SchemaPlanBinder.BindErrorUnion` refuses when the closure's styles are not exactly
  `[EffectTag]` ("selected errors must use the Effect _tag style in M1") and reads the `_tag`
  literal marker per variant. The generated `OpenCodeErrorJsonConverter` dispatches through
  `UnionDiscriminatorReader.TryFindKnown(ref reader, "_tag", …)` over a frozen `TypesByTag`
  map and falls to `UnknownOpenCodeError(marker, payload)`; `IOpenCodeError` exposes
  `[JsonPropertyName("_tag")] string Tag`; `Internal/OpenCodeErrorReader` filters by the
  per-status allowed-tag arrays and builds `OpenCodeApiException` from `error.Tag`.
  `WorktreeErrorEncoded` = `{name: "WorktreeError", data: {message: string, forceRequired?:
  boolean|null}}`; upstream declares it in `packages/protocol/src/groups/worktree.ts:8-17`
  (`Schema.Error` with a `name` literal and a `data` struct, `httpApiStatus: 400`) — a projection of
  `packages/core/src/git.ts:64`'s `Schema.TaggedError("Git.WorktreeError")`. The three worktree
  operations: `create` POST `/api/worktree/{projectID}` body `{strategy, directory (required),
  from?, branch?, name?}` → 200 bare `Worktree.Info`, 400 `anyOf[WorktreeError,
  InvalidRequestError]`, 401; `remove` POST body → 204; `refresh` POST no body → 204. Group row
  `worktree` exists (`Worktrees` / `ProjectWorktreesClient` on `projectID`).
- **Single-key bare objects** (scan of every inline 200 object with exactly one required
  property): `v2.server.get` `{urls}` (selected today) and `persistentPty.handoff` `{handoff:
  PersistentPty.Handoff | null}` (selected; currently binds as the promoted body model
  `PersistentPtyHandoffPostData`, so callers read `response.Handoff.Handoff`).
  `Ingestion/Projection/EnvelopeClassifier.cs` classifies by property-name sets (`data`,
  `data+location`, `cursor+data`, `data+hasMore`) and returns `Bare` otherwise.
- **Telltale probe** (`Binding/PendingOperationBindabilityProbe.cs`): binds each pending operation
  alone under a synthetic root-placed group row and records `exception.Errors[0].Problem` only.
- **`ToolJsonContext`** (`tools/OpenCode.Sdk.Tools/Serialization/ToolJsonContext.cs`) sets
  `WriteIndented = true` without `NewLine`, so on Windows `spec/receipt.json` and
  `src/OpenCode.Sdk/.generated-manifest.json` are written CRLF with a bare-LF tail (`git ls-files
  --eol` → `w/mixed`); `eol=lf` normalizes the committed blob (research log Q155).
- **Proposal 1 and 2 specifications** are the ROADMAP §Known Gaps bullet "Approved
  generator/tooling mechanisms (maintainer, 2026-08-29)" — read it verbatim; it lists Proposal 1's
  guardrails (`DeepEquals` or refuse naming both, fixpoint, never chains, manifest telltale,
  explicit rows kept for non-`_N` cases, loud worst case, retires 24 of 25 `schemaAliases` rows) and
  Proposal 2's two watched-file sets.
- **Hygiene list:** `HANDOFF-2026-08-30.md` §"1. The hygiene batch" — 28 items with file:line
  across Generator (2), Doors (2), Core (5), Session and decoder (7), Fixture (3), Live and sandbox
  (5), Docs (4), plus the sandbox `--no-launch-profile`/provider-less-500 item. Two of the docs
  items touch `client-runtime.md` (canon) → proposal only.
- **Gate baseline:** 4,016 tests green on all TFMs at `6319d78`; slopwatch 0; `generate --verify`
  current; the pinned server builds from `external/opencode` at `b1e3a7b2`.

## Names this plan fixes

| Concept | Name |
|---|---|
| `fs` group client | `FileSystem` (placement client, flat: `list`, `find`, `read` carry no per-id operation) |
| `fs.list` | `FileSystemClient.ListEntriesAsync(FsListRequest? …)` → `FsListResponse.Entries` (`IReadOnlyList<FileSystemEntry>`) + `Location` |
| `fs.find` | `FileSystemClient.FindEntriesAsync(FsFindRequest request)` → `FsFindResponse.Entries`; `FsFindRequest { required string Query; FsFindRequestType? Type; string? Limit; LocationSelector? Location }` |
| `vcs.base` | `VcsClient.GetBaseAsync(VcsBaseRequest? …)` → `VcsBaseResponse.Base` (`VcsBase?`, represented-nullable) + `Location` |
| `vcs.diff` | `VcsClient.GetDiffAsync(VcsDiffRequest request)` → `VcsDiffResponse.Diffs` (`IReadOnlyList<FileDiffInfo>`); `VcsDiffRequest { required VcsMode Mode; string? Base; string? Context; LocationSelector? Location }` |
| `session.stats` | `SessionsClient.GetStatsAsync(SessionStatsRequest? …)` → `SessionStatsResponse.Stats` (`SessionStatsInfo`); `SessionStatsRequest { string? From; string? To; string? Project; string? Timezone; SessionStatsRequestTools? Tools }` |
| `shell.output` | `ShellClient.GetOutputAsync(ShellOutputRequest? …)` → `ShellOutputResponse.Output` (`ShellOutputData { Output, Cursor, Size, Truncated }`) + `Location` |
| `worktree.create` | `ProjectWorktreesClient.CreateWorktreeAsync(WorktreeCreateRequest request)` → `WorktreeCreateResponse.Worktree` (`WorktreeInfo`) |
| `worktree.remove` / `refresh` | `RemoveWorktreeAsync(WorktreeRemoveRequest request)` / `RefreshWorktreesAsync(…)` (row), both 204 no-content responses |
| The `{name, data}` error | `WorktreeError : IOpenCodeError { Tag => "WorktreeError" (wire `name`); WorktreeErrorData Data }`, `WorktreeErrorData { string Message; bool? ForceRequired }` |
| Query enums | `VcsMode` (component), `FsFindRequestType`, `SessionStatsRequestTools` |
| New query kind | `QueryValueKind.Enum` carrying the enum type name |
| Single-key facet | `SpecEnvelopeShape.SingleKey` → `EnvelopeKind.Data`-style flattening; `PersistentPtyHandoffResponse.Handoff` becomes `PersistentPtyHandoff?`; `ServerResponse.Urls` |
| Proposal 1 | `Binding/StabilizeDuplicatePolicy`; manifest section `implicitAliases` |
| Proposal 2 | `spec/source-watch.json`; receipt member `watchedSources` |
| Telltale | `[refused: <wall>; <wall>; …]` — every independent wall, binder order, deduplicated |
| Branch | `feature/coverage-to-full` |

---

### Task 1: Curation-only admissions — `vcs.base` and the filesystem family's `fs.list`

**Where it fits:** proves the pending→selected flow end to end at the new pin before any
mechanism work; two families, two commits, no generator change.

**Files:**
- Modify: `tools/generation-profile.txt` (+`v2.vcs.base`, +`v2.fs.list`)
- Modify: `tools/curation.json` (`groups.fs`, `operationNames` row for `v2.fs.list`,
  `envelopePayloadNames["v2.fs.list"]`)
- Regenerated: `src/OpenCode.Sdk/**` (new `FileSystem/` family folder, `Vcs/VcsBaseResponse.cs`,
  models `VcsBase`, `FileSystemEntry`, routes, adapters, serializer context, manifest, marker)
- Tests: `tests/OpenCode.Sdk.Tests/Vcs/VcsClientContractTests.cs` (extend), new
  `tests/OpenCode.Sdk.Tests/FileSystem/FileSystemClientContractTests.cs`, fixtures under
  `tests/OpenCode.Sdk.Tests/Fixtures/` (or the family's existing wire-data home), PublicApi
  baseline
- Modify: `docs/ROADMAP.md` (profile counts 124/10/2 and the Status sentence for this arc's first
  admissions)

**Steps:**

- [ ] Read `tools/curation.json` (`groups`, `operationNames`, `envelopePayloadNames` — the
      `pty`/`persistentPty` rows are the pluralization precedent) and the precedent commit
      `cb740db` (`git show --stat cb740db`) for the shape of a curation-only admission.
- [ ] **Commit A — `vcs.base`.** Add `v2.vcs.base` to the profile (alphabetical position). Run
      `generate`; verify the emitter named `GetBaseAsync`, `VcsBaseRequest`, `VcsBaseResponse`
      with a nullable `Base` (`VcsBase?`) and `Location`; if any name differs, report what it is
      and adapt the tests — do not add rows to force the table's names unless the mechanical name
      is wrong (a wrong name is one that misreads: report it).
- [ ] Contract tests for `vcs.base` in `VcsClientContractTests.cs`, mirroring the file's idiom:
      typed success from a wire body matching `Vcs.Base` (`name`, `ref`, `source` ∈
      `reflog|default`) with the location echo; the represented-null arm (`"data": null` → a
      successful response whose `Base` is null, `IsError` false); one declared error (503
      `ServiceUnavailableError` is the family's new arm); `NoThrow`. Route test in
      `OpenCodeRoutesTests` if the file covers vcs routes.
- [ ] Full gate incl. tool smoke and `generate --verify`; PublicApi baseline regenerated
      (additive). Commit: `feat(sdk): select vcs.base through the coverage arc`.
- [ ] **Commit B — the filesystem family.** Add `groups.fs` = `{"placement": "client",
      "clientName": "FileSystem", "reason": "<true present-tense fact: the three fs operations
      (list, find, read) carry no per-id operation, so a flat container holds the calls
      (ADR-0019); the client is named for the upstream tag and the FileSystem.Entry component,
      not the abbreviation>"}`; an `operationNames` row `v2.fs.list` → `ListEntriesAsync` (reason:
      the group singular `fs` does not pluralize naively, exactly like `v2.pty.list`; the reviewed
      subject is the directory's entries); `envelopePayloadNames["v2.fs.list"] = "Entries"`. Add
      `v2.fs.list` to the profile. Run `generate`; confirm `FileSystemClient.ListEntriesAsync`,
      `FsListRequest { Path, Location }`, `FsListResponse.Entries`/`Location`, model
      `FileSystemEntry { Path, Type (enum file|directory) }`. Confirm the writer accepted the new
      family folder (no shadow-wall refusal); if it refused, report the exact wall.
- [ ] Contract tests in the new `FileSystemClientContractTests.cs`: typed success with two entries
      (one `file`, one `directory`) and the location echo; the empty list; 400
      `InvalidRequestError`; `NoThrow`; the route with `path` and the location selector.
- [ ] `docs/ROADMAP.md`: update the profile counts to **124 selected / 10 pending / 2
      transport-owned** and add one Status sentence naming both admissions (this arc's paragraph
      is created by Task 11; here a single sentence at the end of §Status suffices). Full gate incl.
      tool smoke and `generate --verify`. Commit:
      `feat(sdk): admit the filesystem family with fs.list through the coverage arc`.
- [ ] Report: names the emitter produced, every PublicApi addition (counts), the marker's new
      header, test totals, concerns.

---

### Task 2: Query-parameter mechanism — required, non-nullable, and enum-valued parameters; select `fs.find`, `vcs.diff`, `session.stats`

**Where it fits:** the first mechanism that reaches `tools/`; unblocks three operations directly
and `shell.output`'s queries for Task 3. Design is fixed by ruling R4; the implementer decides the
emitted shapes within it and reports any place the design does not fit.

**Files:**
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Binding/QueryRequestFacetBinder.cs`,
  `Binding/Models/QueryValueKind.cs`, `Binding/Models/QueryPropertyPlan.cs` (+`IsRequired`,
  +`EnumTypeName`), `Binding/Models/QueryRequestPlan.cs` (+`HasRequiredMember` or derived),
  `Binding/ReachableSchemaCollector.cs` (visit enum-valued query schemas),
  `Binding/SchemaNameResolver.cs` (mechanical `{RequestTypeName}{Property}` names for promoted
  query enums), the ingestion promotion step for inline parameter schemas
  (`Generator/Ingestion/Projection/*` — find where inline nominal body/response schemas are
  promoted and extend it to query parameters under a parallel operation-scoped key),
  `Emission/QueryRequestEmitter.cs` (required property; enum property type),
  `Emission/RoutesEmitter.cs` (required route parameter; enum wire switch),
  `Emission/OperationMethodEmitter.cs` and `ClientEmitter.cs` (required `request` parameter when
  the query request has a required member), the enum model emitter or a new emitter for the
  wire-name helper if a separate helper is chosen
- Modify: `tools/generation-profile.txt` (+`v2.fs.find`, +`v2.vcs.diff`, +`v2.session.stats`),
  `tools/curation.json` (`envelopePayloadNames["v2.fs.find"] = "Entries"`,
  `envelopePayloadNames["v2.vcs.diff"] = "Diffs"` with reasons; an `operationNames` row for
  `v2.fs.find` → `FindEntriesAsync` only if the mechanical name misreads — `find` is not a known
  verb segment, so the mechanical verb is the HTTP method: check what is emitted and curate the
  reviewed name with the standard "the operation's own verb is find; the mechanical GET prefix is
  transport detail" reason)
- Tests: `tests/OpenCode.Sdk.Tools.Tests/Generator/Binding/*` (binder unit rows for each new
  shape: required text, non-nullable optional text, component enum, inline enum, an unsupported
  shape still refusing, the `ListRequest` profile unaffected), emitter micro-snapshots where the
  emitters have them, `tests/OpenCode.Sdk.Tests/OpenCodeRoutesTests.cs` (required parameter
  present; enum wire values; omitted optionals), contract tests for the three operations,
  PublicApi baseline
- Regenerated: `src/OpenCode.Sdk/**`
- Modify: `docs/ROADMAP.md` counts 127/7/2

**Steps:**

- [ ] Read `QueryRequestFacetBinder.cs`, `QueryRequestEmitter.cs`, `RoutesEmitter.cs`,
      `QueryStringBuilder.cs`, `ReachableSchemaCollector.cs`, `OperationNamePolicy.cs`, the
      ingestion promotion of inline nominal schemas (grep `op:` key construction under
      `Generator/Ingestion/Projection`), and one generated enum (`Models/AgentInfoMode.cs`,
      `StrictJsonStringEnumConverter`). Write down, in the report, the exact operation-scoped key
      shape you will use for promoted query enums and why it is consistent with the existing keys.
- [ ] TDD the binder: red tests for (a) a required plain-string parameter binding to a
      required Text property, (b) a non-nullable optional string binding to an optional Text
      property, (c) a `$ref` enum parameter binding to `QueryValueKind.Enum` with the component's
      type name, (d) an inline enum parameter binding to `Enum` with the mechanical
      `{RequestTypeName}{Property}` name, (e) a required deep-object location still refusing,
      (f) an object-valued query parameter still refusing "unsupported schema shape", (g) the
      cursor trio still deriving `ListRequest`. Then implement.
- [ ] Reachability: enum-valued query schemas (component and promoted inline) join the reachable
      set so their models emit; every other query schema stays skipped (keep the comment
      truthful).
- [ ] Emission: a required member emits `public required T Name { get; init; }`; an operation
      whose query request has a required member takes `TRequest request` (no default) on the
      client method and the route builder guards it with `ArgumentNullException.ThrowIfNull`
      exactly as body-bearing routes do; an enum member emits `TEnum?` (or `TEnum` when required)
      and the route builder writes its wire value through a generated switch over the enum's bound
      members (source: the same `EnumPlan` the model emitter renders) into
      `QueryStringBuilder.AddText`. No reflection, no `Enum.ToString`, no attribute lookup at run
      time. Decide where the switch lives (a private static method in the routes family class, or
      an internal per-enum helper emitted beside the model) and state the reason in the report.
- [ ] Select the three operations; add the curation rows; regenerate; review the diff:
      `FsFindRequest`, `VcsDiffRequest`, `SessionStatsRequest` shapes per the Names table;
      `VcsMode`, `FsFindRequestType`, `SessionStatsRequestTools` enums; `SessionStatsInfo` and its
      component closure (`TokenUsage.Info`, `Money.USD`, `SessionStats.Tools`, `.Activity`,
      `.ModelUsage`); `FileDiffInfo` already exists (`FileDiffInfoStatus` is generated today).
- [ ] Route tests: `FsFind` with `query` only; with every member; `VcsDiff` with `mode=committed`
      and `base`; `SessionStats` with `tools=detail` and with nothing set (bare path). Contract
      tests per the floor for each operation, including `vcs.diff`'s 503 arm and `session.stats`'s
      400 arm; the `Enumerate*` companion must NOT appear for any of the three (none carries the
      cursor trio).
- [ ] ROADMAP counts **127 selected / 7 pending / 2 transport-owned**; one sentence in §Status
      naming the mechanism. Full gate incl. tool smoke and `generate --verify`. Commit(s):
      `feat(generator): bind required, non-nullable, and enum-valued query parameters` then
      `feat(sdk): select fs.find, vcs.diff, and session.stats through the query mechanism` (or one
      commit if the emitter and selection cannot be separated cleanly — say which).
- [ ] Report: the key shape chosen, the wire-switch placement, every PublicApi change, the marker
      header, concerns; a "Canon sentence proposed" section if `protocol-and-generation.md`'s
      query/options wording needs a sentence (it currently says nothing about query shapes — if
      nothing is needed, say so).

---

### Task 3: Location-envelope single-object `data` promotion — select `shell.output`

**Where it fits:** the last envelope arm C2 left nominal-only; `shell.output`'s queries are
covered by Task 2's mechanism (b).

**Files:**
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Binding/SchemaNameResolver.cs`
  (`ResolveEnvelopePayloadRootNames`: the DataLocation arm claims the promoted `data` object key,
  not only the list item), `Binding/EnvelopeFacetBinder.cs` (`BindDataLocationPayload` accepts a
  promoted `data` key the resolver claimed; the fall-through refusal stays for unclaimed keys;
  update the doc comments that say the single object "stays nominal")
- Modify: `tools/generation-profile.txt` (+`v2.shell.output`); `tools/curation.json` only if the
  mechanical `ShellOutputData` name misreads (it does not — leave it)
- Tests: binder unit rows (promoted single-object `data` binds under `{stem}Data`; an unclaimed
  promoted key still refuses; the list-item arm unchanged), `tests/OpenCode.Sdk.Tests/Shells/*`
  contract tests, route test, PublicApi baseline
- Regenerated: `src/OpenCode.Sdk/**`; ROADMAP counts 128/6/2

**Steps:**

- [ ] Read `EnvelopeFacetBinder.BindDataLocationPayload`, `SchemaNameResolver.
      ResolveEnvelopePayloadRootNames`/`ResolveDataMemberKey`/`ResolveDataLocationListItemKey`,
      and the Data-shape precedent (`SessionFormCreateResponse`, `PersistentPtyHandoffPostData`).
- [ ] TDD: red binder test — a location wrapper whose `data` is an inline object binds to a
      `NamedTypeReferencePlan` named `{stem}Data` with kind `DataLocation`; red name-resolver test
      — the promoted key is claimed under `PayloadTypeName`. Then implement, mirroring the Data
      arm; keep the '#'-key guard for keys the resolver did not claim.
- [ ] Select `v2.shell.output`; regenerate; confirm `ShellClient.GetOutputAsync(ShellOutputRequest?
      …)`, `ShellOutputRequest { Cursor, Limit, Location }`, `ShellOutputResponse.Output`
      (`ShellOutputData`) + `Location`; the 404 arm reuses `ShellNotFoundError`.
- [ ] Contract tests (floor + the route with `cursor`/`limit` strings passed verbatim — no numeric
      parsing, ADR-0014). ROADMAP counts **128 / 6 / 2** and one sentence. Full gate incl. tool
      smoke and `generate --verify`. Commit(s): `feat(generator): promote a location envelope's
      inline data object` and `feat(sdk): select shell.output through the envelope mechanism`.
- [ ] Report incl. any "Canon sentence proposed" (`protocol-and-generation.md` §Generated model
      shape's envelope bullet currently says "inline objects promoted under deterministic
      operation-scoped names" generically — state whether it already covers this arm).

---

### Task 4: The `{name, data}` error style — select `worktree.create`, `worktree.refresh`, `worktree.remove`

**Where it fits:** the ADR-0007 mechanism decided by ruling R6; the first error dialect beside
`_tag`, touching the binder, the model/union emitters, the generated converter, and the runtime
reader's contract.

**Files:**
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Binding/SchemaPlanBinder.cs` (`BindErrorUnion`
  admits `EffectTag` and `NameData` variants; a `NameData` variant's marker is its `name` literal;
  `UnionVariantPlan` gains the marker property name or a style discriminator),
  `Binding/Models/UnionVariantPlan.cs` / `UnionPlan.cs` as needed, `Emission/ModelEmitter.cs`
  (the `Tag` accessor carries `[JsonPropertyName("name")]` for a `NameData` error; `Data` is the
  nested generated record), `Emission/UnionEmitter.cs` (the error converter emits a second frozen
  map for `name`-tagged types and dispatches `_tag` first, then `name`), and
  `src/OpenCode.Sdk/Internal/Serialization/UnionDiscriminatorReader.cs` only if a non-throwing
  "try find marker" variant is needed for the two-marker scan (keep the scan DOM-free for known
  arms)
- Modify: `tools/generation-profile.txt` (+ the three worktree operations), `tools/curation.json`
  (`operationNames` row `v2.worktree.refresh` → `RefreshWorktreesAsync` — mechanical is
  `PostRefreshAsync`; verify `create`/`remove` derive `CreateWorktreeAsync`/`RemoveWorktreeAsync`)
- Tests: `tests/OpenCode.Sdk.Tools.Tests` (binder: mixed-style closure binds; a `NameData` error
  without a required `data` is not an error style; two variants sharing one marker value across
  styles refuse), `tests/OpenCode.Sdk.Tests` (converter: a `name`-tagged body materializes
  `WorktreeError` with `Data.Message`/`Data.ForceRequired`; a `_tag` body still dispatches; an
  unknown `name` yields the unknown carrier with that marker; a body with neither marker fails as
  today), `tests/OpenCode.Sdk.Tests/Worktrees/*` contract tests (create success → `WorktreeInfo`;
  create 400 `WorktreeError` with `forceRequired`; create 400 `InvalidRequestError` on the same
  status; remove/refresh 204; `NoThrow`), `OpenCodeApiException` message carries
  `'WorktreeError'`, PublicApi baseline
- Regenerated: `src/OpenCode.Sdk/**`; ROADMAP counts 131/3/2; research-log note deferred to Task 11

**Steps:**

- [ ] Read `SchemaPlanBinder.BindErrorUnion`, `ErrorStyleClassifier`, the generated
      `OpenCodeErrorJsonConverter`, `UnionDiscriminatorReader`, `OpenCodeErrorReader`,
      `IOpenCodeError`, one generated error (`ShellNotFoundError`), and ADR-0007.
- [ ] TDD the binder (red: a closure holding one `_tag` error and one `name/data` error binds
      with two variants whose markers are `_tag`/`name` respectively; red: duplicate marker value
      across styles refuses naming both). Implement.
- [ ] Emission: the `NameData` record's `Tag` property serializes as `name` (its literal), `Data`
      binds as the nested promoted record named `{Error}Data` (mechanical; report the name the
      resolver produces); the converter's read path tries `_tag` then `name` without building a
      DOM for known arms and keeps `UnknownOpenCodeError(marker, payload)` for an unknown value of
      either marker; the write path is unchanged. Keep the allocation-free known-arm path
      (`GetAlternateLookup` on net9+).
- [ ] Select the three worktree operations with the `refresh` naming row; regenerate; review:
      `ProjectWorktreesClient` gains `CreateWorktreeAsync(WorktreeCreateRequest request)`,
      `RemoveWorktreeAsync(WorktreeRemoveRequest request)`, `RefreshWorktreesAsync(…)`; the two
      204 responses are no-content envelopes; `WorktreeError`/`WorktreeErrorData` models; the
      converter's `name` map holds `WorktreeError`.
- [ ] Contract and converter tests per the Files list; route tests for the three paths.
      ROADMAP counts **131 / 3 / 2**, one sentence naming the widened error style. Full gate incl.
      tool smoke and `generate --verify`. Commit(s): `feat(generator): admit name/data-tagged
      errors beside the Effect _tag style` and `feat(sdk): select the worktree family through the
      error-style mechanism`.
- [ ] Report: the marker-scan design, every PublicApi change, the exact wall message that replaced
      "must use the Effect _tag style in M1" (there should be no style wall left; a *third* style
      must still refuse by name — say what it says now), and a "Canon sentence proposed" section
      only if `protocol-and-generation.md`/`client-runtime.md` state something this task falsifies.

---

### Task 5: Tools touch A — `ToolJsonContext` `NewLine` pin and the telltale-completeness rider

**Where it fits:** two small tools fixes riding the first mechanism that reached `tools/`.

**Files:**
- Modify: `tools/OpenCode.Sdk.Tools/Serialization/ToolJsonContext.cs` (`NewLine = "\n"` on the
  `[JsonSourceGenerationOptions]` attribute); the two writers that append a bare `"\n"` tail
  (`Generator/Refresh/SnapshotSynchronizer.cs`, `Output/GenerationWriter.cs`) stay as they are
  unless the pin makes the tail double — check and keep exactly one trailing LF
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Binding/PendingOperationBindabilityProbe.cs`
  (`FirstProblem` → every distinct `Problem` in binder order, joined with `; `), the marker writer
  if line shape needs it, and the `[refused: …]` documentation strings
- Tests: `tests/OpenCode.Sdk.Tools.Tests` — a no-CR test over a serialized receipt/manifest
  through `ToolJsonContext` (assert no `\r` byte anywhere, exactly one trailing `\n`); probe test:
  an operation with two independent walls yields both in its mark; the `ToolAppTests` marker
  expectation updated

**Steps:**

- [ ] Add the pin; run `generate` and `refresh-spec --verify` on Windows; confirm
      `git ls-files --eol spec/receipt.json src/OpenCode.Sdk/.generated-manifest.json` reports
      `w/lf` after a regeneration (report the before/after lines).
- [ ] Widen the probe; regenerate; the marker's `config.get` line now lists its walls (expected:
      the group row wall is absent because the probe supplies a synthetic row; the
      `ConfigModelCost` collision, the `IMcp` collision, the three ADR-0016 walls, and the
      consequential promotion errors — report the exact line; if the consequential errors make the
      line unreadable, deduplicate by problem text only, never drop independent walls).
- [ ] Full gate incl. tool smoke, `generate --verify`, `refresh-spec --verify`. Commits: `fix(tools):
      pin ToolJsonContext line endings to LF` and `feat(tools): list every wall in the bindability
      telltale`.

---

### Task 6: Tools touch B — Proposal 1, the stabilize-duplicate collapse

**Where it fits:** maintainer-approved mechanism (ROADMAP §Known Gaps (1)); replaces 24 of 25
explicit `schemaAliases` rows with a mechanical policy.

**Files:**
- Create: `tools/OpenCode.Sdk.Tools/Generator/Binding/StabilizeDuplicatePolicy.cs` (+ a plan
  model for the implicit alias set), tests `tests/OpenCode.Sdk.Tools.Tests/Generator/Binding/
  StabilizeDuplicatePolicyTests.cs`
- Modify: the alias resolution site (where `curation.SchemaAliases` is applied — find it; the
  policy runs before explicit rows and to a fixpoint), `Output/GenerationWriter.cs` / the manifest
  model (`implicitAliases` telltale section), `tools/curation.json` (retire the 24 `_N` rows; keep
  the non-`_N` `op:…causeSchema/items` row), `docs/ROADMAP.md` (the (1) bullet moves to landed),
  the research-log entry deferred to Task 11 (record the facts in the report)
- Regenerated: manifest; generated source must be byte-identical (the aliases collapse to the same
  targets) — prove it with `git diff --stat src/OpenCode.Sdk` showing only the manifest

**Steps:**

- [ ] Read the ROADMAP bullet verbatim and copy its seven guardrails into the test names: a
      reachable `<base>_<N>` folds into `<base>` only when `SchemaNodeComparer.DeepEquals` holds;
      a non-equal pair refuses naming both keys; runs to a fixpoint; never chains (`A_2` → `A_1`
      → `A` is refused, not followed); implicit aliases are written to
      `.generated-manifest.json` as a committed telltale; explicit `schemaAliases` rows stay for
      non-`_N` cases; an unrecognized naming convention gets no implicit alias and surfaces as an
      undeclared duplicate does today (a duplicate error tag refuses; a duplicate model breaks the
      PublicApi baseline) — one test per guardrail, red first.
- [ ] Implement; retire the 24 rows; regenerate; prove the generated source is unchanged and the
      manifest lists the 24 implicit aliases.
- [ ] ROADMAP: move (1) to landed with the row count. Full gate incl. tool smoke and `generate
      --verify`. Commit: `feat(tools): collapse stabilize duplicates mechanically`.
- [ ] Report incl. "Canon sentence proposed" for `protocol-and-generation.md` §Curation boundary
      (the maintainer approved "one curation-boundary sentence in canon" — write it; it is applied
      only after approval).

---

### Task 7: Tools touch C — Proposal 2, the source watch

**Where it fits:** maintainer-approved mechanism (ROADMAP §Known Gaps (2)); a review trigger for
the hand-written doors' upstream inputs, never a generation input (ADR-0013).

**Files:**
- Create: `spec/source-watch.json` (entries: `path`, `sha256`, `anchor` — a content anchor in the
  patch manifests' predicate style — for both file sets the ROADMAP bullet lists, hashed at the
  submodule's checkout `b1e3a7b2`)
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Refresh/SnapshotSynchronizer.cs` (+ models):
  prepare reports each entry's current hash and whether the anchor still matches in the receipt
  (`watchedSources`); `--verify` checks the pins against the submodule checkout; `--apply` re-pins
  over the reviewed receipt; a missing watched file is a loud refusal in prepare/verify
- Modify: `spec/receipt.json` (regenerated through `refresh-spec --verify`? No — verify does not
  write; re-run prepare against the current pin `b1e3a7b2` and apply it to install the
  `watchedSources` section, exactly like a refresh; the document stays identical), `docs/ROADMAP.md`
  ((2) moves to landed)
- Tests: `tests/OpenCode.Sdk.Tools.Tests/Generator/Refresh/*` (receipt gains the section; a
  changed anchor is reported; a hash mismatch fails verify; a missing file refuses)

**Steps:**

- [ ] Read the Refresh slice, its tests, and the patch manifest's `repairPredicate` shape.
- [ ] TDD the receipt/verify/apply behavior; implement; write `spec/source-watch.json` with the
      real hashes (`git -C external/opencode show b1e3a7b2:<path> | sha256sum`) and one anchor per
      file that names the behavior the door depends on (e.g. the `4404` close code in
      `handlers/pty.ts`).
- [ ] Re-prepare and apply at `b1e3a7b2` so the committed receipt carries `watchedSources`;
      `refresh-spec --verify` green. ROADMAP (2) landed. Full gate incl. tool smoke, `generate
      --verify`, `refresh-spec --verify`. Commit: `feat(tools): watch the hand-written doors'
      upstream sources through the refresh receipt`.
- [ ] Report incl. "Canon sentence proposed" for `spec/SNAPSHOT.md` §Refresh procedure and
      `protocol-and-generation.md` §Snapshot production (one sentence each, applied only after
      approval).

---

### Task 8: Tools touch D — the single-key envelope facet (`{handoff}`, `{urls}`)

**Where it fits:** the parked `response.Handoff.Handoff` accessor; ruling R7 makes it a shape
rule.

**Files:**
- Modify: `tools/OpenCode.Sdk.Tools/Generator/Ingestion/Projection/EnvelopeClassifier.cs`
  (`SingleKey` when an inline object has exactly one required property that is not `data`, whose
  schema is a `$ref` or a nullable `$ref`), `Ingestion/Models/SpecEnvelopeShape.cs`,
  `Binding/EnvelopeFacetBinder.cs` (bind like Data with the key as the wire member and
  `PascalCase(key)` as the payload name unless curated), `Binding/SchemaNameResolver.cs` (no
  promoted payload key for this shape — the value is nominal), the envelope DTO/adapter emitters
  (the wire member name is the key, not `data`), a curation opt-out only if a reviewer needs it
  (do not add speculatively)
- Regenerated: `PersistentPtyHandoffResponse.Handoff` → `PersistentPtyHandoff?`;
  `PersistentPtyHandoffPostData` disappears; `ServerResponse` gains `Urls` and loses its promoted
  body model; the hand-written `PersistentPtyClient.HandoffAsync` door and the live test /
  sandbox leg that read `.Handoff.Handoff` are updated
- Tests: classifier and binder rows; contract tests for `persistentPty.handoff` (existing, updated)
  and `server.get` (existing, updated); PublicApi baseline (renames named)

**Steps:**

- [ ] Enumerate the affected operations from the pinned document (expected exactly two) and record
      them in the report before changing anything.
- [ ] TDD classifier + binder; implement; regenerate; update the door, the live test, the sandbox
      leg (`PersistentPtyWalkthrough`), and their contract tests.
- [ ] Full gate incl. tool smoke and `generate --verify`. Commit: `feat(generator): flatten
      single-key success envelopes`.
- [ ] Report: the two renames, every PublicApi change, and a "Canon sentence proposed" for
      `protocol-and-generation.md`'s envelope bullet.

---

### Task 9: The hygiene batch — one dispatch, one review

**Where it fits:** goal (3); every item is non-blocking and cheap together. Verify each against
the file before changing it — line numbers have moved since the handoff was written.

**Files:** per item, from `docs/agents/handover-prompts/HANDOFF-2026-08-30.md` §"1. The hygiene
batch — one dispatch, one review": Generator (2), Doors (2), Core (5), Session and decoder (7),
Fixture (3), Live and sandbox (5), Docs (4), plus the sandbox `--no-launch-profile` /
provider-less-500 item (decide doc note vs code fix and say why).

**Steps:**

- [ ] Read the handoff section and, for each item, the current file. Produce a table in the report:
      item → file:line today → action taken (fixed / doc note / proposal / declined with reason).
- [ ] Fix the code items. Behavioral item first: `PersistentPtyFrameDecoder` maps a null `role`
      to Controller — make it a typed frame failure with a test (a protocol deviation must not
      misreport an observer as a controller). The `ptyId`/`IPtyWebSocket` family-neutral rename is
      the end-of-arc decision the handoff named: rename to family-neutral names
      (`terminalId`/`ITerminalWebSocket`) and report the PublicApi effect (internal types: none
      expected).
- [ ] Docs items: the two `client-runtime.md` items are canon → "Canon sentence proposed" in the
      report, not applied; Q156's "join the parked set" vs ROADMAP's parked-reports bullet →
      reconcile in ROADMAP (operational) by listing Q156's three report candidates; the research
      log's `Info.output.tail` casing is a research-log fix (operational).
- [ ] Sandbox: the O(n²) re-decode becomes a single incremental decode; the `IsError` branch
      distinguishes the daemon-absent 503 from other errors; evidence lines print observed values.
- [ ] Full gate (tests for each behavioral change). Commits grouped by area (`fix(sdk): …`,
      `test(sdk): …`, `docs: …`) — several commits are fine; each gate-green.
- [ ] Report: the table, PublicApi changes, canon proposals.

---

### Task 10: The `PersistentPtySession` benchmark rung

**Where it fits:** goal (5); the increment check only (ruling R12).

**Files:**
- Modify: `tests/OpenCode.Sdk.Performance.Tests/**` — a `PersistentPtySession` read-ladder class
  beside the `PtySession` one (same component ladder: complete read path, decode alone, over the
  same fixture sizes; `GlobalSetup` refuses a fixture that does not materialize the frames it
  claims), `docs/engineering/quality-gates.md` is canon → no edit; `README`/docs of the
  performance project if it lists its classes

**Steps:**

- [ ] Read the `PtySession` ladder (`B2`, research log Q156 names its rungs and the
      `.benchmarks/persistent-pty-after-short` artifacts) and mirror it for `PersistentPtySession`
      over its frame hierarchy (`attached`, output, unknown-type carrier).
- [ ] Run `--job Dry` first (fixtures changed), then `--job short` filtered to the two PTY ladders
      with `--runtimes net10.0 net472 --artifacts .benchmarks/coverage-arc-pty-short`; keep the
      machine quiet; never edit the performance project while the run is live.
- [ ] Full gate. Commit: `perf(tests): add the PersistentPtySession read-ladder rung`. Report
      the exact allocated-bytes columns for both ladders (allocation is the comparable axis;
      timings are indicative only) and the artifact folder.

---

### Task 11: Record — research log, ROADMAP, upstream-report draft, handoff, release-prep proposals

**Where it fits:** the closing documentation; operational docs only.

**Files:**
- Modify: `docs/research/00-research-log.md` (+ `Q158: What did the coverage-to-full arc land,
  and which walls stand?` — the document-identical refresh sentence, the per-mechanism designs and
  evidence, the `config.get` re-classification with the ten-error list, the widened error style,
  the tools touch, the benchmark columns; file `Date:` → 2026-08-30 or later),
  `docs/ROADMAP.md` (§Status paragraph for the arc; §Known Gaps: (1)/(2) landed, the three
  standing walls with their mechanism names, the `ConfigModelCost`/`IMcp` rows recorded for the
  day the ADR-0016 wall moves, release-prep coupling: the packing wall keys on pending = 0 while
  three operations stay pending by decision — the wall needs a maintainer-acknowledged residual or
  a "declined" admission state; §Open Questions parked reports + the `fs.read` wildcard draft),
  `.scratchpad/upstream-issue-drafts/fs-read-wildcard-path.md` (the draft: `/api/fs/read/*` is a
  Hono wildcard, not an OpenAPI path template; propose `/api/fs/read/{path}` with
  `allowReserved`, or a `path` query like `fs.list`; note the `application/octet-stream` body),
  `docs/agents/handover-prompts/HANDOFF-2026-08-31.md` (replaces the 2026-08-30 file — delete it —
  with: start-here state, what landed, the rulings R1–R12 verbatim for confirmation, the canon
  sentences proposed (collected from every task report), the release-prep decisions the
  maintainer owns (packing-wall wording, ADR-0021 `Date:`, the no-suppression rule's home,
  pre-1.0 versioning), the parked minors, standing facts)
- Commit: `docs: record the coverage-to-full arc and hand off`

**Steps:**

- [ ] Read every task report in the SDD workspace and the ledger's rulings; write the four
      documents; keep ROADMAP shrinking (delete landed queue items rather than annotating them).
- [ ] Full gate (docs-only commit still runs the base gate). Commit.
- [ ] Report: the list of canon sentences collected and the open maintainer decisions.

## Self-review (run before handing the plan to executors)

- Every task names its files, its tests, its commit message, and its gate; none edits canon.
- The mechanisms are keyed on shape, not operation id (ADR-0013); each widening keeps a refusal for
  the shapes it does not admit.
- Counts: 122 → 124 (T1) → 127 (T2) → 128 (T3) → 131 (T4); 3 pending by decision; 2
  transport-owned; total 136.
- Task 2 precedes Task 3 (shell.output's queries), Task 5 precedes Tasks 6–8 (manifest line
  endings), Task 8 precedes Task 9 (the live test's `.Handoff` accessor changes), Task 11 is last.

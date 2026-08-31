# Release Preparation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use deniz-process:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the repository publicly presentable and shippable in the maintainer's own house
style — public documentation first, then test badges, then nightly packages to GitHub Packages and
a manual Trusted-Publishing lane to NuGet.org — modeled on the maintainer's three reference
repositories (`localstack-dotnet/localstack-dotnet-client`, `localstack-dotnet/
dotnet-aspire-for-localstack`, `localstack-dotnet/badge-smith`), never on generic templates.

**Spec / authority:** the maintainer's decisions of 2026-08-31 (recorded in the coverage arc's
ledger and Memorizer): CHANGELOG in **the client's custom emoji-headed per-release format** (not
Keep-a-Changelog); public docs as a **`docs/guide/` subset** beside the internal canon; **nightly
on every master push only** (no PR prereleases) → GitHub Packages; **`VersionPrefix 0.1.0`**;
badges via badge-smith's **current** composite actions (both siblings' own copies are outdated —
the client's has been silently 404ing since 2025-07-25 — never template from them); CI/CD workflow
*structure* modeled on the two siblings. Study reports with verbatim excerpts:
`.scratchpad/release-prep-refs/{localstack-dotnet-client,dotnet-aspire-for-localstack,badge-smith}.md`
— every implementer reads the relevant report(s) before writing.

**Already done (do not redo):** the BadgeSmith side is fully seeded — `blind-striker/testdata`
HMAC exists in prod (Secrets Manager `badgesmith/github/blind-striker/testdata`), and this repo
has the `TESTDATASECRET` actions secret. The local badge-smith checkout is at `f3ab12b`.

## Global Constraints

- The completion gate per task is unchanged (`docs/engineering/quality-gates.md`): slopwatch
  (`--exclude ".scratchpad/**,external/**" --fail-on warning`, must be 0), Release build, both
  `dotnet format` verifications, full `dotnet test` (with `--report-trx --report-trx-filename
  <unique>.trx`); generator/curation/profile/marker changes add the tool `--help` smoke and
  `generate --verify`. Every command FOREGROUND with a generous timeout; never `run_in_background`
  for dotnet.
- **Canonical documents are not edited** (`AGENTS.md`, `CONTEXT.md`, `docs/architecture/**`,
  `docs/engineering/**`, `docs/adr/**`, `spec/SNAPSHOT.md`): a sentence a task would need there is
  written verbatim under "Canon sentence proposed" in its report and held for the maintainer.
  Operational docs (`docs/ROADMAP.md`, research log, handoff, README, CHANGELOG, `docs/guide/**`,
  `.github/**`) are edited directly.
- **House style is the reference repos', extracted from the study reports** — section shapes,
  emoji conventions, tone (direct, upbeat, checkmark-heavy status, root-caused known-issues
  prose). English throughout. No AI-attribution trailers; Conventional Commits; never push.
- Workflow YAML mirrors the siblings' *structure* (job names, matrix shape, step ordering,
  comments) adapted to this repo's realities (no Docker/CDK; the pinned-server fixture builds
  `external/opencode` with bun on all three OSes — the existing `ci.yml` already does this and its
  working parts are kept).
- New tests follow Q157 (`ParallelConstraintKeys.ServerProcess` / keyless `[NotInParallel]`) and
  `docs/engineering/testing-style.md`.
- Badge facts (verified 2026-08-31 at badge-smith `f3ab12b`): composite actions
  `localstack-dotnet/badge-smith/.github/workflows/update-test-badge@v1` (inputs: `platform`,
  `test_passed`, `test_failed`, `test_skipped`, `test_url_html?`, `commit_sha`, `run_id`,
  `repository`, `server_url`, `api_base_url`, `hmac_secret`) and
  `localstack-dotnet/badge-smith/.github/workflows/run-dotnet-tests@v1` (build once, test per
  TFM, unique TRX per framework; inputs `project-path`, `results-dir`, `configuration`). Tag `v1`
  exists (immutable `v1.0.0` at the same tip). API base `https://api.localstackfor.net`; badge
  markdown `https://img.shields.io/endpoint?url=https%3A%2F%2Fapi.localstackfor.net%2Fbadges%2Ftests%2F{platform}%2Fblind-striker%2Fopencode-sdk-dotnet%2Fmaster`
  linking `https://api.localstackfor.net/redirect/test-results/{platform}/blind-striker/opencode-sdk-dotnet/master`;
  platforms `linux|windows|macos`; the server lowercases the owner. Secret: `TESTDATASECRET`.
  Badge publishing runs ONLY on master pushes.
- Release facts: version single-sourced as `VersionPrefix` in `Directory.Build.props` (`0.1.0`);
  nightly suffix computed in CI as `-nightly.{yyyyMMdd}.{shortSha}` (the client's shape, concept
  not Cake); nightly → GitHub Packages (`https://nuget.pkg.github.com/Blind-Striker/index.json`,
  `GITHUB_TOKEN`); stable → manual `workflow_dispatch` with a typed version, NuGet.org via
  **Trusted Publishing** (`NuGet/login@v1` OIDC, `id-token: write`, no API key — mirror the
  client's PR #54). The maintainer still owns creating the NuGet.org Trusted Publishing policy
  before the first stable run; the workflow lands now and says so in its header comment.
- Packing today FAILS on the `.generation-incomplete` marker by design while pending > 0
  (`Directory.Build.targets`). Task 4 changes that contract deliberately; no task before it runs
  `dotnet pack`.

## Controller rulings (2026-08-31)

- **RP1** — README badges are written in Task 1 with the final URLs even though they render "not
  found" until the first master CI run feeds them; cost if wrong: a cosmetic day.
- **RP2** — the "declined" admission state (Task 4) is implemented with a canon sentence proposed,
  not applied (R8 pattern); the marker stays the single source of truth: each declined operation
  carries `[declined: <reason>]` from a reasoned curation section, pack unblocks only when
  pending = 0 AND every non-selected, non-transport-owned operation is declined. Cost if wrong:
  one marker-format revert.
- **RP3** — `run-dotnet-tests@v1` is adopted only if it fits the existing multi-project test
  layout without weakening the gate; otherwise keep our test steps and add only the TRX
  count-extraction + `update-test-badge@v1` steps. The implementer decides and reports why.
- **RP4** — nightly packs all three packages (Sdk, Extensions, and the source-generated
  companions as the solution defines them) or the set `dotnet pack` produces at the solution
  root; the implementer enumerates what packs and reports it.

## Tasks

### Task 1: Public face — README, CHANGELOG, community files

**Files:** `README.md` (rewrite), `CHANGELOG.md` (new), `.github/CONTRIBUTING.md`,
`.github/SECURITY.md`, `.github/CODE_OF_CONDUCT.md`, `.github/ISSUE_TEMPLATE/*`,
`.github/PULL_REQUEST_TEMPLATE.md` (new, adapted from the siblings); `docs/ROADMAP.md` only if a
public-facing fact it states goes stale.

- [ ] Read all three study reports plus the live `README.md` of both siblings (local checkouts:
      `E:/repos/my-projects/localstack-dotnet-client`, `E:/repos/my-projects/dotnet-aspire-for-localstack`)
      for section order and tone; read this repo's current `README.md`, `CONTEXT.md`, and
      `docs/ROADMAP.md` §Status for the facts (131/3/2 coverage, TFMs
      `netstandard2.0;net472;net8.0;net9.0;net10.0`, pinned-snapshot model, connection modes,
      SSE/PTY doors, 4,407 tests).
- [ ] `README.md` in the client's shape: badge row (3 test badges linux/windows/macos + CI status
      + GitHub Packages nightly version + NuGet version + license, exact URLs from Global
      Constraints), what-it-is, install (NuGet stable "coming soon" + GitHub Packages nightly feed
      instructions), quickstart (a real compiling snippet: client construction + one call + one
      stream), compatibility table, coverage note (131/136 + the three declined-by-decision with
      one-line reasons), links into `docs/guide/` (Task 2's pages — link them now), known
      issues section (root-caused prose, the client's style), contributing/license footer.
- [ ] `CHANGELOG.md` in the client's exact format (emoji headings, per-release sections): one
      `## [Unreleased]` section summarizing what 0.1.0 will contain (the SDK surface at 131 ops,
      launcher, SSE, PTY families, multi-TFM).
- [ ] Community files adapted from the siblings with this repo's names/links; Conventional
      Commits stated in CONTRIBUTING; SECURITY contact = the maintainer's GitHub security
      advisories flow, mirroring the siblings.
- [ ] Full gate; commit `docs: give the repository its public face`.

### Task 2: The public guide — `docs/guide/`

**Files:** `docs/guide/README.md` (index), `getting-started.md`, `connection-modes.md`
(standalone/external/DI), `streaming.md` (SSE), `terminals.md` (PTY + persistent PTY sessions),
`errors-and-responses.md` (throw/NoThrow, typed errors incl. the `{name,data}` dialect),
`pagination.md`; each page with runnable, compiling snippets checked against the current public
surface (`tests/OpenCode.Sdk.Tests/Snapshots/PublicApi.verified.txt` is the authority; the
sandbox walkthroughs under `tests/OpenCode.Sdk.Sandbox/` are the source of truthful examples).

- [ ] Write the seven pages in the house tone; internal canon (`docs/architecture/**` etc.) is
      NOT linked from the guide except a single "internals" pointer in the index; the guide
      speaks to SDK consumers only.
- [ ] Cross-link from `README.md` (Task 1 already placed the links — verify they resolve).
- [ ] Full gate; commit `docs(guide): add the consumer guide`.

### Task 3: Test badges in CI

**Files:** `.github/workflows/ci.yml` (extend; keep the existing job semantics green).

- [ ] Read the badge-smith study report §action + the composite actions' sources at `f3ab12b`
      (local `E:/repos/my-projects/badge-smith/.github/workflows/{update-test-badge,run-dotnet-tests}/action.yml`).
- [ ] Per RP3: integrate count extraction from the TRX each OS leg already produces (or adopt
      `run-dotnet-tests@v1` if it fits) and add the `update-test-badge@v1` step per platform
      (`Linux`, `Windows`, `macOS`), gated `if: github.ref == 'refs/heads/master' &&
      github.event_name == 'push'`, with `api_base_url: https://api.localstackfor.net` and
      `hmac_secret: ${{ secrets.TESTDATASECRET }}`; badge failure must not fail CI (the action's
      default) — state the chosen wiring in the report.
- [ ] Full gate locally; commit `ci: publish test-result badges to BadgeSmith`. (The proof is the
      next master push's run — the controller watches it.)

### Task 4: The packing wall's "declined" admission state

**Files:** `tools/curation.json` (a new reasoned `declined` section for `v2.config.get`,
`v2.fs.read`, `v2.experimental.migration.v1.status` — each reason states the standing wall and
the maintainer decision date), `tools/OpenCode.Sdk.Tools` (loader/validator/binder/marker: a
declined operation must still be probed and must still appear in the marker as
`[declined: <reason>]` beside its walls; a declined operation that becomes selected refuses; a
declined row over a bindable operation refuses — decline is only for walled operations),
`Directory.Build.targets` (pack unblocks when pending = 0 and every remaining gap is declined or
transport-owned; the marker file stays committed while any declined/transport-owned rows exist),
`Directory.Build.props` (`VersionPrefix` = `0.1.0`), tests for every new wall, ROADMAP.

- [ ] TDD the loader/validator/marker changes; regenerate; the marker now reads
      131 selected / 0 pending / 3 declined / 2 transport-owned (or the format the emitter
      derives — report it verbatim); `dotnet pack` at the solution root SUCCEEDS and the
      produced package list is enumerated in the report (RP4).
- [ ] "Canon sentence proposed" for `protocol-and-generation.md`'s marker sentence (RP2) — held.
- [ ] Full gate + tool smoke + `generate --verify`; commit
      `feat(tools): admit a declined state so packing can open at full reachable coverage`.

### Task 5: Nightly + stable publish workflows

**Files:** `.github/workflows/ci.yml` (a `publish-nightly` job after green tests, master pushes
only: pack with `--version-suffix nightly.{yyyyMMdd}.{shortSha}`, push all packages to GitHub
Packages with `GITHUB_TOKEN`), `.github/workflows/publish-nuget.yml` (new: `workflow_dispatch`
with a typed `version` input and a `target` choice `nuget|github`, Trusted Publishing via
`NuGet/login@v1` + `id-token: write` for the nuget path — mirror the client's PR #54 shape; a
header comment records that the maintainer must create the NuGet.org Trusted Publishing policy
before the first run), README's install section updated if feed instructions shifted.

- [ ] Mirror the siblings' workflow structure; keep permissions minimal per job.
- [ ] Full gate locally (workflows lint by actionlint if available, else YAML sanity via
      `gh workflow list` after push is the controller's step); commit
      `ci: publish nightlies to GitHub Packages and add the manual NuGet lane`.

### Task 6: Record and handoff refresh

- [ ] Research log entry (next Q number) for the release-prep arc: the reference-repo synthesis
      facts, the seeding (no secret values), the declined-state design, the workflows; ROADMAP:
      release-prep items land/shrink, the remaining maintainer step (NuGet.org policy) recorded;
      the active handoff updated (or replaced) with the new state.
- [ ] Full gate; commit `docs: record the release preparation arc`.

## Sequencing

Task 1 → 2 (guide links), Task 3 independent after 1 (README badge URLs exist), Task 4 before 5
(pack must open), Task 6 last. One dispatch per task, per-task reviews, the SDD ledger under
`.superpowers/sdd/2026-08-31-release-prep/`.

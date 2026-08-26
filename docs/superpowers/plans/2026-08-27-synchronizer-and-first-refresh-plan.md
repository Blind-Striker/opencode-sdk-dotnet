# Synchronizer and First Refresh Plan — the M5 opener

Date: 2026-08-27

**Goal:** the accepted snapshot moves from `a6a712a3` to a resolved current `v2` tip through the
first receipt-governed, Restore-patched refresh (ADR-0020), executed by a minimal `refresh-spec`
synchronizer, with the regenerated surface green, the two upstream-deleted question operations
removed from the shipped surface, and every new tip construct either ingested as pending or
refused behind a named wall. Nothing else about the public surface changes in this arc.

Sealed inputs: ADR-0020 (production policy), ADR-0013 (operation-identity curation rows),
design doc §4/§10, research doc 21, research log Q147–Q149, `spec/SNAPSHOT.md`,
[#44911](https://github.com/anomalyco/opencode/issues/44911) /
[PR #45182](https://github.com/anomalyco/opencode/pull/45182). Q149 verified at tip `6170221e`:
the restore step applies cleanly (326 components), the refused set is byte-identical to doc 21's,
and the two operations upstream added both bind green.

## Sealed design

### Synchronizer minimal scope

Three subcommands on `opencode-tool`, in a module separate from `GenerationCoordinator`:

```
refresh-spec --ref <sha-or-moving-ref>   # prepare: scratch artifacts + receipt only
refresh-spec --verify                    # reproduce the accepted recipe observationally
refresh-spec --apply <receipt.json>      # human act: update spec/, SNAPSHOT.md, gitlink
```

- Prepare resolves a moving ref **once** to a full SHA, manages a detached worktree of
  `external/opencode` (the checkout never moves), runs `bun install --frozen-lockfile
  --ignore-scripts` and the pinned upstream generator, applies the ordered patch list, computes
  document/patch hashes and the sorted operation-set digest, and writes the receipt plus a delta
  report (operations added/removed, component counts, `contentSchema` presence) to scratch.
  It changes no accepted repository file.
- Every patch carries: upstream issue/PR reference, exact byte hash, touched-file preimages, a
  **repair predicate** and a **retirement predicate**. If raw upstream already satisfies the
  repair predicate, prepare refuses the patch and requires an empty-patch retirement refresh —
  so if #45182 merges before apply, this arc's own machinery forces the patch out.
- Verify re-derives the committed identity from the committed recipe and compares hashes; it
  never repairs product files.
- Apply consumes one reviewed receipt, refuses time-of-check/time-of-use drift by re-hashing,
  updates only `spec/openapi.json`, `spec/SNAPSHOT.md` (identity table + recipe), and the
  submodule gitlink, and never stages, commits, or pushes.
- Out of minimal scope: observation-lane automation, candidate scheduling, PublicApi-diff
  probes, delta walls beyond the receipt's report (all M6 or later).

### Committed artifacts (decision to confirm at plan approval)

- Patches live under `spec/patches/NNN-<slug>.patch` (ordered, committed). First and only
  member: `001-restore-sse-payloads.patch` — the #45182 restore step; repair predicate:
  `contentSchema` links present on `V2EventEncoded`/`SessionLogItemEncoded` in the raw document.
- The receipt of the **current** accepted snapshot is committed as `spec/receipt.json` and
  replaced at each refresh; history lives in git. `SNAPSHOT.md` stays the human-readable owner.

### Ingestion pre-work (must land before apply; design §10 serialization)

1. **Header parameters** enter SpecIR faithfully. Binder policy stays fail-closed: a *selected*
   operation carrying a header parameter refuses until the location/PTY arc gives headers a
   runtime owner; pending operations merely ingest. (`pty.connect.token` is the only case.)
2. **`contentEncoding: base64`** (persistentPty snapshot checkpoint) is retained losslessly in
   SpecIR; its binding representation is a later decision — the family stays pending this arc.
3. **Operation-identity rows** (ADR-0013): curation gains the row type — subject id, intended
   identity, mandatory reason with upstream issue reference — and the ingestion wall gains the
   curation-gated admit. The eight `server.experimental.persistentPty.*` ids ride these rows
   onto their intended `v2.persistentPty.*` identities as pending operations; the wall refuses
   any unmapped off-convention id exactly as before.

### The refresh's surface consequences

- Removed from the shipped surface: `session.question.reject`, `session.question.reply`
  (deleted upstream, T5). Their curation rows, generated methods, models, and tests go; the
  PublicApi review is **removal-bearing** (accepted pre-1.0).
- Added as pending only: the persistentPty family (via identity rows), `worktree.*`,
  `workspace.create`, `vcs.branches`, `session.stats`, `v2.session.messageUpdate`,
  `v2.credential.activate`, and the other tip arrivals. **No new operation is selected in this
  arc.** Profile counts update mechanically; the receipt's delta report is the authority.
- Stale curation referencing upstream-removed families (the Q137/Q139 drift set) is deleted.

## Increments

Each lands independently green through the full quality gate (generator increments add the tool
smoke and `generate --verify`). Synchronizer logic that shells out to bun/git is exercised
locally and by design not in CI; hashing, receipt validation, preimage checks, and predicate
evaluation are pure and unit-tested.

### Increment 1 — ingestion pre-work

- [x] The upstream T3 report is drafted and parked (maintainer, 2026-08-27): it files when the
      maintainer chooses, alongside the other doc 21 C4 reports — the ROADMAP owns the parked
      list, the draft sits in `.scratchpad/upstream-issue-drafts/`. Identity rows cite doc 21
      T3 until the issue exists, then gain the number.
- [x] Header-parameter and `contentEncoding` ingestion, red-first; binder wall for selected
      header-parameter operations. `contentEncoding` projects as a distinct `EncodedStringNode`
      so every plain-string expectation and the binder's default arm refuse it fail-closed.
- [x] Operation-identity row type + validator + ingestion gate, exercised through synthetic
      documents. `generate --verify` stays byte-identical at the old pin (82/38 unchanged; the
      rows referencing tip-only ids land with Increment 3, or the validator would refuse
      unknown subjects).

### Increment 2 — minimal synchronizer

- [x] `refresh-spec` prepare/verify/apply with receipt, patch, hash, predicate, TOCTOU, and
      rollback machinery; `spec/patches/001-restore-sse-payloads.patch` authored from the
      source-only subset of PR #45182 and hash-pinned beside its manifest.
- [x] Pure-logic unit tests (18; ScriptedProcessRunner seam, no bun/git in CI); one local
      end-to-end prepare run against tip `954cdc7b` succeeded — 133 operations (+22/−9 vs the
      pin), 336 components, `contentSchema` back at 2, both preimages recorded — its receipt
      under `.scratchpad/refresh/954cdc7b…/` is the Increment 3 candidate.

### Increment 3 — the first accepted refresh (maintainer act)

- [ ] Maintainer reviews the prepared receipt; `--apply` updates `spec/`, `SNAPSHOT.md`
      (identity, recipe, procedure rewritten from manual copy to synchronizer usage), and the
      submodule gitlink.
- [ ] Curation batch in the same change: question-operation rows removed, identity rows added,
      drift-set leftovers deleted; regenerate; PublicApi removal review; contract-test cleanup.
- [ ] Live sanity: the committed sandbox's mechanism leg against a server built from the new
      pinned commit (bun-launched from the submodule) — bounded smoke, not the full walkthrough.
- [ ] ROADMAP status + profile counts updated in the same change.

### Increment 4 — closure

- [ ] `refresh-spec --verify` green on the committed state; three-OS hosted run green; research
      log entry recording the refresh identity, delta, and receipt digest.

## Canon edits (inside their increments, never ahead)

1. Increment 3: `spec/SNAPSHOT.md` procedure section rewritten around the synchronizer.
2. Increment 3: ADR-0021's consequence line drops the word "first" (this refresh's question-op
   removals precede the PTY rework's non-additive review) — one-word accuracy fix.
3. Increment 3: `docs/architecture/protocol-and-generation.md` gains the snapshot-production
   mechanics section (deferred from the canon PR precisely for this moment).

## Out of scope, with owners

- Location runtime + PTY ownership rework (next arc, ADR-0021), envelope completion, operation
  inventory/assurance ledger, M4 fixture/launcher, observation-lane automation and canary (M6),
  persistentPty selection and the base64 binding representation (their admitting batches).
- Per-session refresh cadence activates only after this arc proves prepare/verify/apply
  (ADR-0020's gating condition).

## Risks

- The tip moves daily: the ref is resolved once at prepare; a stale receipt is refused at apply
  by re-hashing, and re-preparing is cheap.
- `needs:issue` on PR #45182: upstream process friction may delay the patch's retirement, not
  its application — the patch machinery is indifferent.
- Upstream's committed document is stale against its own generator (T7): prepare runs the
  generator rather than trusting the committed artifact in repair mode, per ADR-0020.

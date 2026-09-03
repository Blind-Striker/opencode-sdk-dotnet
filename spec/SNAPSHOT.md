# OpenAPI Snapshot

Date: 2026-09-03

`openapi.json` is the accepted snapshot of the upstream opencode OpenAPI 3.1 document — the v2
protocol surface (ADR-0005). The SDK is built against this snapshot, never against a live
branch; the `v2` branch moves daily. Refresh policy is receipt-governed (ADR-0020): a refresh
consumes an exact commit, normally with an empty patch list, and temporary Restore patches may
repair upstream projection loss under review receipts. `spec/receipt.json` is the committed
receipt of the current accepted snapshot; active patches live under `spec/patches/` beside
their hash-pinned manifests.

| Fact | Value |
|---|---|
| Upstream file | `packages/protocol/openapi.json` |
| Upstream branch | `v2` (active successor line; no release tags yet) |
| Commit | `48f246695e97a0014f57399f7603df0b1b2b85d4` |
| Upstream product channel | `opencode2` — npm `@opencode-ai/cli@next` (pre-release) |

Platform evidence for the v2 line: internal research, 2026-08-16, "The opencode v2 platform:
branch, surface, architecture, distribution".

## Refresh procedure

The `refresh-spec` synchronizer owns refreshes (ADR-0020):

1. `dotnet run --file tools/opencode-tool.cs -- refresh-spec --ref <commit-ish>` prepares a
   candidate: it resolves the reference once to a full SHA and produces the normalized
   document — an identity transform when `spec/patches/` is empty, the exact pinned upstream
   generator over the ordered Restore patches otherwise — writing the receipt and document to
   `.scratchpad/refresh/<sha>/` without touching accepted state. A patch whose repair
   predicate raw upstream already satisfies refuses, forcing an empty-patch retirement
   refresh.
2. Review the receipt: identity, hashes, operation delta, patch preimages.
3. `refresh-spec --apply <receipt.json>` installs `spec/openapi.json`, updates this file's
   identity table and date, moves the `external/opencode` submodule checkout to the same
   commit, re-pins `spec/source-watch.json` for any watched hash that moved, and writes
   `spec/receipt.json`; it stages and commits nothing.

Prepare also observes every entry of `spec/source-watch.json` at the candidate commit and records
its hash and anchor verdict in the receipt's `watchedSources`; verify checks those pins against the
submodule checkout and apply re-pins them, so an upstream change under a hand-written door is
reviewed before the snapshot moves.

4. Run `generate`; resolve what it reports (wall admits, curation rows) and review the
   regenerated diff. `refresh-spec --verify` reproduces the committed identity
   observationally.

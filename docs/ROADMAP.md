# Roadmap

Date: 2026-09-01

Operational state: what ships today, what is queued next, what is still open, and what is known to
be incomplete. This file is a summary and shrinks as work lands. `../AGENTS.md` routes to the
current architecture and engineering canon; decision records live in `adr/`.

## Status

**Pre-release, and the protocol surface is complete.** The callable surface is generated from an
accepted OpenAPI snapshot and rides one hand-written transport runtime.

- **Protocol pin** — generation reads an accepted snapshot of upstream's `v2` OpenAPI document,
  never a live branch, and refreshes are receipt-governed (ADR-0020). `../spec/SNAPSHOT.md` owns
  the exact commit and the refresh procedure.
- **Coverage** — **131 of 136 operations selected** across 27 client families, with 3 declined by
  decision and 2 transport-owned (Known Gaps below); `src/OpenCode.Sdk/.generation-incomplete` is
  the committed marker and names every one. One-shot calls, server-sent event streams (the global
  bus and the per-session log), PTY and persistent-PTY WebSocket sessions, cursor pagination, typed
  errors with `NoThrow`, and the standalone launcher (`OpenCodeServer.StartAsync`) are landed.
- **Assurance** — the suite runs on `net8.0`, `net9.0`, and `net10.0` across Linux, Windows, and
  macOS, plus `net472` on Windows, the fullest leg; `engineering/quality-gates.md` owns the gate a
  change must pass before it is called done.
- **Packages** — `OpenCode.Sdk` and `OpenCode.Sdk.Extensions` pack at the single-sourced
  `VersionPrefix 0.1.0`, and every `master` push publishes a `0.1.0-nightly.*` build to GitHub
  Packages. NuGet.org publication is currently blocked by an upstream prefix reservation dispute;
  the manual publish lane is wired and waits on it.

## Milestones

Deliverable-first: every milestone ends in something callable or demonstrable. The next milestone
gets a short (1–2 page) plan when it starts — never earlier. Ordering beyond the current milestone
is revisited at each boundary.

1. **M1 — Walking skeleton.** `v2.health.get` and `v2.session.message` end to end: pinned document
   through binding and emission to committed generated source, over a hand-written transport, with
   typed errors and `NoThrow`. **Complete.**
2. **M2 — Breadth batches.** The generation profile grows in vertical operation batches, each
   landing its curation rows, reachable models, operation methods, and contract tests together, and
   the Extensions package grows alongside. **Complete.**
3. **M3 — Streams.** The construction reshape (ADR-0010), the location and merged-request
   marshalling design, the SSE engine over the v2 stream surface, the cursor paginator (ADR-0017),
   the owned-transport and `net472` gate, and a measured performance pass. **Complete.**
4. **M4 — Launcher and process truth.** Parity with upstream's three connection modes: standalone
   start, explicit endpoint, and the registration-file background service. The standalone door
   (`OpenCodeServer.StartAsync`, ADR-0001) and the explicit-endpoint validation option are landed
   with three-OS acceptance, an exact-pin server fixture, and a deterministic simulated-model
   session workflow (ADR-0022). **The background-service parity arc is queued** —
   `OpenCodeService.DiscoverAsync/EnsureAsync/StopAsync` over the registration file, an
   upstream-observed contract outside the OpenAPI pin, so canary-guarded.
5. **M5 — Full surface.** Target admission over the refreshed surface, driven by the `refresh-spec`
   synchronizer (ADR-0020) and the ownership pattern for the terminal families (ADR-0021). Coverage
   has reached its end state; what remains is exclusion fingerprints for the transport-owned
   operations (ADR-0008), the remaining package, API, and TFM assurance, and the operation
   inventory and assurance ledger — which standardizes pending-operation bindability tracking,
   subsumes `tools/generation-profile.txt` as the one hand-authored admission list, and makes
   per-operation assurance mechanically complete: a contract test for every status arm the pinned
   document declares, verifier-checked, with the arms no deterministic fixture can reach listed by
   name rather than skipped silently (ADR-0022).
6. **M6 — Operational closure.** Automation for the upstream observation lanes (tip detector,
   candidate refresh), retry/telemetry/hooks with the public network-timeout knob, a quarantine lane,
   the nightly source-run canary with the performance suite (ADR-0022), and Restore-patch retirement.

## Open Questions

- **v2 GA watch** — the v2 line ships as `opencode2` (npm `@opencode-ai/cli@next`) with no GA date.
  The pin therefore stays a deliberate snapshot, refreshed under receipt at milestone boundaries.
- **`v2.session.log` resume guarantees** — the pinned document exposes `after` as an optional
  string, and the generated surface stays faithful to it; ADR-0013 forbids importing the narrower
  type upstream's implementation decodes. Retention and replay guarantees are unestablished.
- **OpenAPI projection fidelity** — the pinned document loses detail upstream's implementation
  carries. Confirmed losses are reported upstream
  ([anomalyco/opencode#44911](https://github.com/anomalyco/opencode/issues/44911), restored by the
  still-open [PR #45182](https://github.com/anomalyco/opencode/pull/45182)); further candidates are
  parked for filing at the maintainer's choosing — off-convention `persistentPty.*` operation ids,
  a missing security-scheme declaration, 25 lost `Config.Info` descriptions, an undeclared header
  value, a numeric range and a file path both invisible behind bare strings, a WebSocket close code
  overloaded across two causes, and two declared arms the handler cannot produce. Findings stay
  diagnostic and never feed generation or curation (ADR-0013).
- **Release mechanics** — ADR-0006's shape is wired. Open: the release-notes flow and the
  prerelease-versus-stable wording for the first tagged release.
- **Deferred design questions, each parked behind a named trigger** — splitting validated client
  configuration from the transport factory (reopens when M6 attaches telemetry or hooks, or when
  Extensions gains a concrete named-client need); a parent-mediated handle door for flat
  single-action families and exposing a handle client's resource id as a property (both additive, so
  both wait for the packaging freeze's surface review); a `PtySession.SubmitAsync(string)`
  convenience door (waits for a first real consumer); the generated folder and namespace layout
  review; and the generator's remaining binding-locality extractions.

## Known Gaps

- **Three operations stay declined by decision, not by omission.** The generation marker carries
  each reason. `v2.config.get` and `v2.experimental.migration.v1.status` meet the ADR-0016
  structural-union wall: same-token-kind unions need a union mechanism, not a curation row.
  `v2.fs.read` is declared on a framework wildcard rather than an OpenAPI path template, so the file
  path the call must carry is invisible to any generated client; admitting it would mean inventing a
  path parameter the document does not declare (ADR-0013), and the upstream report is drafted.
- **Two allocation follow-ups are queued behind a benchmark gate** — on `net472` and
  `netstandard2.0` a response body over 1 MB costs one wire-sized copy, and `PtySession.ReadAsync`
  allocates a fresh 16 KiB receive buffer per call. Both are measured rather than suspected, and
  both are described for consumers in the README's Known Issues.
- **A server-process start stalls in-process `net472` tests for about ten seconds** on hosted
  Windows. Harmless today, because every timing-bounded test runs alone, and queued as a hygiene
  candidate: the first suspect is .NET Framework's synchronous pipe reads holding thread-pool
  threads for every piped child. Measure before changing anything.
- **Small cleanups queued for their next natural touch** — `envelopePayloadNames` is the one
  curation section whose rows cannot carry a reason (a mechanical loader change, though authoring
  fifteen verified reasons is not); the generator still inlines the dot-segment refusal into every
  route builder instead of calling the shared policy (a large but purely mechanical generated
  diff); the `form` group's curation reason is written in the future tense where every sibling
  states present fact; the `MedianNanoseconds` benchmark column breaks the other columns'
  abbreviation convention; and the committed sandbox's `--paginate` mode exits nonzero on an empty
  enumeration.
- **Three one-off test failures were seen once each and never reproduced.** No runner named a test
  and re-runs of the same binaries were green, so this is a measurement gap rather than a known
  defect: run the gates with `--report-trx --report-trx-filename <unique>` so a recurrence names it.
- **`BuildOs`/`BuildArch` in `Directory.Build.props`** need their values adapted to opencode's
  release-asset naming when the binary-download need lands.

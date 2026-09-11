# Roadmap

Date: 2026-09-12

Operational state: what ships today, what is queued next, what is still open, and what is known to
be incomplete. This file is a summary and shrinks as work lands. `../AGENTS.md` routes to the
current architecture and engineering canon; decision records live in `adr/`. The live operational
queue is on the [project board](https://github.com/users/Blind-Striker/projects/1).

## Status

**Pre-release, and the protocol surface is complete.** The callable surface is generated from an
accepted OpenAPI snapshot and rides one hand-written transport runtime.

- **Protocol pin** — generation reads an accepted snapshot of upstream's `v2` OpenAPI document,
  never a live branch, and refreshes are receipt-governed (ADR-0020). `../spec/SNAPSHOT.md` owns
  the exact commit and the refresh procedure.
- **Coverage** — **134 of 139 operations selected** across 28 client families, with 3 declined by
  decision and 2 transport-owned (Known Gaps below); `src/OpenCode.Sdk/.generation-incomplete` is
  the committed marker and names every one. One-shot calls, server-sent event streams (the global
  bus and the per-session log), PTY and persistent-PTY WebSocket sessions, cursor pagination, typed
  errors with `NoThrow`, and the standalone launcher (`OpenCodeServer.StartAsync`) are landed.
- **Assurance** — the suite runs on `net8.0`, `net9.0`, and `net10.0` across Linux, Windows, and
  macOS, plus `net472` on Windows, the fullest leg; `engineering/quality-gates.md` owns the gate a
  change must pass before it is called done.
- **Terminal lifetime corrections (D)** — consumer cancellation, send deadlines, local viewport
  ordering, and shared cleanup are implemented and verified on Windows across the four runnable
  targets. Linux and macOS live verification also passed on net8/net9/net10, including the persistent daemon
  round trip and normal PTY reuse after read cancellation. `architecture/client-runtime.md` and
  ADR-0023 own the contract.
- **Packages** — the two packages publish as `OpenCodeAI.Sdk` and `OpenCodeAI.Sdk.Extensions`
  (the assemblies stay `OpenCode.Sdk`) and pack at the single-sourced
  `VersionPrefix 0.8.0`. Every `master` push publishes a `0.8.0-nightly.*` build to GitHub
  Packages, and `0.8.0-preview.1` is on NuGet.org, owned by `OpenCode.NET` and pushed through the
  manual lane over Trusted Publishing. The ids carry `OpenCodeAI` because nuget.org reserves the
  `OpenCode.` prefix for an unrelated owner; that dispute is still open and no longer blocks
  anything.

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
   candidate refresh), retry/telemetry/hooks with the public network-timeout knob and the
   per-operation event-stream idle bound it gates, a quarantine lane, the nightly source-run
   canary with the performance suite (ADR-0022), and Restore-patch retirement.

## Open Questions

- **v2 GA watch** — the v2 line ships as `opencode2` (npm `@opencode/cli@beta`) with no GA date.
  The pin therefore stays a deliberate snapshot, refreshed under receipt at milestone boundaries.
- **`v2.session.log` resume guarantees** — the pinned document exposes `after` as an optional
  string, and the generated surface stays faithful to it; ADR-0013 forbids importing the narrower
  type upstream's implementation decodes. Replay mechanics and retention are established and
  carried by canon. What stays open is the wire behaviour nothing upstream pins: no server-level
  test covers this route, so the status a malformed `after` answers, and whether an idle `follow`
  connection survives an intermediary, are settled only by this repository's own live tests.
  Upstream has no production caller that passes `after`, so this SDK is the path's first consumer.
- **OpenAPI projection fidelity** — the pinned document loses detail upstream's implementation
  carries. Confirmed losses are reported upstream
  ([anomalyco/opencode#44911](https://github.com/anomalyco/opencode/issues/44911), restored by the
  still-open [PR #45182](https://github.com/anomalyco/opencode/pull/45182)); further candidates are
  parked for filing at the maintainer's choosing — off-convention `persistentPty.*` operation ids,
  a missing security-scheme declaration, 25 lost `Config.Info` descriptions, an undeclared header
  value, a numeric range and a file path both invisible behind bare strings, a WebSocket close code
  overloaded across two causes, and two declared arms the handler cannot produce. Findings stay
  diagnostic and never feed generation or curation (ADR-0013).
- **Release mechanics** — ADR-0006's shape is wired, and the first tagged release ships as a
  `0.8.0-preview.N` prerelease, iterating the preview and patch numbers toward `1.0.0`. Open:
  the release-notes flow.
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
  `netstandard2.0` a response body over 1 MB costs one wire-sized copy, and each terminal connection
  allocates one 16 KiB receive buffer, reused across consumer reads. Both are described for consumers
  in the README's Known Issues; pooling requires evidence under the current connection-lifetime harness.
- **A half-open event stream is not detected.** A successful SSE body stays live until caller
  cancellation, server completion, or failure, so a connection whose peer is gone without closing
  hangs a consumer that supplied no cancellation of its own; ordinary resets, server exits, and
  killed processes already surface at once. A consumer cannot bound this from outside the SDK,
  because the server's keepalive comments carry no event and never reach the enumeration. The bound
  has to be per operation rather than a property of every SSE body: upstream writes a keepalive
  every fifteen seconds on `v2.event.subscribe` and none on `v2.session.log`, whose follow mode is
  silent by design while a session is idle. Queued behind M6's public network-timeout knob so the
  bound arrives configurable rather than as a behavior no caller can widen. Upstream's own clients
  place this one layer above their core client, which does not reconnect either
  (`packages/client/src/solid/connection.ts`: two-second connect, forty-five-second idle abort,
  one-second reconnect delay, and an authoritative refetch once reconnected).
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
- **CodeQL analyses C# without a build.** Code scanning runs on default setup, which extracts C#
  in `build-mode: none`. That leaves the database under CodeQL's own confidence thresholds — 83% of
  calls resolve to a target against a threshold of 85%, and 89% of expressions carry a known type
  against the same 85% — which its guidance attributes partly to generated source, and this
  repository commits a large generated surface. No alert is open, so the gap is possible false
  negatives rather than a known defect. Closing it means retiring default setup for an advanced
  `codeql.yml` that builds: full type resolution, bought with a workflow whose failure would stop
  scanning silently rather than loudly.
- **`BuildOs`/`BuildArch` in `Directory.Build.props`** need their values adapted to opencode's
  release-asset naming when the binary-download need lands.
- **The launcher's descendant-termination proof has a platform boundary.** The startup-tree tests
  prove the direct child exits immediately and the grandchild terminates inside a ten-second bound
  on the modern target frameworks (all three OSes) and on `net472` Windows (`taskkill /T`). The
  downlevel non-Windows arm of the tree kill (a plain `Kill()`) is not exercised by any test project,
  and the Linux/macOS behavior is established only by the three-OS CI run, never by a Windows-local
  suite. On Unix the observed grandchild is not a child of the test process, so its exit is visible
  only once the adopting parent reaps it: an environment without a reaping PID 1 fails that bound
  with the process still recorded as a zombie.

# Roadmap

Date: 2026-09-24

Operational state: what ships today, what is queued next, what is still open, and what is known to
be incomplete. This file is a summary and shrinks as work lands. `../AGENTS.md` routes to the
current architecture and engineering canon; decision records live in `adr/`. The live operational
queue is on the [project board](https://github.com/users/Blind-Striker/projects/1).

## Status

**Pre-release, and the protocol surface is complete.** The callable surface is generated from an
accepted OpenAPI snapshot and rides one hand-written transport runtime.

- **Protocol pin** — generation reads an accepted snapshot of upstream's OpenAPI document taken at
  a release tag, never a live branch, and refreshes are receipt-governed (ADR-0020).
  `../spec/SNAPSHOT.md` owns the exact commit and the refresh procedure.
- **Coverage** — **130 of 136 operations selected** across 27 client families, with 4 declined by
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
- **2.0.15 refresh** — the accepted pin follows upstream's release tags inside M4, now `v2.0.15`,
  with no compatibility layer between them. 2.0.15 adds no operation and moves none:
  `session.update` gains `metadata`, which replaces the session's metadata as a whole and is
  logged as the durable `session.metadata.updated` event, and a connection credential's `method`
  and a project's `active` time become required. The hand-written doors' upstream inputs moved in
  two places, both reviewed: the CLI entry writes a fatal startup cause to stderr, which a
  contender's failure report carries, and on Windows a package-installed service keeps a second
  hard link to its own image while it runs, which `StopAsync` ends by pid and start time as before
  — proven against the published CLI on the consumer leg. Each refresh's CI run qualifies every
  leg at its pin: Windows on `net472`, `net8.0`, `net9.0`, and `net10.0`, Linux and macOS on
  `net8.0`, `net9.0`, and `net10.0`, with regeneration and receipt verification passing. The
  remaining M4 slices follow the order in the milestone paragraph.
- **Official launch watch** — upstream's own 1.x npm package (`opencode-ai`) and its GitHub Releases
  page were both still at `1.18.31` when last observed on 2026-09-16, so the 2.x line had not had
  its official launch then. The pin tracks upstream release tags and is refreshed under receipt at
  milestone boundaries; package names and launch documentation are re-checked when the launch
  lands. The accepted protocol identity is owned by `../spec/SNAPSHOT.md`.
- **Packages** — the two packages publish as `OpenCodeAI.Sdk` and `OpenCodeAI.Sdk.Extensions`
  (the assemblies stay `OpenCode.Sdk`) and pack at the single-sourced
  `VersionPrefix 0.9.0`. Every `master` push publishes a `0.9.0-nightly.*` build to GitHub
  Packages, and `0.9.0-preview.3` is on NuGet.org, owned by `OpenCode.NET` and pushed through the
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
   session workflow (ADR-0022). **The background-service parity arc has landed** — its three
   doors: `OpenCodeServer.DiscoverAsync` over the registration file — an
   upstream-observed contract outside the OpenAPI pin, so source-watched (ADR-0024, ADR-0025) —
   with a non-owning handle (`OwnsProcess`), the CLI's channel, migration, and status rules, and
   live proof against the pin's own `serve --service` daemon on every runtime leg; and
   `OpenCodeServer.StopAsync`, the CLI's `service stop` — persistent terminals shut down through
   the generated door, the handoff sidecar cleared, the registered process ended by the pinned
   `SIGTERM`/`SIGKILL` ladder (hard kills on Windows) with the registration re-read before every
   signal and the process identified by pid and start time (ADR-0026), and the registration
   removed once the process is gone — proven against the pin's own daemon and against a process
   that ignores the first rung; and `OpenCodeServer.EnsureAsync`, the CLI's managed-service
   election — reusing a ready compatible daemon, replacing a version-mismatched one under an
   `Ignore`/`Replace`/`Error` policy, and otherwise spawning detached contenders until one
   registers or the 120-second bound expires, with the persistent-terminal handoff sidecar
   travelling across replacement — proven against the pin's own daemon from source (the live runs
   reach the handoff with no persistent terminal open, so the ticket-carrying path is proven by the
   loopback tests, not live) and, on the weekly consumer leg, against the published CLI.
   **Two generator slices rode inside M4 and have landed.** Fail-closed operation naming
   ([#86](https://github.com/Blind-Striker/opencode-sdk-dotnet/issues/86)): the HTTP method is
   never a name source — the closed grammar names an operation or a reason-bearing
   `operationNames` row does, handle clients name themselves rather than their family, and
   request, response, and payload types follow the same rule (ADR-0008) — together with the
   enum member naming channel ([#84](https://github.com/Blind-Striker/opencode-sdk-dotnet/issues/84),
   `McpProtocol.Revision20260728`). **Surface completeness** then admits the
   operations that sit outside generation: `config.get` and `experimental.migration.v1.status`
   through an ADR-0016 first-match arm that mirrors upstream's own union decode — token kind,
   literal sentinel, declaration order, required-key presence — proven against upstream's real
   decoder ([#87](https://github.com/Blind-Striker/opencode-sdk-dotnet/issues/87)); `fs.read`
   through a hand-written door over the wildcard octet-stream route with a watched upstream
   handler (an ADR-0013 question first), with `experimental.fs.write` — the same family's
   octet-stream request body, which the binder's JSON-and-event-stream media-type vocabulary
   cannot bind — decided alongside it; and the two transport-owned WebSocket doors counted as
   the covered operations they are — so the surface reads 136 of 136 usable.
   **What remains of M4, in order:** surface completeness (#87), bounded live-test parallelism
   ([#83](https://github.com/Blind-Striker/opencode-sdk-dotnet/issues/83)), then the close.
5. **M5 — Full surface.** Target admission over the refreshed surface, driven by the `refresh-spec`
   synchronizer (ADR-0020) and the ownership pattern for the terminal families (ADR-0021). Coverage
   has reached its end state; what remains is exclusion fingerprints for the transport-owned
   operations (ADR-0008), the remaining package, API, and TFM assurance, and the operation inventory
   and assurance ledger — which standardizes pending-operation bindability tracking, subsumes
   `tools/generation-profile.txt` as the one hand-authored admission list, and makes per-operation
   assurance mechanically complete: a contract test for every status arm the pinned document
   declares, verifier-checked, with the arms no deterministic fixture can reach listed by name
   rather than skipped silently (ADR-0022). It opens with response envelopes that stop printing the
   server's raw error body in `ToString`
   ([#100](https://github.com/Blind-Striker/opencode-sdk-dotnet/issues/100)).
6. **M6 — Operational closure.** Automation for the upstream observation lanes (tip detector,
   candidate refresh), retry/telemetry/hooks with the public network-timeout knob and the
   per-operation event-stream idle bound it gates, a quarantine lane, the nightly source-run
   canary with the performance suite (ADR-0022), Restore-patch retirement, and an evaluation of
   moving the repository to an organization for larger CI runners (GitHub larger runners and
   third-party providers are organization-only; the three legs run on the free 4-vCPU public
   runners today, macOS on 3). Also a publish-lane diet: the nightly job's `generate --verify`
   repeats what the same run's Linux leg already proved on the same commit; and test
   categorization (no test carries a category today; the only split is by project and by the
   `*LiveTests` / `*ContractTests` names), so a lane can run a named subset.

## Open Questions

- **`session.log` resume guarantees** — the pinned document exposes `after` as an optional
  string, and the generated surface stays faithful to it; ADR-0013 forbids importing the narrower
  type upstream's implementation decodes. Replay mechanics and retention are established and
  carried by canon. What stays open is the wire behaviour nothing upstream pins: no server-level
  test covers this route, so the status a malformed `after` answers, and whether an idle `follow`
  connection survives an intermediary, are settled only by this repository's own live tests.
  Upstream has no production caller that passes `after`, so this SDK is the path's first consumer.
- **OpenAPI projection fidelity** — the pinned document loses detail upstream's implementation
  carries. Confirmed losses are reported upstream
  ([anomalyco/opencode#44911](https://github.com/anomalyco/opencode/issues/44911), restored by the
  proposed [PR #45182](https://github.com/anomalyco/opencode/pull/45182)); further candidates are
  parked for filing at the maintainer's choosing — off-convention `persistentPty.*` operation ids,
  a missing security-scheme declaration, 25 lost `Config.Info` descriptions, an undeclared header
  value, a numeric range and a file path both invisible behind bare strings, a WebSocket close code
  overloaded across two causes, and two declared arms the handler cannot produce. Findings stay
  diagnostic and never feed generation or curation (ADR-0013).
- **Release mechanics** — ADR-0006's shape is wired. The line stays a preview until the M-series
  is complete: the minor number advances at each milestone boundary (`0.9.0-preview.N` when M4
  lands, `0.10.0-preview.N` when M5 lands) and M6 closes it at `1.0.0`; between milestones only
  the preview number moves, so a pin refresh or a fix round ships as the next `preview.N`. Open:
  the release-notes flow.
- **Deferred design questions, each parked behind a named trigger** — splitting validated client
  configuration from the transport factory (reopens when M6 attaches telemetry or hooks, or when
  Extensions gains a concrete named-client need); a parent-mediated handle door for flat
  single-action families and exposing a handle client's resource id as a property (both additive, so
  both wait for the packaging freeze's surface review); the generated folder and namespace layout
  review; and the generator's remaining binding-locality extractions.

## Known Gaps

- **Four operations stay declined by decision, not by omission.** The generation marker carries
  each reason. `config.get` and `experimental.migration.v1.status` meet the ADR-0016
  structural-union wall: same-token-kind unions need a union mechanism, not a curation row.
  `fs.read` is declared on a framework wildcard rather than an OpenAPI path template, so the file
  path the call must carry is invisible to any generated client; admitting it would mean inventing a
  path parameter the document does not declare (ADR-0013), and the upstream report is drafted.
  `experimental.fs.write` takes its file as an `application/octet-stream` request body, a media
  type the binder does not bind (its vocabulary is JSON and `text/event-stream`), so it is the
  same hand-written-door question as `fs.read`. The current marker also records config naming and
  inline-model walls. All four have a sketched admission path (Milestones, M4: surface
  completeness), so these are scheduled decisions to revisit, not standing ones.
- **There is no configuration read at the 2.0.15 pin.** Upstream removed `/api/config/preferences`
  and folded reading into `config.get`, which stays declined above, so the SDK writes configuration
  through `Experimental.UpdateConfigAsync` and cannot read it back. Admitting `config.get` is what
  restores the read; it raises that decision's cost without changing its mechanism.
- **An opencode server on Windows can die inside its native file watcher.** `@parcel/watcher` 2.5.1
  crashes the server process when a directory it watches natively is written to while
  subscriptions to it are being released and re-created, which the server does per location for
  the skills directories of every `.claude`, `.agents`, and `.opencode` root it discovers between a
  location and the drive root. A caller sees `OpenCodeTransportException`, not an SDK fault. The
  test fixtures are hermetic against it (`engineering/testing-style.md`); a consumer's server is
  not. The defect is
  [parcel-bundler/watcher#262](https://github.com/parcel-bundler/watcher/issues/262), where the
  standalone reproducer from this repository's investigation is on record.
- **The downlevel Unix arm of the background-service door is compiled, not run.** Discovery's
  one-time copy of an older hashed registration and Ensure's persistent-terminal handoff sidecar,
  which carries a terminal-adoption ticket, are created exclusively at mode `0600`: `net8.0` and
  later set the mode at creation through `FileStreamOptions.UnixCreateMode`, while `net472` and
  `netstandard2.0` have no such API and apply it through Polyfill's `File.SetUnixFileMode`, which
  spawns `chmod` with an unquoted path and no exit-code check, so a failed `chmod` leaves the file
  at the process umask. The stop door's Unix signal rungs and the contender spawn are `DllImport`s
  on that asset, where the `LibraryImport` generator the modern targets use is unavailable
  (ADR-0026, ADR-0027). The registration copy is a convenience the daemon's own registration
  supersedes and the ticket carries its own expiry; the population of that arm is Mono on Unix
  (`net472` is Windows-only), and no CI leg runs the combination, so it is recorded rather than
  tested; the README's Known Issues carries the consumer-facing sentence. Reopens if a supported
  target ever needs that arm or if Polyfill quotes the path.
- **A pid reused before `StopAsync` runs is indistinguishable from a wedged daemon.** The
  registration names a pid and no process start time, so a daemon that died and whose pid the
  operating system handed to an unrelated process before a stop ran looks, to the file, like the
  registered service still there. The identity token (ADR-0026) closes the window from the stop's
  first look to its last signal — the upstream client compares registration fields only — and the
  residual before it is upstream's too. Recorded, not solved; the registration file's write time
  could bound it if it ever matters.
- **Two allocation follow-ups are queued behind a benchmark gate** — on `net472` and
  `netstandard2.0` a response body over 1 MB costs one wire-sized copy, and each terminal connection
  allocates one 16 KiB receive buffer, reused across consumer reads. Both are described for consumers
  in the README's Known Issues; pooling requires evidence under the current connection-lifetime harness.
- **Durable session-log replay cannot be enabled on any distributed CLI build.** `events.persist`
  is a server-library option: the `opencode` serve command declares no flag for it, bridges no
  environment variable to it, and reads no configuration key for it, so a replay from a CLI-started
  server is one `log.synced` marker whose sequence advanced with no durable events before it
  (confirmed at the pin; observed on `@opencode/cli@2.0.2`). The SDK is faithful to the route — the
  gap is upstream capability — and consumers are told in the README's Known Issues and the
  streaming guide. Reversal trigger:
  `tests/OpenCode.Sdk.Tests/Sessions/SessionLogCliProfileLiveTests.cs`; when it fails, upstream
  began persisting by default and the guide, the README, and the canon sentence in
  `docs/architecture/client-runtime.md`'s server-sent-events section all change together.
- **A half-open event stream is not detected.** A successful SSE body stays live until caller
  cancellation, server completion, or failure, so a connection whose peer is gone without closing
  hangs a consumer that supplied no cancellation of its own; ordinary resets, server exits, and
  killed processes already surface at once. A consumer cannot bound this from outside the SDK,
  because the server's keepalive comments carry no event and never reach the enumeration. The bound
  has to be per operation rather than a property of every SSE body: upstream writes a keepalive
  every fifteen seconds on `event.subscribe` and none on `session.log`, whose follow mode is
  silent by design while a session is idle. Queued behind M6's public network-timeout knob so the
  bound arrives configurable rather than as a behavior no caller can widen. Upstream's own clients
  place this one layer above their core client, which does not reconnect either
  (`packages/client/src/solid/connection.ts`: two-second connect, forty-five-second idle abort,
  one-second reconnect delay, and an authoritative refetch once reconnected).
- **A server-process start stalls in-process `net472` tests for about ten seconds** on hosted
  Windows. Harmless today, because every timing-bounded test runs alone, and queued as a hygiene
  candidate: the first suspect is .NET Framework's synchronous pipe reads holding thread-pool
  threads for every piped child. Measure before changing anything.
- **Live tests are serialized by one mutex within a host** ([#83](https://github.com/Blind-Striker/opencode-sdk-dotnet/issues/83)):
  every live class that shares a server carries the `ServerProcess` key, and the classes whose
  assertions ride a wall-clock bound run keyless `[NotInParallel]`, so a host's live tests run one
  at a time.
  Bounded parallelism (`ParallelLimiter`) needs the simulated drive controller demultiplexed by
  session first; until then the remaining gain is about 8% per host and not worth the Windows
  watcher-churn risk. Queued inside M4.
- **Small cleanups queued for their next natural touch** — `envelopePayloadNames` is the one
  curation section whose rows cannot carry a reason (a mechanical loader change, though authoring
  fifteen verified reasons is not); the generator still inlines the dot-segment refusal into every
  route builder instead of calling the shared policy (a large but purely mechanical generated
  diff); the `form` group's curation reason is written in the future tense where every sibling
  states present fact; the `MedianNanoseconds` benchmark column breaks the other columns'
  abbreviation convention; the committed sandbox's `--paginate` mode exits nonzero on an empty
  enumeration; and the public API baseline renders `typeof(X)` attribute arguments as `typeof(X?)`
  on interfaces whose members are mostly nullable — a PublicApiGenerator artifact of the
  compiler's nullable-context compression, harmless and stable, to be normalized in the baseline
  test and reported upstream.
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

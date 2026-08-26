# OpenAPI Projection Fidelity — what the document does not carry, at two fixed points

Date: 2026-08-26

Evidence, not policy. This document measures the gap between what opencode's protocol actually
is and what its OpenAPI document says, at two reference points, and lays out the choice space
that gap creates. Decisions belong to the maintainer; nothing here is sealed.

## 1. The two fixed points

Every claim below is anchored to one of exactly two documents. Mixing them is the single
easiest way to draw a wrong conclusion, so they are named throughout.

| | **PIN** | **TIP** |
|---|---|---|
| Commit | `a6a712a3` (2026-08-13) | `bc1f67e5` (re-verified at `a5829431b0`) |
| Distance | — | 712 commits ahead on `v2` |
| Operations | 120 | 130 |
| Components | 324 | 217 |
| Event tree (`V2Event`/`SessionLogItem`) | present | **pruned** |
| `contentSchema` occurrences | 2 | **0** |
| Suffixed duplicate schemas | 25 | 7 |

A third artifact appears where it matters: **TIP+RESTORE** — TIP with the SSE-payload restore
step from [anomalyco/opencode#44911](https://github.com/anomalyco/opencode/issues/44911)
applied (316 components). It is what TIP becomes if the patch we offered upstream lands.

Method: a git worktree of the submodule at TIP with `bun install --frozen-lockfile`, upstream's
own generator (`OpenApi.fromApi(ClientApi)` → `stabilizeOpenApi`), the restore step executed
for real, and our own generator run against the result. Working notes and probes live under
`.scratchpad/oc-restore-NOTES.md`. The submodule checkout itself never left the pin.

## 2. What is *not* a problem

Recording the negative results matters as much as the findings; each one closes a question that
looked open.

- **Endpoint parity is exact.** The Effect contract declares 131 endpoints; the document carries
  131 operations; the set difference is zero in both directions. The mechanism is verified, not
  assumed: the projection's only silent-drop path (`OpenApi.Exclude`) is never used upstream,
  duplicate operations and operationIds *throw* rather than overwrite, and `stabilizeOpenApi`
  touches only `components.schemas`.
- **Declared channels project faithfully.** Across 131 endpoints: zero parameter mismatches
  (name/`in`/required) and zero status-set mismatches.
- **The form endpoints are fully documented.** All seven, with their complete `Form.*` component
  family and zero dangling `$ref`s. What is missing at TIP is the form *events*, and only as
  collateral of the event-tree pruning.
- **Our exclusion instincts match upstream's own.** Upstream's private `httpapi-codegen`
  hard-codes three omissions — `fs.read`, `pty.connect`, `persistentPty.connect`. Our
  fail-closed walls refuse exactly those three, arrived at independently.
- **Our SSE reader already tolerates the server's 15-second `: heartbeat` comment frames**
  (`ServerSentEventReader.cs:185`).

## 3. Findings

### 3.1 Ours — gaps in this repository

| # | Finding | Evidence |
|---|---|---|
| O1 | **No per-request header channel.** `OpenCodeRequestOptions` carries only `ErrorBehavior`. Two known protocol channels need headers: `x-opencode-ticket` (below) and `x-opencode-directory`/`x-opencode-workspace` for multi-project targeting — the latter recorded in research doc 01 §4 since the first architecture pass. | `OpenCodeRequestOptions.cs` |
| O2 | **`PostConnectTokenAsync` cannot succeed.** The server requires `x-opencode-ticket: "1"`; neither PIN nor TIP declares the header's required value, and the SDK cannot send it. Live against the pinned server: without the header `403 ForbiddenError`, with it `200`. The `403 → ForbiddenError` the B-1 batch generated *is* this failure, admitted without asking what produced it. | `packages/server/src/handlers/pty.ts:123`; live curl |
| O3 | **The envelope binder accepts only `data: $ref`.** A `data` that is an array, an inline object, or a dictionary is refused. This is 18 of the 31 refusals at TIP+RESTORE — one mechanism gap with three faces. | probe, §4 |
| O4 | **Error unions are not deduplicated.** When an endpoint declares an error the API-level middleware also declares, the document emits the `$ref` twice; 47 operations at TIP. The contract deduplicates, the projection does not. | e.g. `v2.session.fork` 404 |
| O5 | **A misleading diagnostic.** `"inline nominal schema was not promoted into the graph"` fires when a `$ref` target merely has no *name* — `TypePlanBinder.BindReference` falls through to `BindCore` on the target node. The schema is in the graph and promoted. This misattribution is what first framed an entire batch as "inline promotion" work when it was naming work. | `TypePlanBinder.cs:56-76` |
| O6 | **`schemaAliases` has a blind spot.** Its structural-identity test cannot distinguish semantically different types that project identically. `Money.USD` and `Money.USDPerMillionTokens` are byte-identical `{"type":"number"}`; an alias row collapsing them would be accepted. The mechanism is still an explicit human act, so this is a limit of the guard, not an active defect. | §3.3 |

### 3.2 The PIN's problems — and which of them TIP already solved

| # | Finding | At PIN | At TIP |
|---|---|---|---|
| P1 | **The duplicated `Form.*` generation.** Twelve `Form.…1`/`Fields3` schemas reachable only through the events, colliding with the operation-side family. This is what blocks ten form/integration operations today. | present | **gone** |
| P2 | Root cause of P1: `Schema.Number` rendered two ways — the operation side wrapped the special-number union in a redundant second `anyOf`. Both admit exactly the same value set. | divergent | **converged** |
| P3 | `pty.update`'s promoted inline `size` member derived `V2PtyUpdateSize`. | present | n/a |

P1/P2 were fixed upstream by the same effect upgrade that broke the streams — one bump, one
repair, one regression. **Verified by experiment, not inference:** running the restore step
against TIP leaves the `Form.*` name set byte-identical before and after, with exactly one
conflict (`Form.Info`, differing only in which of three byte-identical `Form.Fields` aliases it
references — so the merge's `??=` loses nothing).

P3 was ours and is already closed by a reasoned schema-name row.

### 3.3 TIP's problems — the refresh's price

| # | Finding | Consequence |
|---|---|---|
| T1 | **The SSE payload regression.** `contentSchema` gone, the event union pruned. Of the PIN's 144-schema event closure, **120 are absent at TIP**. | Blocks the refresh entirely: refreshing today would delete shipped public surface, not merely fail to add. Tracked as #56 / #44911. |
| T2 | **Authentication is invisible.** `securitySchemes: {}`, and all 131 operations carry `security: []` — the document asserts the API is unauthenticated. The server requires Basic auth or `?auth_token=`. Upstream's `Authorization` middleware declares an `error` but never an `HttpApiSecurity`, and Effect emits schemes only from the latter. | A document-only client 401s on every call. Ours works only because the password is carried by hand. |
| T3 | **Eight `persistentPty.*` operationIds lack the `v2.` prefix** — the group annotates an identifier on one of its nine endpoints, so the rest fall through to Effect's default. | Our ingestion refuses all eight outright. |
| T4 | **A base64 core shape we do not know**: `PersistentPty.Snapshot.checkpoint` is `{type: string, format: byte, contentEncoding: base64}`. | Ingestion refusal. |
| T5 | **Two shipped operations were removed upstream** — `session.question.reject` and `session.question.reply`, deleted with the legacy question service (`9872fb8a54`). | The refresh removes them from our public surface. |
| T6 | **Descriptions are dropped positionally.** 42 of 57 `annotate({description})` blocks never reach the document; `Config.Info` has 25 written and zero delivered. The rule is mechanical: an annotation applied after a transformation lands on the Type side, and the projection reads the encoded side. | Generated XML docs are silently empty where upstream wrote prose. |
| T7 | Upstream's own committed `openapi.json` is **stale** against its own generator (regenerating adds `ProjectNotFoundErrorEncoded`). | Their `check:generated` gate is not holding. |

### 3.4 Both points — structural properties of the projection

These are not regressions; they are what an OpenAPI projection of Effect Schema *is*. They will
not be fixed by a refresh and must be absorbed by our dialect.

- **Brands are erased.** 36 branded schemas, none recoverable. `Session.ID` and `Project.ID` are
  both `{"type":"string"}`; `RelativePath` and `AbsolutePath` are indistinguishable.
- **Domain types behind transformations are unrecoverable.** `DateTimeUtcFromMillis` → plain
  `{"type":"number"}` at ~30 positions, with no `format` and no description: we emit `double`
  for what is a UTC instant. Numeric query constraints (`isInt`, `1..200`, `PositiveInt`) are
  erased to `string?`; `shell.output?cursor` degrades to a numeric-looking pattern that admits
  `-5` and `3.7`. The document contains zero `minLength`/`maxLength` keywords.
- **No discriminator, ever.** 22 `toTaggedUnion` call sites; the word `discriminator` appears
  nowhere in the document. Our structural marker recovery is not a preference — it is the only
  available route.
- **`undefined` and `null` are conflated** (`Undefined` projects as `{"type":"null"}`), so a
  field that may be absent and a field that may be JSON-`null` look alike.
- **The projection *adds* shapes.** Every plain `Schema.Number` becomes a four-arm union
  (`number | "NaN" | "Infinity" | "-Infinity"`) at 29 positions — the same mechanism behind P2 —
  and inside a union it creates real ambiguity: in `Form.When.value` the input `"NaN"` matches
  two branches. Empty structs become `{"anyOf":[object, array]}` at 12 positions.
- **`/api/fs/read/*`** ships a literal `*` segment with no path parameter, in both documents.
- **The server's behaviour exceeds the contract**: a 503 `{code, message, action}` envelope
  reachable on every route, a process-level `/api/health` answering 500/503 before the API
  layer, CORS preflight, non-`/api` paths serving the embedded SPA, and the PTY WebSocket's real
  framing (the document says `success: boolean`).

## 4. What our generator can take at TIP+RESTORE

With the restore applied and T3/T4 set aside so ingestion can proceed: **123 operations ingest,
92 bind, 31 are refused.** No currently-shipped operation is refused — the refresh does not
break the bound surface; it only removes T5's two.

| refusal class | ops |
|---|---|
| envelope payload not a required named ref | 8 |
| success payload not a named ref | 6 |
| location envelope `data` not a named ref | 4 |
| errors not in Effect `_tag` style | 5 |
| query walls (required / non-null / unsupported shape) | 5 |
| naive pluralization | 4 |
| payload/response-spine collision | 2 |
| WebSocket | 2 |
| structural-union overlap (`config.get`) | 2 |
| wildcard path | 1 |

New families among the refused: `worktree.*` (4), `workspace.create`, `vcs.branches`,
`session.stats`, `persistentPty.connect`.

## 5. The choice space

Each row is a decision the maintainer owns. Options are stated with their consequence, not
ranked.

**C1 — The header channel (O1/O2).** ADR-0008 reserves exclusion for transports HTTP cannot
carry, so dropping `pty.connect.token` is not available: it is a plain POST, and it is the
handshake for the PTY WebSocket client that ADR-0008 already plans as hand-written. Options:
(a) extend `OpenCodeRequestOptions` with per-request headers — one change serving connect-token,
multi-project targeting, and future header channels; (b) carry the fixed `x-opencode-ticket: "1"`
in curation and emit it automatically, since it is a protocol constant rather than user input;
(c) both, with (a) as the general channel and (b) removing a footgun. Until one lands, the
generated method cannot succeed and that should be stated wherever the surface is described.

**C2 — The envelope class (O3).** Eighteen of thirty-one refusals. Independent of the refresh
in both directions: the refresh neither fixes it nor makes it worse. Options: extend the `Data`
envelope binding to arrays and dictionaries; or keep the wall and accept that a fifth of the
surface stays out.

**C3 — The refresh (T1–T6).** Blocked on upstream restoring the stream payloads. The lever is
ours: we offered the patch and have now verified it works and is safe (§3.2). Sending it
converts ten blocked operations and the entire form class into a side effect of a change we
were going to make anyway. Choosing not to send it leaves both the stream surface and the form
class blocked indefinitely.

**C4 — What else to report upstream.** T2 (a missing `HttpApiSecurity` declaration), T3 (eight
operationIds off-convention), T6 (25 lost `Config.Info` descriptions), T7 (the stale committed
document), and the undeclared `x-opencode-ticket` value. All are the same class as #44911:
invisible to upstream because their own clients generate from the Effect contract, material to
anyone consuming the document.

**C5 — Dialect absorption (§3.4).** Brands, timestamps, and numeric constraints are gone for
good. Options per class: accept the widened type; recover it through reasoned curation rows;
or refuse and exclude. The timestamp case is the most consequential — `double` versus a typed
instant is a public-API decision, not a generator detail.

**C6 — Alias policy (O6).** Whether the structural-identity guard needs a companion rule
(for example, refusing an alias between two schemas that carry distinct upstream identifiers)
or whether the explicit-human-act requirement is protection enough.

## 6. Method note

Two claims in this session were asserted before they were verified — that the duplicated form
generation was an upstream artifact, and that a refresh would resolve it. Both later proved
true, but the reasoning offered for them at the time was inference presented as evidence. Both
are now backed by executed experiments (upstream source read at the pinned commit; the restore
step run for real). The distinction is recorded here because the same failure mode is what a
fail-closed generator exists to prevent.

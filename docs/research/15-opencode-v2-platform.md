# The opencode v2 platform: branch, surface, architecture, distribution

Date: 2026-08-16

> Dated evidence and decision history, not current policy. Follow current canon through
> `AGENTS.md`; `spec/SNAPSHOT.md` owns the current protocol pin.
>
> Retarget-decision research (docs 09/10 follow-up). Question: what is the opencode v2 platform
> today — which branch carries it, what API surface it serves, how its server/client architecture
> and its distribution work, and what retargeting this SDK onto it changes. Sources, retrieved
> 2026-08-13: `anomalyco/opencode` branch `v2` at head `1288161` ("chore: generate"), inspected
> via shallow clone; the GitHub REST API (branch head, commit list, releases, default branch); the
> npm registry (`@opencode-ai/cli` and `opencode-ai` dist-tags); live probes of
> `update.opencode.ai`; and a live install and run of `@opencode-ai/cli@next` (0.0.0-next-17403)
> on this machine. §5a and the auth detail in §6 were re-derived 2026-08-14 against the pinned
> spec commit `a6a712a` directly (submodule checkout, `git grep`/`git show` at the pin). This
> document is the dated platform picture that supported the retarget; docs 09 and 10 remain the
> dated v1-line genealogy it extends, and research log session 12 (Q58) recorded the branch's
> first sighting.

## 1. The active branch is `v2`, and the investment is visibly there

- Branch head `1288161` was committed 2026-08-13T07:18:58Z ("chore: generate" — the spec
  artifact regenerating, §3). **15 commits landed on 2026-08-13 alone**, a full-width mix:
  `fix(app): unify v2 server and session lifecycle`, `feat(app): redesign non-modal settings`,
  `ci: make v2 publishing manual`, `feat(deskltop): use opencode2 in WSL`, core/tui fixes,
  test refactors, docs, and two separate `chore: generate` runs.
- The **default branch remains `dev`** — the 1.x maintenance line, which released v1.18.18 on
  2026-08-13T01:15Z. Every GitHub release on the main repository is still a `v1.18.x` tag.
- The monorepo is restructured: ~37 packages (`core`, `server`, `client`, `protocol`, `cli`,
  `sdk-next`, `desktop`, `tui`, `schema`, `util`, `updates`, `httpapi-codegen`, …).
  **`packages/opencode` — the source of the 127-operation legacy root block — does not exist
  on the branch**, and neither does `packages/sdk`.
- v1's end is written in code: `packages/protocol/src/groups/migration.ts` declares
  `v2.experimental.migration.v1.status` — *"Return the progress of the V1 to V2 session history
  migration."* — with a `V1MigrationStatus` union (`required`/`completed`, `running` + progress,
  `error`). v2 absorbs v1 installations in place; the v1 line is being sunset, not forked from.

Unlike the frozen April `2.0` branch (doc 10's genealogy correction), this branch shows every
liveness signal that one lacked: daily volume, a regenerating committed artifact, migration
machinery, and a client stack that has already moved (§2).

## 2. One surface — the dual-surface problem does not exist on v2

- `packages/server/src/handlers/` and `packages/protocol/src/groups/` are **1:1 mirrors — 30
  files each** (agent, command, config, credential, debug, event, form, fs, generate, health,
  integration, location, mcp, message, migration, model, permission, plugin, project,
  project-copy, provider, pty, question, reference, server, session, shell, skill, vcs,
  websearch). The server serves the protocol surface and nothing else; no legacy route layer
  exists anywhere on the branch.
- The TUI depends on `@opencode-ai/client` — whose `contract.ts` is one line, re-exporting
  `ClientApi` from `@opencode-ai/protocol/client` — not on the old `@opencode-ai/sdk`. The
  client ships `effect/` and `promise/` flavors over the same protocol contract, plus the
  background-service discovery machinery (§6).
- `sdk-next` is a separate concern: an in-process embedding host, not the network client —
  its README: *"The SDK executes Server's assembled HTTP router in memory. It opens no listener
  and performs no network I/O"* and *"This transitional package will replace the existing
  generated `@opencode-ai/sdk` after its consumers migrate."*

v1's defining problem — two generations served simultaneously, with upstream's own TUI calling
both — is a v1-line artifact. On v2, server, client, TUI, and desktop all speak one surface.

## 3. The spec artifact is maintained, checked, and self-consumed

`packages/protocol/openapi.json` (681 KB): OpenAPI **3.1.0**, title "opencode HttpApi", info
version `0.0.1`, self-described *"Experimental HttpApi surface for selected instance routes."*
**104 paths / 120 operations / 324 schemas**, every path under `/api`, operationIds still
`v2.`-prefixed (the prefix-strip naming policy carries over unchanged).

The artifact is not a dump: `packages/protocol/package.json` owns
`"generate": "bun run script/generate-openapi.ts"` and `"check:generated": "… --check"` — their
own regen-verify — and the branch head itself is a `chore: generate` commit. Their own CLI
consumes it: `opencode2 api` takes *"OpenAPI operation ID, or an HTTP method followed by a
path"*. First-class producer, first-class consumer — low rot risk for anyone generating from it.

## 4. Surface diff against our pinned v1.18.15 modern block: 61 → 120

Eight pinned-block operations are gone: `v2.integration.attempt.cancel` / `.complete` /
`.status`, `v2.integration.connect.oauth` (the attempt/connect model reshaped into
`integration.command.*` ×3 + `integration.oauth.*` ×4), `v2.pty.connectToken` (now
`v2.pty.connect.token`), and — significant for M3 — `v2.session.events`, `v2.session.history`,
`v2.session.messages` (§5, stream reshape).

67 operations are new. By family: `session.*` grew from 25 to **48** (forms ×6, inbox ×4,
instructions ×3, plus `export`/`import`/`fork`/`rename`/`move`/`remove`/`shell`/`skill`/
`synthetic`/`log`/`command`/`background`/`generate`); **`mcp.*` ×6** (add, connect, disconnect,
list, remove, resource.catalog); **`shell.*` ×6**; `integration.*` reshaped to 10;
**`vcs.*` ×3**; `project.*` ×3; `websearch.*` ×2; `debug.*` ×2; plus `config.get`, `agent.get`,
`model.default`, `plugin.list`, `server.get`, `health.stop`, `generate.text`, `form.request.list`,
`message.list`, `experimental.migration.v1.status`, `experimental.integration.wellknown.add`.

| Family | Ops | Family | Ops |
|---|---|---|---|
| session | 48 | fs, permission, project, projectCopy, vcs | 3 each |
| integration | 10 | agent, credential, debug, experimental, health, model, provider, websearch | 2 each |
| pty | 7 | command, config, event, form, generate, location, message, plugin, question, reference, server, skill | 1 each |
| shell, mcp | 6 each | | |

The consequence for doc 10's **78-operation capability gap**: it is being absorbed into the
protocol surface. `mcp`, `config`, `vcs`, `project` — the families that forced ADR-0005's
"generate both surfaces" — now exist as modern operations, joined by new capability (`shell`,
`websearch`, `generate`). The 13-operation `tui.*` remote-control surface is **gone entirely** —
not migrated, removed.

## 5. Dialect census (head `1288161`)

| Construct | Count | Note |
|---|---|---|
| `allOf` | 420 | **100% single-element**, validation-only wrappers (`{"type":"integer","allOf":[{"exclusiveMinimum":0}]}`) |
| `const` | 0 | the April const shift stays reverted |
| single-value `enum` | 370 | the literal-marker dialect, unchanged |
| `anyOf` | 446 | discriminator-free unions, unchanged |
| `oneOf` / `discriminator` | 1 / 0 | |
| `prefixItems` | 6 | |
| `patternProperties` | 2 | |
| empty schemas `{}` | 9 | |

Four shapes inside those counts only surface when an operation's closure is actually admitted;
the stream operations are what first reached them (2026-08-16, by admitting `v2.session.log`
and `v2.event.subscribe` to the profile and reading the walls):

- **Single-value `enum` is not always a string.** `durable.version` is a numeric literal marker
  across the 40-schema durable event family — `enum: [1]` on 35 of them and `enum: [2]` on the
  other 5, so the envelope carries two versions rather than one. All 41 numeric single-value
  enums in the spec declare `type: number`; none declare `integer`. The 370 count above does not
  split by type; the string-literal reading of the dialect was incomplete.
- **A leaf schema can belong to more than one union.** 39 of `Session.Event.Durable`'s 40
  branches are also direct branches of the 87-branch `V2Event`, so the durable log stream and
  the live bus share most of their payload types. Census of all 14 all-`$ref` unions: 41 leaves
  carry two parents, in three families — the 39 above, plus the `Tool.Content`/`Tool.Content1`
  and `Form.Field`/`Form.Field1` spec-gen duplicates. Every one of them discriminates on `type`
  in both parents. This is what ADR-0011 answers.
- **An empty struct renders as a two-branch union.** `Session.Inbox.CompactionPayload` is
  `anyOf[{"type":"object"},{"type":"array"}]`, which is how Effect emits
  `Schema.Struct({})` — upstream's own generated client types it as `{}`. It is a declaration
  that there is no payload, not an undiscriminated union of two real shapes.
- **Some `anyOf`s are refinements over one primitive.** `session.instructions.updated`'s
  `data.delta` is a map whose values are `anyOf[string(pattern ^[a-f0-9]{64}$),
  string(enum ["removed"])]` — both branches are strings, so the construct is a
  string-to-string dictionary rather than a union.
- **The structural-duplicate family extends to union parents.** `Tool.FileContent1` is
  byte-identical to `Tool.FileContent`, which makes the inline union `Tool.Content1`
  identical to `Tool.Content`; because `Tool.TextContent` is a branch of both, the duplicate
  surfaces as one branch needing two parents. Same upstream spec-gen behavior already seen on
  `InvalidRequestError1` and `Shell.Info1`, and the same `schemaAliases` collapse resolves it.

Closure cost differs sharply between the two stream operations: admitting `v2.session.log`
reaches 2 of the empty-struct/refinement sites, `v2.event.subscribe` reaches 16. Both reach
the same numeric-literal, duplicate-parent and nested-union walls.

Component names are widely dotted (`Session.Message.Info`, `Location.Info`) — the existing
mangling rule covers them. Two-day drift against session 12's census of the same branch
(2026-08-11: 322 schemas, 422 `allOf`, 359 single-value enums) measures the churn rate directly
— the branch moves daily, which is exactly what snapshot pinning is for.

### 5a. Parameter-placement census at the pin (`a6a712a`, 2026-08-14)

120 operations: 59 GET, 44 POST, 12 DELETE, 3 PUT, 2 PATCH. Placement facts that bound the
SDK's request-marshalling design:

- **Header parameters: zero.** No operation declares an `in: header` parameter; the
  ambient location headers (§6) are middleware-level and spec-invisible.
- **`location` deepObject: 61 operations** carry an optional `location` query parameter
  (`style: deepObject`, `explode: true` — wire shape `location[directory]=…&location[workspace]=…`,
  schema `anyOf[{directory?, workspace?}, null]`).
- **Body + query mixing: 15 operations** (POST/PUT/PATCH/DELETE) — and in every one of them
  the *only* query parameter is `location`. The merged-Request marshalling question and the
  location question are the same design.
- **Flat location fields: exactly one operation** — `v2.session.list` carries
  `directory`/`workspace`/`project`/`subpath` as plain query parameters instead of the
  deepObject; the platform-wide mechanism is the deepObject.

Side observation validating the Q83 rename: the v2 first-party generated client emits one
uniform `{Op}Input` type per operation (`SessionListInput`, `SessionPromptInput`, …) —
upstream's own rendering of the uniform-request idea.

**Streaming reshape.** `text/event-stream` lives on `v2.event.subscribe` and `v2.session.log`;
`v2.pty.connect` carries `x-websocket`; `x-effect-stream` rides both SSE operations
(2026-08-13 ingestion-level correction of this session's earlier jq census). The v1 durable
stream pair (`v2.session.events` + `v2.session.history`, `after`-cursor resume) is replaced by
**`v2.session.log`** (`/api/experimental/session/{sessionID}/log`, params `after` + `follow`) and
cursor pagination on `v2.message.list`. The global `/api/event` stream has **no resume
parameters**. Cursor/after parameters exist on exactly: `session.list`, `session.log`,
`message.list`, `pty.connect`, `shell.output`. The locked SSE-resume design premise must be
re-derived against `session.log` at M3. How the two streams differ on the wire — and why
their identical spec declarations do not mean identical bytes — is in research doc 02.

## 6. Server-client architecture: one shared daemon, discovered by file

The background-service model replaces v1's per-invocation `opencode serve` habit:

- **Registration file** in the XDG state directory, 0600, content
  `{id, version, url, pid, password}`. The code's own words: *"That file is the complete
  discovery contract — reading it is all a client needs to connect."*
- **`Service.ensure`** (in `@opencode-ai/client`): reuse a healthy, version-compatible server;
  replace a version-mismatched one; otherwise spawn detached, `unref`'d contenders (default
  command `opencode serve --service`) — racing clients may spawn several, one wins the
  registration, losers exit. The daemon survives its parent.
- **Every client uses the same flow**: the TUI root command (via `ServerConnection.resolve` —
  "Starting background server…" / "Restarting background server (version mismatch)…"), the
  `api` command, and the desktop app (`background-cli.ts` runs `Service.ensure` with its
  bundled CLI; isolated/dev mode redirects `XDG_STATE_HOME` to its own userData for a private
  service).
- The server is **project-agnostic**: operations take `location[…]` query addressing
  (`LocationQuery`), sessions carry `location.directory`, storage is central — one daemon hosts
  every project (hence "session history migration", §1).

CLI surface (root description: "OpenCode 2.0 preview command line interface"):

| Command | Behavior |
|---|---|
| `serve` | *"Start the v2 API and web server"* — `--hostname`, `--port`, `--service`, `--stdio` (the last two cannot combine) |
| `service start/stop/restart/status/get/set/unset` | managed background daemon |
| `pair` | prints server URLs + username `opencode` + password + a QR code (uqr) |
| root `--standalone` / `--server <url>` | private server / explicit remote instead of the daemon |
| `acp` | *"Start an Agent Client Protocol server"* (§7) |
| `api`, `mini`, `run`, `export`, `import`, `mcp`, `models`, `plugin`, `auth`, `console`, `debug` | client-side utilities over the same surface |

**`serve --stdio`** is lifecycle-over-stdio, not HTTP-over-stdio: a real HTTP server whose
stdout handshake is `JSON.stringify({url})` and whose lifetime is `waitForStdinClose()` — parent
dies, server exits. It scrubs `OPENCODE_PASSWORD`/`OPENCODE_SERVER_PASSWORD` from its
environment and generates a random password. Made for embedding hosts (the desktop uses the
bundled CLI this way).

**Auth (re-derived at the pin, 2026-08-14)**: HTTP Basic, and **optional** — the server's
`ServerAuth.required` is true only when a password is configured and non-empty; otherwise the
authorization middleware is a pass-through (`packages/server/src/middleware/authorization.ts`).
The username is `opencode` at the pin: the pinned server layer hardcodes it, while the
`--username`/`-u` and `OPENCODE_SERVER_USERNAME` controls exist only as upstream direction
(CLI/server docs; the desktop WSL sidecar exports the variable) that has not reached the
pinned server implementation. Client-side password resolution lives in the *consumer*, not the client library:
`packages/cli/src/env.ts` resolves `OPENCODE_PASSWORD` falling back to the legacy
`OPENCODE_SERVER_PASSWORD` and hands the value to the client — the generated client itself
reads no environment. Service mode generates `randomBytes(32).toString("base64url")` when
unconfigured; foreground `serve` prints the password when it generated one.

**Location addressing is dual-channel (re-derived at the pin, 2026-08-14; measured
2026-08-16 — research log Q95)**: the server's location middleware
(`packages/server/src/location.ts`) resolves per request as `location[workspace]` query
**or** `x-opencode-workspace` header, and `location[directory]` query **or**
`x-opencode-directory` header **or** the server's cwd — precedence query > header. Three
mechanics matter and are easy to get wrong:

- **Precedence is per field, not per location object**, and the code uses `||`, so an empty
  query value falls through to the header. An ambient `{directory, workspaceID}` plus a
  per-request query carrying only `directory` resolves to the per-request directory and the
  **ambient** workspace.
- **The middleware is attached per group** (`packages/protocol/src/api.ts:150-180`), not per
  endpoint that declares the parameter. It therefore reaches operations with no location
  query parameter at all (`project.list`, `permission.saved.*`) and is inert on the groups
  without it (health, server, message, event, debug, migration) and on session-scoped
  endpoints, which resolve location from the session DB row and ignore both channels.
- **Only the directory header is percent-decoded** server-side; the workspace header is read
  verbatim, so a client's escaping must be asymmetric to match.

The headers are absent from the document because they were never expressible in it, not
because they were removed: the spec is `OpenApi.fromApi(ClientApi)` over declared endpoint
schemas, and middleware contributes nothing to the parameter surface — `Authorization` is
invisible for exactly the same reason while remaining mandatory. The per-operation `location`
deepObject query parameter (61 operations, §5a) is the spec-visible channel the first-party
generated client uses exclusively; the headers are the spec-invisible ambient channel other
consumers (drive driver, CLI) ride. The SDK-side
rendering — per-op request property vs ambient client/request-option default, plus the
deepObject marshalling and the `session.list` flat-field exception — is the location +
merged-Request design session that opens M3 planning (research log Session 22).

## 7. MCP is client-side only; the exposure protocol they chose is ACP

The `mcp` protocol group (6 ops, including `mcp.resource.catalog`) manages the MCP servers
opencode itself connects to: *"Retrieve configured MCP servers and their connection status"*,
*"Add an MCP server at runtime or replace an existing one, connecting it immediately."*
`packages/core/src/mcp/` is `client.ts` / `stdio.ts` / `oauth.ts` over
`@modelcontextprotocol/sdk` 1.29.0 (patched at the root); the CLI's `mcp` handlers are
add/auth/list/logout/resolve. **No MCP-server exposure exists anywhere on the branch.** The
protocol opencode exposes *itself* over is **ACP**: `opencode acp` — *"Start an Agent Client
Protocol server"* — aimed at editors driving the agent.

Consequence: the ecosystem gap this repository's MCP-server premise rests on (research docs
03/04) persists on v2 — arguably validated, since upstream sees the expose-the-agent need and
chose the editor-side protocol. ACP is an adjacent surface to watch: it can absorb some
"drive opencode from another agent" use cases, but MCP clients remain unserved.

## 8. Distribution: scoped npm CLI + a desktop update service, side by side with 1.x

- **The v2 CLI publishes as `@opencode-ai/cli`** (scoped — not the old `opencode-ai` package):
  dist-tag `next` = `0.0.0-next-17403`, registry modified 2026-08-13T05:11Z, build-number
  versioning. It installs side by side as **`opencode2`** (`OPENCODE_CLI_NAME` build-time
  rename; opencode.ai/v2/docs: "OpenCode 2 installs and runs as opencode2. It does not replace
  OpenCode 1's opencode binary"). The old `opencode-ai` package's `next` tag is a June-27
  fossil; both packages share `beta`/`dev`/`latest` tags for the 1.x line — two families, one
  registry namespace.
- **The desktop beta rides their own update service**: `update.opencode.ai`, a Cloudflare
  Worker (D1 artifact table; OIDC-authenticated `POST /api/publish` against the GitHub Actions
  JWKS; channel feeds at `/api/{channel}`). Live probe of `/api/beta`: desktop
  `0.0.0-beta-17406`, released 2026-08-13T09:04Z, artifacts on the **separate
  `anomalyco/opencode-beta` repository's** GitHub Releases (dmg/deb/rpm/AppImage/exe +
  electron-updater manifests). `/api/dev` and `/api/prod`: "Channel not found" — beta is the
  live channel.
- Non-prod desktop builds **bundle `opencode-cli*` binaries** (electron-builder
  `extraResources`) — desktop-beta users get CLI+server without npm. Channel app IDs:
  `ai.opencode.desktop{.dev,.beta,}`.
- The same `opencode-beta` repo also hosts timestamped releases (`v0.0.0-beta-202608110357`,
  37 assets) matching the old npm `beta` dist-tag exactly — the 1.x CLI beta pipeline. Two
  pipelines, one artifact repository.
- `publish.yml` maps `ref_name == 'v2'` → channel `next`; `ci: make v2 publishing manual`
  landed 2026-08-13 — v2 publishing is currently a manual dispatch.

## 9. Live verification on this machine (2026-08-13)

```text
$ npm install -g @opencode-ai/cli@next      # postinstall blocked by local allow-scripts
$ opencode2 --version                       #   policy; binary functional regardless
opencode2 v0.0.0-next-17403
$ opencode2 service status
stopped                                     ← installing starts no server
$ opencode2 serve --hostname 127.0.0.1 --port 41999
server listening on http://127.0.0.1:41999
server password <44-char random base64url>
$ curl http://127.0.0.1:41999/api/health                       → HTTP 401
$ curl -u opencode:<password> http://127.0.0.1:41999/api/health → HTTP 200
{"healthy":true,"version":"0.0.0-next-17403","pid":3829434}
$ curl -u opencode:<password> "http://127.0.0.1:41999/api/session?limit=2"
{"data":[{"id":"ses_0706…","title":"…","location":{"directory":"/home/deniz/src/…"}}, …]}
```

Three facts proven live: auth is enforced (401 without credentials); the health response is the
reshaped `ServiceHealth` (`{healthy, version, pid}` — richer than v1's bare `{healthy:true}`);
and `session?limit=2` returned this machine's **real pre-existing sessions** in the `{data}`
envelope — the central store (and the v1 data path into it) works.

## 10. Consequences for this SDK

The maintainer sealed the retarget (2026-08-13): **single surface, v2 protocol target** —
ADR-0005 revised in place; the M1 Arc B plan gains a retarget task; the legacy hub (old M5) is
never built.

**Carries over verbatim:** the Basic-auth decoration design; both selected M1 operations
(`v2.session.message` shape-identical with its `{data}` envelope; `v2.health.get` present with a
reshaped response); the discriminator-free `anyOf` union dialect and with it ADR-0009 and the
converter machinery; the launcher premise (M4 maps to `opencode2 serve`; `--stdio` is a
lifecycle-leash bonus; connect-or-launch fits the discovery-file model).

**Changes at retarget:** the ingestion wall needs one new admit rule (single-element,
validation-only `allOf` unwrap); `HealthResponse` maps `ServiceHealth`; the
`location[…]`-query vs ambient-header decoration policy is settled as a design input (§6:
dual-channel, query > header) and its SDK rendering is designed in the location +
merged-Request session at M3 planning; the M3 durable-stream design is re-derived against
`session.log` (`after` + `follow`).

## Unverified at the snapshot

- The beta-desktop→v2-branch CI wiring is not traced line-level; the link is inferred from the
  artifact shape (desktop-only), the distinct build-number versioning, same-day cadence with
  branch activity, and the branch's electron-builder config.
- v2 GA timing and channel stabilization — no `latest`-equivalent channel exists yet;
  `@opencode-ai/cli@next` cadence is unclear now that v2 publishing went manual (2026-08-13).
- `session.log` semantics versus the v1 durable stream were unresolved at this snapshot. Research
  doc 02 now records the pinned implementation and live default-server behavior.
- The v2-side error-response shapes of the selected M1 operations were unresolved at this snapshot;
  M1 later bound and generated their declared status maps.
- How long the 1.x line keeps publishing releases alongside v2.

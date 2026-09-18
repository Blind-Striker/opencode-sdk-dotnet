# OpenCode.Sdk.Sandbox

A committed local playground for driving the SDK against a real `opencode serve` under a
debugger. It rides the repository's full convention set (analyzers, `.editorconfig`, format
gate) — unlike `.scratchpad/`, which remains the home for throwaway prototypes that answer a
question and disappear.

Configuration comes from environment variables, prefilled for the IDE by
`Properties/launchSettings.json` (profiles `sandbox-session-log` and `sandbox-events`):

| Variable | Meaning |
|---|---|
| `OPENCODE_SANDBOX_ENDPOINT` | Absolute server endpoint (required) |
| `OPENCODE_PASSWORD` / `OPENCODE_SERVER_PASSWORD` | Resolved by sandbox code and passed as `OpenCodeClientOptions.Password` — the SDK itself reads no environment |

## Running

Start a server with a fixed password so the checked-in profile matches (`serve` adopts the
same `OPENCODE_PASSWORD` variable; without it the server generates and prints a
random one):

```sh
OPENCODE_PASSWORD=123456 opencode serve --hostname 127.0.0.1 --port 4096
```

Then F5 with one of the sandbox profiles, or run either stream mode directly:

```sh
dotnet run --project tests/OpenCode.Sdk.Sandbox -- --stream
dotnet run --project tests/OpenCode.Sdk.Sandbox -- --events
```

**Pointing the sandbox at a server that is not the checked-in one takes
`--no-launch-profile`.** `Properties/launchSettings.json` prefills
`OPENCODE_SANDBOX_ENDPOINT` at port 4096, and `dotnet run` applies the default profile unless
told not to, so without the flag the run silently addresses 4096 whatever the environment says.
The prefill stays: it is what makes the zero-argument F5 and `dotnet run` work against the local
server every other line here assumes. A second fact belongs beside it — the standing walkthrough's
earlier session legs answer 500 on a server with no provider configured, and because those legs
run first, the PTY and persistent PTY legs are unreachable there. That is the real server's
answer, not something the walkthrough should swallow: it asserts what the server says, so an
isolated provider-less server is a server this leg cannot be driven against. `PersistentPtyLiveTests`
is what proves the persistent PTY round trip; on a Windows workstation that needs a Linux-hosted
server (`docs/engineering/developing-on-windows.md`).

The stream example composes through the Extensions package:
`AddOpenCode` registers one singleton client family, and the Generic Host injects its
`SessionsClient` into `SessionLogWorker`. The worker creates a session, obtains its bound
`SessionClient`, and follows `GetLogAsync` through the host's normal `stoppingToken`.
Each frame is logged with its generated runtime type. Ctrl+C stops the host, cancels the
open response read, and disposes the singleton SDK transport with the container.

The event mode injects `EventsClient` into `EventBusWorker` and consumes the volatile global bus.
After it reports that the bus is opening, trigger server activity from another process; running the
standing breadth walkthrough without a mode flag creates a session and supplies representative
events. The bus has no replay or resume contract: events during disconnection are missed, and a slow
consumer can overflow and fail the stream. Ctrl+C exercises the same host cancellation path.

`--stream` and `--events` are mutually exclusive. Run without either flag to keep driving the
standing breadth walkthrough: status, session create/list/get, message list, experimental export
with its sanitize query, permission create/get/reply, compact and fork, interrupt and DELETE
revert-clear, experimental instructions and MCP mutations, PTY update, and a typed
`FormNotFoundError` through NoThrow. The envelope leg reads `Vcs.ListBranchesAsync`, the resolved
directory, the session-active dictionary, `Server.GetStatusAsync().ServerStatus.Urls`, and session
context. The PTY and persistent PTY legs use the same Extensions registration.

The PTY leg (`PtySessionWalkthrough`) is the hand-written family's live proof (ADR-0021). It
creates a PTY, lists the family, mints a connect ticket through the token door — whose
`x-opencode-ticket` header the SDK applies internally — and then opens the WebSocket session
**ticket-less**, carrying the client's Basic credential on the upgrade request, which is the
designed non-browser path. It records the replay frames and the single cursor frame that ends
the replay, writes `echo hello`, reads until the terminal echoes it, reconnects at the observed
cursor to show that a resume replays only what came after it, and finally removes the PTY while a
read is in flight so the normal close ends the enumeration rather than faulting it.

The persistent PTY leg (`PersistentPtyWalkthrough`) follows it over the second hand-written family,
whose live socket is the inverse wire: binary output frames and framed binary input. Which arm runs
is the server's own answer rather than a flag. Where the `opencode-pty` daemon exists (Linux and
macOS; the daemon ships no win32 package) it creates a terminal for the walkthrough's session,
lists it, attaches as controller, writes `echo sdk-live` terminated by a line feed — Enter on these
terminals' Unix line discipline — reads until the output carries the echo, resizes to 100x30 and
reads the `resized` frame the server answers with, reads the same terminal back over HTTP (the
controller attach is what selected it for that route), and removes it. Where the daemon does not
exist, `create` is the only route that fails, so the leg records the declared 503 naming
`opencode-pty` beside the daemon-absent answers of the others:

```text
ppty-create: status=503 service=opencode-pty error=ServiceUnavailableError
ppty-list:   status=200 ptys=0
ppty-read:   status=200 read=<null>
ppty-handoff: status=200 handoff=<null>
ppty-shutdown: status=204 isError=False
```

## Standalone server demo (`--standalone`)

`StandaloneServerWalkthrough` is the M4 launcher demo leg: unlike every mode above, it needs no
`OPENCODE_SANDBOX_ENDPOINT` and no ambient server — the SDK starts and owns the server itself
through `OpenCodeServer.StartAsync` (the standalone-start connection mode; `docs/architecture/
client-runtime.md` §Connection modes), then calls `CreateClient()` and `Server.GetStatusAsync` under a
5-second-bounded probe, the same recipe door 2 (explicit endpoint) would run against a
caller-supplied endpoint. It is checked before the `OPENCODE_SANDBOX_ENDPOINT` gate, so it is the
only mode reachable without a running server.

Status is followed by `ModelSelectionWalkthrough`, the executable "choosing a model" recipe.
It observes provider/model catalogs, creates a session with an available `ModelRef { ProviderId, Id }`,
and removes it. Catalogs can still be incomplete during asynchronous plugin activation
(`CONTEXT.md`, Plugin activation). An empty observation prints `model: none currently available`
and returns successfully; it does not prove that activation has settled or that no provider is
configured.

`OPENCODE_SANDBOX_SERVER_COMMAND` overrides the launched command (`|`-separated, to survive paths
with spaces); unset uses the product default (`opencode serve`, resolved from `PATH` the way a
shell would). Run from the
repository root against the pinned submodule source:

```sh
OPENCODE_SANDBOX_SERVER_COMMAND="bun|--cwd=$(pwd)/external/opencode/packages/cli|src/index.ts|serve" \
  dotnet run --project tests/OpenCode.Sdk.Sandbox --no-launch-profile -- --standalone
```

The `--cwd=<abs>` token is load-bearing, not decorative: the source-run server's workspace/JSX
preload discovery (`@opentui/solid/preload`, wired through `packages/cli/bunfig.toml`) walks from
bun's own process working directory, not from the entry file's path — an absolute entry path with
the launcher's default (unset) working directory reproduces upstream's own `bun run --cwd
packages/cli src/index.ts` shape one token short and fails before readiness with `Cannot find
module 'react/jsx-dev-runtime'`. This is the same root cause `PinnedOpenCodeServerFixture` anchors
around via `OpenCodeServerOptions.WorkingDirectory` for the test suite (Task 2); the sandbox demo
reaches the identical fix by folding `--cwd` into the command tokens themselves.

## Pointing the live suite at another build of the server

`PinnedOpenCodeServerFixture` normally starts the pinned submodule source under bun.
`OPENCODE_SDK_TESTS_SERVER_COMMAND` replaces that command and changes nothing else — the same
isolated XDG roots, the same repository-owned RPC plugin seeded into the server's configuration,
the same launcher-owned readiness and teardown, the same retained logs — so the whole suite can be
run against a different build of the same server:

```powershell
$env:OPENCODE_SDK_TESTS_SERVER_COMMAND = "opencode|serve"
dotnet test tests/OpenCode.Sdk.Tests --configuration Release --no-build --framework net10.0
```

The value is `|`-separated for the same reason `OPENCODE_SANDBOX_SERVER_COMMAND` is: a path with
spaces survives as one token. The first token is resolved by the SDK's own launcher from `PATH`
(`PATHEXT` included, so an npm `.cmd` shim starts), so a bare `opencode` is the whole value it
needs, and the fixture prints which command it started. The distributed-build consumer leg
(`.github/workflows/consumer-leg.yml`) is the standing user: that is how the published
`@opencode/cli` build gets this suite run against it.

Plugin check/update tests use `OwnedPinnedOpenCodeServerFixture`, which owns its server even when an
external endpoint is configured, so their configuration is package-free by construction. It still
honors `OPENCODE_SDK_TESTS_SERVER_COMMAND`, so the distributed-build lane exercises these
operations. `docs/engineering/testing-style.md` owns what every owned server is isolated from.
Plugin listing asserts on both branches: an owned server carries its builtins plus the seeded RPC
plugin, and an external endpoint cannot carry that plugin. RPC tests prove `rpc.unavailable`
through a real call, whose upstream handler awaits activation, before checking absence from
inventory. Owned event and inventory tests wait for the exact seeded RPC plugin and assert its
local path and active state.

## Live legs against a server on another host

The same external-endpoint mode runs the suite against a server hosted elsewhere. The standing
case is a Windows workstation driving the persistent PTY legs against a WSL2-hosted server, because
the `opencode-pty` daemon ships no win32 package; that recipe and its gotchas live in
`docs/engineering/developing-on-windows.md`. The sandbox can be pointed at such an endpoint with
`--no-launch-profile`, but it is not the proof: the walkthrough's earlier session legs answer 500 on
a provider-less isolated server, so it never reaches the persistent PTY leg there.

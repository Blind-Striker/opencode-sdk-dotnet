# OpenCode.Sdk.Sandbox

A committed local playground for driving the SDK against a real `opencode2 serve` under a
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
same `OPENCODE_SERVER_PASSWORD` variable; without it the server generates and prints a
random one):

```sh
OPENCODE_SERVER_PASSWORD=123456 opencode2 serve --hostname 127.0.0.1 --port 4096
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
is what proves the persistent PTY round trip; see the WSL2 recipe below.

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
standing breadth walkthrough: health, session
create/list/get, message list, export with its sanitize query, the permission
create/get/reply round trip, the NoThrow spine over compact and a deliberately bad fork
boundary, the mechanism leg — the bodyless POSTs (interrupt, revert clear), the PUT
family (mcp add, pty update, the instructions entry), and a typed `FormNotFoundError` over
NoThrow — the envelope-completion leg (vcs branches' ref-to-array shape, the location sibling,
the session-active dictionary, the server response's flattened single-key `Urls` list, and a
session's context read), the PTY leg, and the persistent PTY leg, all through the same Extensions
registration.

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
client-runtime.md` §Connection modes), then calls `CreateClient()` and `GetHealthAsync` under a
5-second-bounded probe, the same recipe door 2 (explicit endpoint) would run against a
caller-supplied endpoint. It is checked before the `OPENCODE_SANDBOX_ENDPOINT` gate, so it is the
only mode reachable without a running server.

`OPENCODE_SANDBOX_SERVER_COMMAND` overrides the launched command (`|`-separated, to survive paths
with spaces); unset uses the product default (`opencode serve` from `PATH`). Run from the
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

## Live legs against a WSL2 server (Windows workstations)

The `opencode-pty` daemon (`@opencode-ai/pty`) ships darwin/linux platform packages only — no
win32 package exists at the pinned upstream commit — so a persistentPty live test run directly on
Windows can only exercise the daemon-absent arms. `PinnedOpenCodeServerFixture`'s
external-endpoint mode plus `PersistentPtyDaemonGate`'s override (Task 6) let a Windows
workstation instead run the live leg against a server hosted in WSL2, whose linux package does
carry the daemon binary.

Clone into the WSL filesystem instead of reusing the Windows checkout over `/mnt/<drive>`: one
shared `external/opencode/node_modules` holds the platform package of whichever OS installed last,
so the two installs clobber each other's daemon binary.

1. In WSL2, clone the repository and initialize the submodule:
   ```sh
   git clone https://github.com/Blind-Striker/opencode-sdk-dotnet.git ~/repos/opencode-sdk-dotnet
   cd ~/repos/opencode-sdk-dotnet
   git submodule update --init --depth 1 external/opencode
   ```
2. Install the bun the pin names in its `packageManager` field, not the workstation's own:
   ```sh
   curl -fsSL https://bun.sh/install | bash -s "bun-v$(grep -o '"packageManager": *"bun@[0-9.]*"' \
     external/opencode/package.json | grep -o '[0-9][0-9.]*')"
   ```
3. Install the dependencies — this is what places `@opencode-ai/pty-<platform>/bin/opencode-pty`,
   which `packages/core` resolves as its `binaryPath`:
   ```sh
   cd ~/repos/opencode-sdk-dotnet/external/opencode
   bun install --frozen-lockfile --ignore-scripts
   ```
4. Serve from `packages/cli` under isolated XDG roots and a fixed password, so the run touches no
   real profile and the Windows side can authenticate:
   ```sh
   cd ~/repos/opencode-sdk-dotnet/external/opencode/packages/cli
   mkdir -p /tmp/ocsdk/data /tmp/ocsdk/cache /tmp/ocsdk/config /tmp/ocsdk/state
   XDG_DATA_HOME=/tmp/ocsdk/data XDG_CACHE_HOME=/tmp/ocsdk/cache \
   XDG_CONFIG_HOME=/tmp/ocsdk/config XDG_STATE_HOME=/tmp/ocsdk/state \
   OPENCODE_CONFIG_CONTENT='{}' OPENCODE_DISABLE_MODELS_FETCH=1 OPENCODE_PASSWORD=<pw> \
     bun src/index.ts serve --port 4097
   ```
5. On Windows, point the fixture at it and run the persistentPty legs:
   ```powershell
   $env:OPENCODE_SDK_TESTS_ENDPOINT = "http://localhost:4097"
   $env:OPENCODE_SDK_TESTS_PASSWORD = "<pw>"
   $env:OPENCODE_SDK_TESTS_PTY_DAEMON = "1"
   dotnet test tests/OpenCode.Sdk.Tests --configuration Release --no-build `
     -- --treenode-filter "/*/*/PersistentPtyLiveTests/*" --report-trx
   ```

The filter keeps the run to the live legs, which is what the recipe was proven with; the whole suite
can also run against the same endpoint, because the fixture-driven live legs create their sessions
without a location, so no Windows path from the workstation reaches the WSL2 server. The
blank-password guard is real: an unset or whitespace `OPENCODE_SDK_TESTS_PASSWORD` fails
initialization by name rather than reaching the server.

The exact-pin discipline is the operator's: the WSL2 server must be built from the same submodule
commit; the fixture prints both so a mismatch is visible, it cannot verify a source run's version.

Two gotchas when driving WSL from a Windows shell. Put the WSL side in a script file and run it as
`MSYS_NO_PATHCONV=1 wsl.exe -- bash /mnt/c/<path>.sh` — Git Bash otherwise mangles the quoting and
rewrites `/tmp` paths into Windows ones. Stop the server by pattern
(`pkill -f "index.ts serve --port 4097"`) rather than by a pid file, because a backgrounded
`setsid nohup … &` leaves the file empty; the `opencode-pty` daemon exits with the server.

The sandbox can be pointed at the same endpoint, but it is not the proof and was not exercised this
way: `dotnet run --project tests/OpenCode.Sdk.Sandbox` needs `--no-launch-profile` (the checked-in
`launchSettings.json` prefills `OPENCODE_SANDBOX_ENDPOINT` at port 4096), and the walkthrough's
earlier session legs answer 500 on a provider-less isolated server, so it never reaches the
persistent PTY leg there. `PersistentPtyLiveTests` is what proves the round trip.

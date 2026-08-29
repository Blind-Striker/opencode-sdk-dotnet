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
the session-active dictionary, the server response's promoted-inline `ServerData`, and a
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
carry the daemon binary:

1. In WSL2, at the same checkout (`/mnt/<drive>/…/external/opencode`):
   ```sh
   bun install --frozen-lockfile --ignore-scripts        # places the linux opencode-pty package
   OPENCODE_PASSWORD=<pw> bun packages/cli/src/index.ts serve --port 4097
   ```
2. On Windows:
   ```powershell
   $env:OPENCODE_SDK_TESTS_ENDPOINT = "http://localhost:4097"
   $env:OPENCODE_SDK_TESTS_PASSWORD = "<pw>"
   $env:OPENCODE_SDK_TESTS_PTY_DAEMON = "1"
   dotnet test --configuration Release
   ```

That command runs the whole suite, not just the persistentPty legs: the fixture-driven live legs
create their sessions without a location, so no Windows path from the workstation reaches the WSL2
server.

The exact-pin discipline is the operator's: the WSL2 server must be built from the same submodule
commit; the fixture prints both so a mismatch is visible, it cannot verify a source run's version.

The sandbox reaches the same server without any of that wiring — point `OPENCODE_SANDBOX_ENDPOINT`
at it and the persistent PTY leg takes its round-trip arm, because that leg branches on the
server's own `create` answer rather than on the platform.

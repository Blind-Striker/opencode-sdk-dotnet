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
NoThrow — and the PTY leg, all through the same Extensions registration.

The PTY leg (`PtySessionWalkthrough`) is the hand-written family's live proof (ADR-0021). It
creates a PTY, lists the family, mints a connect ticket through the token door — whose
`x-opencode-ticket` header the SDK applies internally — and then opens the WebSocket session
**ticket-less**, carrying the client's Basic credential on the upgrade request, which is the
designed non-browser path. It records the replay frames and the single cursor frame that ends
the replay, writes `echo hello`, reads until the terminal echoes it, reconnects at the observed
cursor to show that a resume replays only what came after it, and finally removes the PTY while a
read is in flight so the normal close ends the enumeration rather than faulting it.

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

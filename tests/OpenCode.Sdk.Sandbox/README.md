# OpenCode.Sdk.Sandbox

A committed local playground for driving the SDK against a real `opencode2 serve` under a
debugger. It rides the repository's full convention set (analyzers, `.editorconfig`, format
gate) — unlike `.scratchpad/`, which remains the home for throwaway prototypes that answer a
question and disappear.

Configuration comes from environment variables, prefilled for the IDE by
`Properties/launchSettings.json` (profile `sandbox`):

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

Then F5 with the `sandbox` profile. The program composes through the Extensions package —
a Generic Host whose `AddOpenCode` registers one singleton client owning its transport,
with `SessionsClient` resolved straight from the container — then drives the breadth
surface end to end: health, `CreateSessionAsync`, `ListSessionsAsync` (typed page + wire
cursor), `GetSessionAsync` on the created handle, and `ListMessagesAsync`.

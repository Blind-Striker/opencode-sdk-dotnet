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
| `OPENCODE_SERVER_PASSWORD` | Consumed by the SDK's own auth fallback, not by sandbox code |

## Running

Start a server with a fixed password so the checked-in profile matches (`serve` reads the
same `OPENCODE_SERVER_PASSWORD` variable the SDK falls back to; without it the server
generates and prints a random one):

```sh
OPENCODE_SERVER_PASSWORD=123456 opencode2 serve --hostname 127.0.0.1 --port 4096
```

Then F5 with the `sandbox` profile. The program drives the breadth surface end to end:
health, `CreateSessionAsync`, `ListSessionsAsync` (typed page + wire cursor),
`GetSessionAsync` on the created handle, and `ListMessagesAsync`.

The project deliberately stays a flat console for now; it grows a Generic Host composition
root when the Extensions package (DI) lands.

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
| `OPENCODE_SANDBOX_SESSION_ID` | Optional; with a message id, fetches one message |
| `OPENCODE_SANDBOX_MESSAGE_ID` | Optional; see above |

## Running

Start a server with a fixed password so the checked-in profile matches (`serve` reads the
same `OPENCODE_SERVER_PASSWORD` variable the SDK falls back to; without it the server
generates and prints a random one):

```sh
OPENCODE_SERVER_PASSWORD=123456 opencode2 serve --hostname 127.0.0.1 --port 4096
```

Then F5 with the `sandbox` profile. The prefilled session/message ids belong to this
machine's local opencode history — replace them when they age out.

## Finding ids with curl

Session and message ids come from the pinned surface's list operations
(`v2.session.list`, `v2.message.list` — both still pending SDK breadth):

```sh
# newest session id
curl -su opencode:123456 "http://127.0.0.1:4096/api/session?limit=1" | jq -r '.data[0].id'

# first message id of that session
curl -su opencode:123456 "http://127.0.0.1:4096/api/session/<sessionID>/message" | jq -r '.data[0].id'
```

The project deliberately stays a flat console for now; it grows a Generic Host composition
root when the Extensions package (DI) lands.

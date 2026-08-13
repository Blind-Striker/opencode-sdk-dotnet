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

Start a server (`opencode2 serve --hostname 127.0.0.1 --port 4096` prints its generated
password), fill the profile, and F5. The project deliberately stays a flat console for now;
it grows a Generic Host composition root when the Extensions package (DI) lands.

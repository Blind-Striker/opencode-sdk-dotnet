# 📚 The opencode SDK guide

Six pages for people **calling** the SDK. Read them in order the first time: install the package
and make a call, decide how you reach a server, then the three subsystems that behave differently
from ordinary request/response calls — event streams, terminals, and cursor lists — plus the error
model that runs underneath all of them.

Every C# snippet on these pages is compiled against the shipped public surface before it is written
down, and every type is spelled exactly as the package spells it. If a page shows a member, that
member exists.

| Page | What it covers |
|---|---|
| [Getting started](getting-started.md) | Installing the package, constructing a client, your first health check, session, and prompt |
| [Connection modes](connection-modes.md) | The standalone launcher, a server you already run, and registering the client with dependency injection |
| [Streaming](streaming.md) | The global event bus, per-session log streams, cancellation, and what a stream does when it fails |
| [Terminals](terminals.md) | PTY and persistent-PTY sessions over the WebSocket doors, frames, input, resize, and the Windows platform note |
| [Errors and responses](errors-and-responses.md) | The response spine, throwing versus `NoThrow`, the typed error family, and transport failures |
| [Pagination](pagination.md) | The two cursor-carrying list envelopes, manual paging, and the `EnumerateMessagesAsync` companion |

New here? [Getting started](getting-started.md) is the shortest path to a working call, and the
root [README](../../README.md) carries the badges, the compatibility matrix, the API-coverage
numbers, and the known issues.

## 🔧 Looking for the internals?

This guide describes the API you call. Architecture, decision records, the generator, and the
engineering policy that produced them are contributor material — start at
[`AGENTS.md`](../../AGENTS.md).

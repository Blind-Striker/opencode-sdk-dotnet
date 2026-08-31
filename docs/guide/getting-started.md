# 🚀 Getting started

Install the package, point a client at a server, and make three calls. Ten minutes, and the last
one talks to a model.

- [📦 Install](#-install)
- [🔌 Construct a client](#-construct-a-client)
- [▶️ Your first calls](#️-your-first-calls)
- [🧭 How the client is organised](#-how-the-client-is-organised)
- [➡️ Where to go next](#️-where-to-go-next)

## 📦 Install

**Nothing is on NuGet.org yet** — `0.1.0` is prepared but unpublished. Where the packages can be
had in the meantime, the exact `dotnet nuget add source` command, the two package names, and the
`read:packages` token requirement all live in one place so they never drift:
[**Installation** in the root README](../../README.md#-installation).

You also need an `opencode` server. Either install the CLI and run one yourself, or let the SDK
start a private one for you — [connection modes](connection-modes.md) covers both, and the
[prerequisites](../../README.md#prerequisites) section has the CLI install line.

## 🔌 Construct a client

`OpenCodeClient` is the root. It owns its transport, so construct it once and keep it — a singleton
per server is the intended shape, not a per-call object.

```csharp
using OpenCode.Sdk;
using OpenCode.Sdk.Models;
```

```csharp
using var client = new OpenCodeClient(new OpenCodeClientOptions
{
    Endpoint = new Uri("http://127.0.0.1:4096"),
    Password = Environment.GetEnvironmentVariable("OPENCODE_SERVER_PASSWORD"),
});
```

`OpenCodeClientOptions` has four members, and only the first is normally yours to think about:

| Member | Type | Meaning |
|---|---|---|
| `Endpoint` | `Uri?` | The server's base address. Required. |
| `Password` | `string?` | The HTTP Basic password. `null` sends anonymous requests, which is what a server started without authentication expects; an empty or whitespace value is refused at construction. |
| `Username` | `string` | The Basic username. Defaults to `opencode` — the only username the pinned server accepts — so leave it alone unless upstream changes. |
| `Location` | `LocationSelector?` | The ambient project directory/workspace every call resolves against, overridable per call. |

> **🔑 The SDK reads no environment variables of its own.** `OPENCODE_SERVER_PASSWORD` above is
> *your* code reading *your* environment — exactly how opencode's own CLI layers it. Options are
> snapshotted at construction, so changing the environment later never reaches a live client.

Options are validated when the client is built: a missing endpoint or a blank password throws
straight away rather than on the first call.

## ▶️ Your first calls

### Is the server alive?

```csharp
var health = await client.GetHealthAsync();

Console.WriteLine($"opencode {health.Health.Version} (pid {health.Health.Pid}) healthy: {health.Health.Healthy}");
```

`GetHealthAsync` and `GetLocationAsync` are the only two operations that hang off the root client
directly. Everything else lives on a family.

### Create a session and send a prompt

A **session** is a durable conversation. Create one, take a handle bound to its id, and post to it:

```csharp
var created = await client.Sessions.CreateSessionAsync(new SessionCreateRequest { Title = "hello from .NET" });
var session = client.Sessions.GetSessionClient(created.Session.Id);

var prompt = await session.PostPromptAsync(new SessionPromptPostRequest { Text = "Summarize this repository." });

Console.WriteLine($"queued {prompt.Prompt.Id} in session {created.Session.Id}");
```

`PostPromptAsync` **queues** the turn and returns the inbox entry it created — the assistant's
answer arrives asynchronously, which is what [streaming](streaming.md) is for. If you just want
text back from a model with no conversation state, use generate instead:

```csharp
var generated = await session.PostGenerateAsync(new SessionGeneratePostRequest { Prompt = "Name three C# testing libraries." });

Console.WriteLine(generated.Generate.Text);
```

## 🧭 How the client is organised

The root client exposes **27 families** as properties — `Sessions`, `Events`, `Ptys`,
`PersistentPtys`, `Shells`, `Providers`, `LanguageModels`, `Agents`, `Skills`, `Commands`,
`Permissions`, `Credentials`, `Projects`, `Workspaces`, `Worktrees`, `Vcs`, `FileSystem`, `Forms`,
`Generation`, `Integrations`, `McpServers`, `Plugins`, `References`, `Server`, `Websearch`,
`Debug`, and `Experimental`:

```csharp
var providers = await client.Providers.ListProvidersAsync();
var agents = await client.Agents.ListAgentsAsync();

Console.WriteLine($"{providers.Providers.Count} providers, {agents.Agents.Count} agents");
```

Two shapes repeat everywhere, and once you have seen them the rest of the surface reads itself:

- **Collection client → bound handle.** Where the API keys operations by an id, the collection
  client has a `Get*Client(id)` factory and the handle carries the id-keyed operations:
  `client.Sessions.GetSessionClient(id)`, `client.Ptys.GetPtyClient(id)`,
  `client.PersistentPtys.GetPersistentPtyClient(id)`. The handle is a cheap value — take one per
  id, keep it as long as you like.
- **Three optional tails.** Almost every operation is
  `…Async(request, requestOptions, cancellationToken)`, where `request` carries the body and query
  and is optional when every member is, `requestOptions` selects per-call behaviour such as
  [`NoThrow`](errors-and-responses.md#-ask-for-the-failure-as-data-instead), and the token is the
  usual one. Streaming operations are the exception — they take no `requestOptions`.

## ➡️ Where to go next

| If you want to… | Read |
|---|---|
| Let the SDK start its own server, or wire the client into a `Host` | [Connection modes](connection-modes.md) |
| React to what the server is doing, live | [Streaming](streaming.md) |
| Drive a real terminal | [Terminals](terminals.md) |
| Branch on a failure instead of catching it | [Errors and responses](errors-and-responses.md) |
| Walk a long message or session history | [Pagination](pagination.md) |

For a full runnable program that exercises most of the surface against a live server, the
in-repo sandbox is committed and documented:
[`tests/OpenCode.Sdk.Sandbox`](../../tests/OpenCode.Sdk.Sandbox/README.md).

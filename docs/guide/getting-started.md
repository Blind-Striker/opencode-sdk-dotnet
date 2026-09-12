# 🚀 Getting started

Date: 2026-09-08

Install the package, point a client at a server, and make three calls. Ten minutes, and the last
one talks to a model.

- [📦 Install](#-install)
- [🔌 Construct a client](#-construct-a-client)
- [▶️ Your first calls](#️-your-first-calls)
- [🧭 How the client is organised](#-how-the-client-is-organised)
- [➡️ Where to go next](#️-where-to-go-next)

## 📦 Install

```bash
dotnet add package OpenCodeAI.Sdk --prerelease
dotnet add package OpenCodeAI.Sdk.Extensions --prerelease   # dependency injection, optional
```

> **The package id is not the namespace.** You install `OpenCodeAI.Sdk` and you write
> `using OpenCode.Sdk;` — nuget.org reserves the `OpenCode.` id prefix for an unrelated owner, so
> the artifact carries a different name than the code inside it.

The packages target `netstandard2.0`, `net472`, `net8.0`, `net9.0`, and `net10.0`, so any project
on one of those works. Nightly builds of `master` live on a GitHub Packages feed; the source
command and its `read:packages` token requirement live in one place so they never drift:
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
    Password = Environment.GetEnvironmentVariable("OPENCODE_PASSWORD"),
});
```

`OpenCodeClientOptions` has four members, and only the first is normally yours to think about:

| Member | Type | Meaning |
|---|---|---|
| `Endpoint` | `Uri?` | The server's base address. Required. |
| `Password` | `string?` | The HTTP Basic password. Required for any server the `opencode` CLI started — it always runs with one, generated and printed as `server password <pw>` when you set none. `null` sends no credential at all, which only a server embedded without authentication accepts; an empty or whitespace value is refused at construction. |
| `Username` | `string` | The Basic username. Defaults to `opencode` — the only username the pinned server accepts — so leave it alone unless upstream changes. |
| `Location` | `LocationSelector?` | The ambient directory/workspace header values, overridable per call; only operations that resolve location from those headers use them. |

Session creation selects its location from `SessionCreateRequest.Location`; leaving it unset uses
the server's working directory. Session listing filters with `SessionListRequest.Directory` or
`Project`, independently of these headers. See the [location contract](../architecture/client-runtime.md#location)
for ambient and per-call header behavior.

> **🔑 The SDK reads no environment variables of its own.** `OPENCODE_PASSWORD` above is
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

### Choosing a model

The calls above let the server pick a model. To pick one yourself, the *order* is the part worth
knowing, because a server that has just answered a health check does not necessarily have a catalog
yet:

```csharp
// 1. Health is process liveness. It says nothing at all about the model catalog.
var health = await client.GetHealthAsync();

// 2. Providers register while the location's plugins activate, and activation is asynchronous.
//    This is the settle signal; a health probe is not.
await client.Plugins.AwaitPluginActivationAsync();

// 3. Now the catalog is worth reading.
var providers = await client.Providers.ListProvidersAsync();
var models = await client.LanguageModels.ListModelsAsync();

Console.WriteLine($"{providers.Providers.Count} providers, {models.Models.Count} models");

// 4. Listing already filters to enabled models of available providers, so this guards an empty
//    catalog rather than filtering one.
var model = models.Models.FirstOrDefault(candidate => candidate.Enabled);

if (model is not null)
{
    // 5. A session reference carries the catalog id.
    var created = await client.Sessions.CreateSessionAsync(new SessionCreateRequest
    {
        Title = "picked a model",
        Model = new ModelRef { ProviderId = model.ProviderId, Id = model.Id },
    });

    Console.WriteLine($"session {created.Session.Id} on {model.ProviderId}/{model.Id}");
}
```

Three things the types do not tell you:

- **`ModelRef.Id` takes `ModelInfo.Id`, never `ModelInfo.ModelId`.** `Id` is the catalog identity;
  `ModelId` is the id the provider's own API uses. They hold the same string for most models — which
  is exactly why the mistake survives a first test and then fails on an aliased one. Upstream's own
  TUI matches on `ProviderId` plus `Id`.
- **An empty list right after health is ordinary, not a failure.** Plugins activate asynchronously
  and providers register as they go, so a list issued too early observes a partial or empty
  registry. `AwaitPluginActivationAsync` is the wait for it, and it is cheap to call again.
- **Neither create nor prompt validates the ref.** A wrong `ProviderId`/`Id` pair is accepted by
  both and surfaces later as a failed turn — there is no typed model error to catch at the call.

Every call above resolves its location the same way the rest of the client does: from the client's
ambient `Location`, or the server's own working directory when you set none. Each request type
carries its own `Location` if you want to read one location's catalog from a client pointed at
another.

And **provider inventory is the machine's ambient opencode configuration**, not something the SDK
or the launcher supplies. A host with no provider credentials configured lists nothing, and
`OpenCodeServer.StartAsync` does not change that — it starts a fresh process, not a fresh machine.
If you have no preference at all, `client.LanguageModels.GetDefaultAsync()` answers with the
location's default model, whose `Default` payload is legitimately `null` on such a host.

### Export a session transcript

`GetExportAsync` hands back the session plus every settled message in one `SessionTransferData`,
and `PostImportAsync` takes that same shape back at a location (answering 409 for an id that
already exists):

```csharp
var export = await session.GetExportAsync(new SessionExportRequest { Sanitize = QueryBoolean.True });

Console.WriteLine($"{export.Export.Messages.Count} messages from {export.Export.Info.Id}");
```

> **✂️ `Sanitize` redacts by design.** With `Sanitize = QueryBoolean.True` the server rewrites
> message text to placeholders before it answers — `[redacted:text:<messageId>]` for user text,
> `[redacted:synthetic:<messageId>]` for synthetic text, `[redacted:session-title:<sessionId>]` for
> the title, and the same treatment for the directory. Ids, types, order, and count all survive
> untouched. So a sanitized export is for sharing the *shape* of a conversation, never for
> comparing transcripts: compare ids, not text. Leave `Sanitize` unset or `False` when you want the
> conversation itself. Verified on `@opencode/cli@2.0.2`.

## 🧭 How the client is organised

The root client exposes **29 families** as properties — `Sessions`, `Events`, `Ptys`,
`PersistentPtys`, `Shells`, `Providers`, `LanguageModels`, `Agents`, `Skills`, `Commands`,
`Permissions`, `Credentials`, `Config`, `Projects`, `Workspaces`, `Worktrees`, `Vcs`, `FileSystem`,
`Forms`, `Generation`, `Integrations`, `McpServers`, `Plugins`, `References`, `Rpc`, `Server`,
`Websearch`, `Debug`, and `Experimental`:

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

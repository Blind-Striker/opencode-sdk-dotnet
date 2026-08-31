# 🔌 Connection modes

There are two ways to get a client bound to a running opencode server: **let the SDK start one**,
or **point it at one you already have**. Dependency injection is not a third way in — it is how you
register either of them with a container.

- [🚀 The SDK starts the server](#-the-sdk-starts-the-server)
- [🔗 A server you already run](#-a-server-you-already-run)
- [🧩 Registering with dependency injection](#-registering-with-dependency-injection)
- [🔜 Attaching to a background service](#-attaching-to-a-background-service)

## 🚀 The SDK starts the server

`OpenCodeServer.StartAsync()` launches a private `opencode serve` child, waits for it to report
readiness, mints its credential, and hands you an owner object. No ambient process, no endpoint to
configure, no port to pick.

```csharp
await using var server = await OpenCodeServer.StartAsync();
using var client = server.CreateClient();

Console.WriteLine($"started {server.Endpoint} (pid {server.ProcessId})");

var health = await client.GetHealthAsync();

Console.WriteLine($"healthy: {health.Health.Healthy}");
```

The signature is
`OpenCodeServer.StartAsync(OpenCodeServerOptions? options = null, CancellationToken cancellationToken = default)`.

**Every start is a fresh private server on port zero.** It never discovers, attaches to, or shuts
down a server somebody else is running, so coexisting with your own dev server is safe by
construction. The returned `OpenCodeServer` is the only owner of that child, and it tells you what
it started:

| Member | Meaning |
|---|---|
| `Endpoint` | The `http://127.0.0.1:{port}` address the child actually bound |
| `Password` / `Username` | The generated lease credential this server accepts |
| `ProcessId` | The child's PID |

### Shaping the launch

```csharp
await using var server = await OpenCodeServer.StartAsync(new OpenCodeServerOptions
{
    Command = ["opencode2", "serve"],
    WorkingDirectory = "/srv/my-project",
    Environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["OPENCODE_LOG_LEVEL"] = "debug" },
    ReadinessTimeout = TimeSpan.FromSeconds(90),
    GracefulShutdownTimeout = TimeSpan.FromSeconds(5),
});
```

| Option | Default | What it does |
|---|---|---|
| `Command` | `["opencode", "serve"]` | The executable plus its leading arguments. The launcher appends `--stdio --port 0` itself. |
| `WorkingDirectory` | `null` | The child's working directory; `null` inherits yours. |
| `Environment` | `null` | Extra environment entries for the child. |
| `ReadinessTimeout` | 60 s | How long to wait for the readiness line before failing and ending the child. |
| `GracefulShutdownTimeout` | 3 s | The grace between releasing the ownership lease and the forced kill. |

> **🔒 Your `Environment` entries can never shadow the credential.** The launcher writes its own
> generated `OPENCODE_PASSWORD` entry *after* yours, so a stray value in your dictionary cannot
> take over the child's authentication.

### Clients from a started server

`CreateClient(Action<OpenCodeClientOptions>? configure = null)` builds a client already pinned to
that server's endpoint and lease credential. The delegate is for **behaviour only** — setting
`Endpoint`, `Username`, or `Password` inside it is refused with `InvalidOperationException`,
because a started server's identity is not yours to reassign:

```csharp
using var client = server.CreateClient(options => options.Location = new LocationSelector
{
    Directory = "/srv/my-project",
});
```

Each call builds a new client over its own transport, so dispose each one. Disposing the *server*
stops the child: it releases the ownership lease, waits out `GracefulShutdownTimeout`, then kills
the whole process tree — every step bounded, so disposal never hangs your shutdown. If your process
dies before disposal runs, the operating system closes the lease and the child exits anyway.

Startup failures throw `OpenCodeServerException`, carrying a bounded tail of the child's stderr
whenever a child actually ran — see
[errors and responses](errors-and-responses.md#-when-the-launcher-fails).

## 🔗 A server you already run

If you already know an endpoint, construct the client directly. There is no separate verb for this
door: the endpoint and the credential *are* the connection.

```csharp
var endpoint = new Uri("http://127.0.0.1:4096");

using var client = new OpenCodeClient(new OpenCodeClientOptions
{
    Endpoint = endpoint,
    Password = Environment.GetEnvironmentVariable("OPENCODE_SERVER_PASSWORD"),
});

using var probe = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var health = await client.GetHealthAsync(cancellationToken: probe.Token);

if (!health.Health.Healthy)
{
    throw new InvalidOperationException($"opencode at {endpoint} answered unhealthy");
}
```

That bounded health probe is the whole validation recipe, and it is deliberately yours to write:
the SDK carries no version comparand of its own and no network-timeout knob yet, so a
`CancellationTokenSource` is the honest timeout and your own expectation is the honest version
check.

**About `OPENCODE_SERVER_PASSWORD`**: that is the variable *the opencode CLI* reads when you start
a server with authentication —

```sh
OPENCODE_SERVER_PASSWORD=your-password opencode2 serve --hostname 127.0.0.1 --port 4096
```

— and the client must present the same value as its Basic password. The SDK never reads it, or any
other environment variable, for you. Reading it in the snippet above is your application's choice;
a configuration section or a secret store works exactly as well. A server started **without** a
password expects anonymous requests, so leave `Password` as `null` for one.

## 🧩 Registering with dependency injection

`OpenCode.Sdk.Extensions` adds `AddOpenCode` to `IServiceCollection`. What lands in the container is
deliberately small: **one `OpenCodeClient` singleton** holding the transport open for the
container's lifetime, and **each of the 27 families registered as its own singleton resolved from
that one client**. A service therefore asks for the family it actually uses — `EventsClient`,
`PtysClient`, `WorktreesClient` — and all of them share a single pipeline and a single disposal at
shutdown.

There are two overloads, and the difference between them matters more than it looks:

| Overload | Options come from | Trimming / native AOT |
|---|---|---|
| `AddOpenCode(Action<OpenCodeClientOptions>)` | a delegate you write | ✅ safe — no reflection |
| `AddOpenCode(IConfiguration)` | a bound configuration section | ⚠️ annotated, see below |

Both go through the standard options pattern, so `IOptions<OpenCodeClientOptions>`, options
validation, and anything else layered on options behaves exactly as it does for any other library.

Binding from configuration keeps the endpoint out of your code entirely:

```csharp
var builder = Host.CreateApplicationBuilder(args);

// Binds Endpoint, Password, Username, and Location from the "OpenCode" section.
builder.Services.AddOpenCode(builder.Configuration.GetSection("OpenCode"));
builder.Services.AddHostedService<EventLogger>();

await builder.Build().RunAsync();
```

```json
{
  "OpenCode": {
    "Endpoint": "http://127.0.0.1:4096"
  }
}
```

Leave `Password` out of that file. Configuration is layered, so user secrets in development and an
environment variable or a secret store in production bind onto the same section without the
credential ever reaching source control.

Then inject whichever family the service needs — here the event bus, straight into a hosted
service:

```csharp
internal sealed class EventLogger(EventsClient events, ILogger<EventLogger> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var @event in events.SubscribeAsync(stoppingToken))
        {
            logger.LogInformation("opencode event {EventType}", @event.Type);
        }
    }
}
```

> **⚡ Trimming and native AOT**: `AddOpenCode(IConfiguration)` carries `[RequiresDynamicCode]`
> and `[RequiresUnreferencedCode]`, because configuration binding reflects over the options type —
> so a trimmed or AOT publish reports **IL3050** and **IL2026** at that call. Nothing is wrong with
> your code; the annotation is doing its job. Switch to the configure-action overload there, which
> needs no reflection at all. Both packages declare `IsAotCompatible` on `net10.0`.

The configure-action overload, with a `SessionsClient` worker doing a paged read, is the worked
example in the root README's [dependency-injection quickstart](../../README.md#dependency-injection)
— worth reading side by side with the binding above.

Nothing about DI changes which door you came in through: an `AddOpenCode` registration is the
explicit-endpoint door, and a launcher-started server joins a container by registering
`CreateClient()`'s result yourself.

## 🔜 Attaching to a background service

opencode has a third connection mode of its own — discovering a registered background daemon
through its registration file (`Service.discover` / `ensure` / `stop`). **The SDK has no parity for
it yet.** You can point a client at an endpoint you already know, or start a private server; what
you cannot do today is find a daemon somebody else started. That is a queued follow-up, tracked in
the root README's [known issues](../../README.md#known-issues).

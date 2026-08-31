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

`OpenCode.Sdk.Extensions` adds `AddOpenCode` to `IServiceCollection`. It registers **one singleton
`OpenCodeClient`** that owns its transport for the container's lifetime, plus all 27 sub-clients
resolved from that same instance — so you can inject `SessionsClient`, `EventsClient`, or
`PtysClient` directly instead of reaching through the root every time.

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOpenCode(options =>
{
    options.Endpoint = new Uri("http://127.0.0.1:4096");
    options.Password = Environment.GetEnvironmentVariable("OPENCODE_SERVER_PASSWORD");
});

builder.Services.AddHostedService<SessionWorker>();

await builder.Build().RunAsync();
```

```csharp
internal sealed class SessionWorker(SessionsClient sessions) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var page = await sessions.ListSessionsAsync(
            new SessionListRequest { Limit = "10", Order = ListOrder.Descending },
            cancellationToken: stoppingToken);

        Console.WriteLine($"{page.Sessions.Count} sessions, next cursor {page.Cursor.Next ?? "<none>"}");
    }
}
```

The registration goes through the standard options pattern, so `IOptions<OpenCodeClientOptions>`
and everything built on it works as usual, and the container disposes the one client at shutdown.

### Binding from configuration

The second overload binds the same options from a configuration section:

```csharp
builder.Services.AddOpenCode(builder.Configuration.GetSection("OpenCode"));
```

> **⚡ Trimming and native AOT**: this overload is annotated `[RequiresDynamicCode]` and
> `[RequiresUnreferencedCode]`, because configuration binding reflects over the options type.
> Calling it from a trimmed or AOT-published app produces **IL2026** and **IL3050** warnings by
> design. Use the configure-action overload above there — it needs no reflection, and both packages
> declare `IsAotCompatible` on `net10.0`.

Nothing about DI changes which door you came in through: an `AddOpenCode` registration is an
explicit endpoint, and a launcher-started server is registered by handing `CreateClient()`'s result
to the container yourself.

## 🔜 Attaching to a background service

opencode has a third connection mode of its own — discovering a registered background daemon
through its registration file (`Service.discover` / `ensure` / `stop`). **The SDK has no parity for
it yet.** You can point a client at an endpoint you already know, or start a private server; what
you cannot do today is find a daemon somebody else started. That is a queued follow-up, tracked in
the root README's [known issues](../../README.md#known-issues).

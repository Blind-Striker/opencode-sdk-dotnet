# 🔌 Connection modes

Date: 2026-09-08

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
| `ProcessId` | The PID of the child it owns — the `cmd.exe` host when the command resolved to a Windows batch shim, see [below](#how-the-command-is-resolved) |

### Shaping the launch

```csharp
await using var server = await OpenCodeServer.StartAsync(new OpenCodeServerOptions
{
    Command = ["/opt/opencode/bin/opencode", "serve"],
    WorkingDirectory = "/srv/my-project",
    Environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["OPENCODE_LOG_LEVEL"] = "debug" },
    ReadinessTimeout = TimeSpan.FromSeconds(90),
    GracefulShutdownTimeout = TimeSpan.FromSeconds(5),
});
```

| Option | Default | What it does |
|---|---|---|
| `Command` | `["opencode", "serve"]` | The executable plus its leading arguments. The launcher resolves `Command[0]` the way a shell would — see [How the command is resolved](#how-the-command-is-resolved) — and appends `--stdio --port 0` itself. |
| `WorkingDirectory` | `null` | The child's working directory; `null` inherits yours. |
| `Environment` | `null` | Extra environment entries for the child. |
| `ReadinessTimeout` | 60 s | How long to wait for the readiness line before failing and ending the child. |
| `GracefulShutdownTimeout` | 3 s | The grace between releasing the ownership lease and the forced kill. |

> **🔒 Your `Environment` entries can never shadow the credential.** The launcher writes its own
> generated `OPENCODE_PASSWORD` entry *after* yours, so a stray value in your dictionary cannot
> take over the child's authentication.

### How the command is resolved

`Command[0]` is resolved **before** anything is spawned, following the same rules a shell does.
This is not a detail you normally think about — until you install the CLI with npm on Windows,
where npm writes shim files (`opencode`, `opencode.cmd`, `opencode.ps1`) and keeps the real
binary inside `node_modules`. There is no `opencode.exe` anywhere, and a raw `Process.Start` only
ever appends `.exe`. Resolving first is what makes the shipped default work there.

The rules, in full:

- A command containing a directory separator, or a rooted path, is used exactly as written. No
  search happens, so pointing `Command` at an absolute path always wins.
- A bare name is looked up through the `PATH` directories in order. Empty entries are skipped, and
  a relative entry is resolved against the process's current directory.
- **On Windows**, a bare name with no extension is tried with each `PATHEXT` extension, in `PATHEXT`
  order (falling back to `.COM;.EXE;.BAT;.CMD` when `PATHEXT` is unset). A name that already carries
  an extension — `opencode.cmd` — is tried exactly as written. So an npm `.cmd` shim is found and
  started.
- **On Unix**, the `PATH` directories are searched for the name itself; the operating system still
  decides at spawn time whether the file is executable.

When a bare name matches nothing, the start fails with `OpenCodeServerException` before any process
exists, naming how many directories were searched and which extensions were tried — see
[errors and responses](errors-and-responses.md#-when-the-launcher-fails).

> **⚙️ Batch shims run through `cmd.exe`.** When resolution lands on a `.cmd` or `.bat` file, the
> launcher starts the system `cmd.exe` explicitly (`/d /s /c`) with the script and every argument
> quoted, rather than relying on Windows' implicit batch handling. Two consequences are worth
> knowing:
>
> - **`ProcessId` is then the `cmd.exe` host**, not the server itself — the shim's own child. The
>   stdin ownership lease and the stdout readiness line pass straight through it, and disposal's
>   whole-tree kill covers the server underneath, so nothing else about the lifecycle changes. Ask
>   the server for its own pid (`health.Health.Pid`) if you need that one.
> - **Arguments carrying `cmd` metacharacters are refused**, not escaped. `cmd.exe` re-parses the
>   line it is handed, so any leading argument of yours containing `&`, `|`, `<`, `>`, `^`, `%`,
>   `!`, `"`, a carriage return, or a line feed fails the start with `OpenCodeServerException`
>   before anything runs. This is the same fail-closed stance Rust and Node took for BatBadBut
>   (CVE-2024-24576). If you need such an argument, point `Command` at the real executable instead
>   of the shim.

### What `StartAsync` does not isolate

A started server is a **fresh process on a fresh port** — it is not a sandbox. Unless you say
otherwise, it reads and writes the same user data, state, cache, and config roots as any other
opencode process on the machine, including the one your editor is running. Sessions, credentials,
and configuration are shared, on Windows as much as on Linux and macOS.

Redirect those roots through `Environment` when you want a private one:

```csharp
var root = Path.Combine(Path.GetTempPath(), "my-app", Guid.NewGuid().ToString("N"));

await using var server = await OpenCodeServer.StartAsync(new OpenCodeServerOptions
{
    Environment = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["XDG_DATA_HOME"] = Path.Combine(root, "data"),
        ["XDG_STATE_HOME"] = Path.Combine(root, "state"),
        ["XDG_CACHE_HOME"] = Path.Combine(root, "cache"),
        ["XDG_CONFIG_HOME"] = Path.Combine(root, "config"),
    },
});
```

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
    Password = Environment.GetEnvironmentVariable("OPENCODE_PASSWORD"),
});

using var probe = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var health = await client.GetHealthAsync(cancellationToken: probe.Token);

if (!health.Health.Healthy)
{
    throw new InvalidOperationException($"opencode at {endpoint} answered unhealthy");
}
```

That bounded health probe is the whole validation recipe for *reaching* the server, and it is
deliberately yours to write: the SDK carries no version comparand of its own and no network-timeout
knob yet, so a `CancellationTokenSource` is the honest timeout and your own expectation is the
honest version check. It is liveness only — a healthy server can still have an empty model catalog
for a second or two while its plugins activate, which
[choosing a model](getting-started.md#choosing-a-model) covers.

**About `OPENCODE_PASSWORD`**: that is the variable *the opencode CLI* reads to decide which
password its server will accept —

```sh
OPENCODE_PASSWORD=your-password opencode serve --hostname 127.0.0.1 --port 4096
```

— and the client must present the same value as its Basic password. `OPENCODE_SERVER_PASSWORD` is
the CLI's legacy name for the same value, still honored as a fallback, so an older setup keeps
working. The SDK never reads either one, or any other environment variable, for you. Reading it in
the snippet above is your application's choice; a configuration section or a secret store works
exactly as well.

> **🔑 `opencode serve` always has a password.** Setting neither variable does not start an open
> server — it makes the CLI generate one and print it as `server password <pw>` on startup, and no
> serve flag disables authentication. A client for a CLI-started server therefore always needs
> `Password`. Leaving it `null` is right only for a host that genuinely runs without one: a server
> embedded through the opencode server library, as this repository's own simulation host does for
> its tests. Point a passwordless client at `opencode serve` and every call answers **401 with an
> empty body** — the SDK says so in the exception message, see
> [a 401 with no credential](errors-and-responses.md#a-401-with-no-credential).

## 🧩 Registering with dependency injection

`OpenCode.Sdk.Extensions` adds `AddOpenCode` to `IServiceCollection`. What lands in the container is
deliberately small: **one `OpenCodeClient` singleton** holding the transport open for the
container's lifetime, and **each of the 29 families registered as its own singleton resolved from
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

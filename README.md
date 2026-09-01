# opencode SDK for .NET

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE) [![NuGet](https://img.shields.io/badge/NuGet-coming%20soon-lightgrey)](https://www.nuget.org/packages/OpenCode.Sdk)<!-- first stable: swap back to the dynamic badge-smith NuGet badge --> [![CI](https://github.com/Blind-Striker/opencode-sdk-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/Blind-Striker/opencode-sdk-dotnet/actions/workflows/ci.yml) [![Linux Tests](https://img.shields.io/endpoint?url=https%3A%2F%2Fapi.localstackfor.net%2Fbadges%2Ftests%2Flinux%2Fblind-striker%2Fopencode-sdk-dotnet%2Fmaster)](https://api.localstackfor.net/redirect/test-results/linux/blind-striker/opencode-sdk-dotnet/master)

> **🚀 Quick Start**: the first release is upcoming — [nightly builds](#-installation) are on GitHub Packages today | [Quick start](#-quick-start) | [Guide](docs/guide/getting-started.md)

> **Unofficial.** This project is not affiliated with or endorsed by the
> [opencode](https://opencode.ai) team.

A strongly typed .NET client for the [opencode](https://github.com/anomalyco/opencode) HTTP API —
the surface every opencode front-end (TUI, desktop, web UI, plugins) goes through. The callable
surface is generated from a pinned OpenAPI snapshot and rides one hand-written transport runtime,
so what you call is exactly what the server declares.

---

## 🎉 Project Status

**Pre-release, and the surface is complete.** Everything below is landed and covered; the only
thing missing is the first published package.

- ✅ **131 of 136 operations** callable, across 27 client families — sessions, PTYs, persistent
  PTYs, shells, events, MCP servers, integrations, providers, permissions, credentials, VCS,
  worktrees, websearch, and more
- ✅ **4,418 tests** green on Windows — the fullest leg, the only one that adds the `net472`
  assemblies. Linux and macOS run the same suite on `net8.0`, `net9.0`, and `net10.0`
- ✅ **Server-sent event streams**, global and per-session, over the same transport as one-shot calls
- ✅ **PTY and persistent-PTY terminal sessions** through hand-written WebSocket doors
- ✅ **A launcher** — `OpenCodeServer.StartAsync()` starts, monitors, and stops a private
  `opencode serve` child for you
- ✅ **Source-generated `System.Text.Json`** with no reflection fallback; both packages declare
  `IsAotCompatible` on `net10.0`
- 🔜 **First release (0.1.0)** — packaging is the current work; see [CHANGELOG.md](CHANGELOG.md)
- 🔜 **Background-service attachment** and an **MCP server** over this SDK — both planned, neither
  started

**Versioning**: the SDK builds against an accepted OpenAPI snapshot, never a live branch. The exact
upstream commit and the refresh procedure live in [`spec/SNAPSHOT.md`](spec/SNAPSHOT.md); today's
pin is `b1e3a7b2` on upstream's `v2` branch.

## 🚀 Platform Compatibility & Quality Status

### Supported Platforms

- [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) | [.NET 9](https://dotnet.microsoft.com/download/dotnet/9.0) | [.NET 8](https://dotnet.microsoft.com/download/dotnet/8.0)
- [.NET Standard 2.0](https://docs.microsoft.com/en-us/dotnet/standard/net-standard)
- [.NET Framework 4.7.2 and Above](https://dotnet.microsoft.com/download/dotnet-framework)

Both packages target the same set: `netstandard2.0;net472;net8.0;net9.0;net10.0`. The downlevel
targets are not a compatibility shim — the whole suite runs on `net472` on Windows,
real-process launcher acceptance included, and on `net8.0`/`net9.0`/`net10.0` on all three OSes.
`netstandard2.0` is a consumption target rather than a test target: it has no runtime to execute
on, and the `net472` leg is what exercises its compile surface.

### Build & Test Matrix

| Category | Platform/Type | Status | Description |
|----------|---------------|--------|-------------|
| **🔧 Build** | Cross-Platform | [![CI](https://github.com/Blind-Striker/opencode-sdk-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/Blind-Striker/opencode-sdk-dotnet/actions/workflows/ci.yml) | Matrix: Windows, Linux, macOS |
| **🧪 Tests** | Linux | [![Linux Tests](https://img.shields.io/endpoint?url=https%3A%2F%2Fapi.localstackfor.net%2Fbadges%2Ftests%2Flinux%2Fblind-striker%2Fopencode-sdk-dotnet%2Fmaster)](https://api.localstackfor.net/redirect/test-results/linux/blind-striker/opencode-sdk-dotnet/master) | `net8.0`, `net9.0`, `net10.0` |
| **🧪 Tests** | Windows | [![Windows Tests](https://img.shields.io/endpoint?url=https%3A%2F%2Fapi.localstackfor.net%2Fbadges%2Ftests%2Fwindows%2Fblind-striker%2Fopencode-sdk-dotnet%2Fmaster)](https://api.localstackfor.net/redirect/test-results/windows/blind-striker/opencode-sdk-dotnet/master) | `net472` plus every modern target |
| **🧪 Tests** | macOS | [![macOS Tests](https://img.shields.io/endpoint?url=https%3A%2F%2Fapi.localstackfor.net%2Fbadges%2Ftests%2Fmacos%2Fblind-striker%2Fopencode-sdk-dotnet%2Fmaster)](https://api.localstackfor.net/redirect/test-results/macos/blind-striker/opencode-sdk-dotnet/master) | `net8.0`, `net9.0`, `net10.0` |

## 📦 Package Status

| Package | NuGet.org | GitHub Packages |
|---------|-----------|-----------------|
| **OpenCode.Sdk** | [![NuGet](https://img.shields.io/endpoint?url=https%3A%2F%2Fapi.localstackfor.net%2Fbadges%2Fpackages%2Fnuget%2FOpenCode.Sdk%3Fprerelease%3Dtrue)](https://www.nuget.org/packages/OpenCode.Sdk) | [![GitHub Packages](https://img.shields.io/badge/GitHub%20Packages-nightly-blue)](https://github.com/Blind-Striker/opencode-sdk-dotnet/pkgs/nuget/OpenCode.Sdk) |
| **OpenCode.Sdk.Extensions** | [![NuGet](https://img.shields.io/endpoint?url=https%3A%2F%2Fapi.localstackfor.net%2Fbadges%2Fpackages%2Fnuget%2FOpenCode.Sdk.Extensions%3Fprerelease%3Dtrue)](https://www.nuget.org/packages/OpenCode.Sdk.Extensions) | [![GitHub Packages](https://img.shields.io/badge/GitHub%20Packages-nightly-blue)](https://github.com/Blind-Striker/opencode-sdk-dotnet/pkgs/nuget/OpenCode.Sdk.Extensions) |

## Table of Contents

1. [Supported Platforms](#supported-platforms)
2. [Why this SDK?](#-why-this-sdk)
3. [Prerequisites](#prerequisites)
4. [Installation](#-installation)
5. [Quick Start](#-quick-start)
6. [API Coverage](#-api-coverage)
7. [Documentation](#-documentation)
8. [Known Issues](#known-issues)
9. [Developing](#developing)
10. [Changelog](#changelog)
11. [License](#license)

## 💡 Why this SDK?

- **Typed all the way down.** Every operation has a generated request type, a generated response
  envelope, and typed error models — including opencode's discriminator-free unions, which the
  repository's own generator represents faithfully because off-the-shelf .NET OpenAPI generators
  did not.
- **One transport, either way in.** Point the client at a server you already run, or let
  `OpenCodeServer.StartAsync()` start a private one for you — the same pipeline owns endpoint
  authority, authentication, buffering, and failure mapping in both. (opencode's third connection
  mode, attaching to a registered background service, is not implemented yet.)
- **Errors you can branch on.** Every call throws typed exceptions by default, or returns the
  failure as data with `OpenCodeRequestOptions.NoThrow` when a 404 is a normal answer.
- **Broad .NET reach.** `netstandard2.0` and `net472` are first-class, so this works inside
  .NET Framework hosts, not just modern console apps.
- **No reflection serialization.** `System.Text.Json` source generation throughout, with no
  reflection fallback to surprise a trimmed or AOT-published app.
- **A pinned protocol, not a moving target.** Upstream's `v2` branch moves daily; this SDK builds
  against a reviewed snapshot with a receipt, so a regeneration is a reviewable diff.

## Prerequisites

You need an `opencode` server. Install the CLI
([opencode.ai](https://opencode.ai) — the v2 line ships as `@opencode-ai/cli@next` and installs the
`opencode2` command), then either run it yourself:

```sh
OPENCODE_SERVER_PASSWORD=your-password opencode2 serve --hostname 127.0.0.1 --port 4096
```

…or let the SDK start one for you — that is what `OpenCodeServer.StartAsync()` in the
[quick start](#-quick-start) does, and it needs nothing running in advance.

## 📦 Installation

### Stable (NuGet.org)

**The first release is upcoming.** Nothing is on NuGet.org yet; when `0.1.0` ships, installation
will be the usual:

```bash
dotnet add package OpenCode.Sdk
dotnet add package OpenCode.Sdk.Extensions   # dependency injection, optional
```

Until then, use the nightly feed below.

### Nightly builds (GitHub Packages)

Every code push to `master` publishes `0.1.0-nightly.{yyyyMMdd}.{shortSha}` to GitHub Packages:

```bash
# Add the GitHub Packages source (PAT: classic token with the read:packages scope)
dotnet nuget add source https://nuget.pkg.github.com/Blind-Striker/index.json \
  --name github-opencode-sdk \
  --username YOUR_GITHUB_USERNAME \
  --password YOUR_GITHUB_PAT \
  --store-password-in-clear-text

# Install the nightly packages
dotnet add package OpenCode.Sdk --prerelease --source github-opencode-sdk
dotnet add package OpenCode.Sdk.Extensions --prerelease --source github-opencode-sdk
```

Prefer keeping the token out of shell history? Commit a `nuget.config` next to your solution and
keep the credentials in environment variables:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="github-opencode-sdk" value="https://nuget.pkg.github.com/Blind-Striker/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github-opencode-sdk>
      <add key="Username" value="%GITHUB_USERNAME%" />
      <add key="ClearTextPassword" value="%GITHUB_PAT%" />
    </github-opencode-sdk>
  </packageSourceCredentials>
</configuration>
```

> **🔑 GitHub Packages Authentication**: GitHub Packages requires a
> [classic Personal Access Token](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/creating-a-personal-access-token)
> with the `read:packages` scope, even for public packages (fine-grained tokens are not supported
> by the NuGet registry). `--store-password-in-clear-text` is required on Linux and macOS, where
> NuGet cannot encrypt stored credentials. Never commit a real token — the `nuget.config` above
> reads it from the environment. Inside GitHub Actions you need no PAT at all: the workflow's own
> `GITHUB_TOKEN` works as the password.

## 🚀 Quick Start

### The SDK starts the server

No ambient process, no endpoint to configure — the launcher starts a private `opencode serve`
child, mints its credential, and hands you a client bound to it. Disposing the server stops the
child.

```csharp
using OpenCode.Sdk;
using OpenCode.Sdk.Models;

await using var server = await OpenCodeServer.StartAsync();
using var client = server.CreateClient();

var health = await client.GetHealthAsync();
Console.WriteLine($"opencode {health.Health.Version} is healthy: {health.Health.Healthy}");

var created = await client.Sessions.CreateSessionAsync(new SessionCreateRequest { Title = "hello from .NET" });
Console.WriteLine($"session {created.Session.Id}: {created.Session.Title}");

using var window = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await foreach (var @event in client.Events.SubscribeAsync(window.Token))
{
    Console.WriteLine($"{@event.GetType().Name} ({@event.Type})");
}
```

### A server you already run

```csharp
using var client = new OpenCodeClient(new OpenCodeClientOptions
{
    Endpoint = new Uri("http://127.0.0.1:4096"),
    Password = Environment.GetEnvironmentVariable("OPENCODE_SERVER_PASSWORD"),
});

var health = await client.GetHealthAsync();
Console.WriteLine(health.Health.Version);
```

The SDK reads no environment variables of its own — resolving a password from the environment is
the caller's decision, exactly as opencode's own CLI layers it.

### Dependency injection

`OpenCode.Sdk.Extensions` registers one singleton client owning its transport for the container's
lifetime, plus every sub-client resolved from that same instance — so inject `SessionsClient`,
`EventsClient`, or `PtysClient` directly.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenCode.Sdk;
using OpenCode.Sdk.Models;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOpenCode(options =>
{
    options.Endpoint = new Uri("http://127.0.0.1:4096");
    options.Password = Environment.GetEnvironmentVariable("OPENCODE_SERVER_PASSWORD");
});

builder.Services.AddHostedService<SessionWorker>();

await builder.Build().RunAsync();

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

`AddOpenCode(IConfiguration)` binds the same options from a configuration section instead. That
overload is annotated `[RequiresDynamicCode]` / `[RequiresUnreferencedCode]` because configuration
binding reflects over the options type — under trimming or native AOT, prefer the configure-action
overload shown above.

## 🧭 API Coverage

**131 of the 136 operations** in the pinned snapshot are callable. The remaining five are not
omissions — each one is a recorded decision with a named cause, and
[`src/OpenCode.Sdk/.generation-incomplete`](src/OpenCode.Sdk/.generation-incomplete) is the
machine-readable map that the build itself reads.

**Three operations are declined**, because admitting them would mean inventing a contract upstream
does not declare:

- **`v2.config.get`** — the config schema nests unions whose branches are all JSON objects with no
  discriminator (`lsp`'s and `references`' map values), so a decoder cannot tell one branch from
  another without guessing. Moving this needs a new union mechanism, not a mapping row.
- **`v2.fs.read`** — the route is `/api/fs/read/*`, a framework wildcard rather than an OpenAPI
  path template. There is no declared path parameter to bind, and inventing one would put a
  fabricated contract in a generated client. An upstream report is drafted.
- **`v2.experimental.migration.v1.status`** — the same undiscriminated-object-union wall as
  `v2.config.get`, on an operation upstream itself marks experimental.

**Two operations are transport-owned**: `v2.pty.connect` and `v2.persistentPty.connect` are
WebSocket upgrades that the HTTP pipeline cannot carry. They are fully usable — through the
hand-written `PtySession` and `PersistentPtySession` doors described in
[the terminals guide](docs/guide/terminals.md) — they simply are not generated.

## 📚 Documentation

| Guide | What it covers |
|---|---|
| [**The guide**](docs/guide/README.md) | Index of every page below, in reading order |
| [Getting started](docs/guide/getting-started.md) | Install, first call, and the shape of the client family |
| [Connection modes](docs/guide/connection-modes.md) | The standalone launcher, an external server, and DI registration |
| [Streaming](docs/guide/streaming.md) | The global event bus and per-session server-sent event streams |
| [Terminals](docs/guide/terminals.md) | PTY and persistent-PTY sessions over the WebSocket doors |
| [Errors and responses](docs/guide/errors-and-responses.md) | Throwing versus `NoThrow`, and the typed error model |
| [Pagination](docs/guide/pagination.md) | Cursor-carrying list envelopes and `EnumerateMessagesAsync` |

Architecture, decision records, and engineering policy live under [`docs/`](docs) — start at
[`AGENTS.md`](AGENTS.md) if you want the internals rather than the API.

## Known Issues

- **Nothing is on NuGet.org yet.** The `0.1.0` package is prepared but unpublished —
  publication is currently blocked by an upstream package-ID prefix reservation dispute — so
  `dotnet add package OpenCode.Sdk` will not resolve. Use the
  [GitHub Packages nightly feed](#nightly-builds-github-packages) until the first stable release;
  the nightly and stable packages are built from the same sources by two workflows that share the
  same verify-and-pack steps.

- **Response bodies larger than 1 MB allocate an extra copy on `net472` and `netstandard2.0`.**
  The downlevel array pool caps its buckets at 1 MB, so a rent above that cap falls through to a
  fresh allocation and the body is copied once at wire size. Modern targets are unaffected — their
  pool has no such cap. This only shows up on genuinely large payloads (a long session export, for
  example); a larger-capacity pool is a measured, benchmark-gated follow-up rather than a
  speculative change.

- **`PtySession.ReadAsync()` allocates its receive buffer per call.** Each read rents nothing and
  allocates a fresh 16 KiB buffer — measured at 16,776 bytes on the complete read path versus
  24 bytes for decoding alone. Correctness is unaffected; a tight read loop over a chatty terminal
  will produce more garbage than it needs to. Pooling the buffer is a queued optimization, held
  behind the same benchmark gate as the item above so the change ships with evidence.

- **Attaching to an existing background service is not implemented.** opencode's third connection
  mode — discovering a registered daemon through its registration file (`Service.discover` /
  `ensure` / `stop`) — has no SDK parity yet. You can point the client at an endpoint you already
  know, or let `OpenCodeServer.StartAsync()` start a private server; what you cannot do is find a
  daemon someone else started. That parity is a queued follow-up arc, not a defect in what ships.

- **The event bus has no replay contract.** `EventsClient.SubscribeAsync` is a live, volatile
  stream: events published while you are disconnected are gone, and a consumer slower than the
  producer can overflow and fail the stream. This is the server's contract, not an SDK limitation —
  if you need durable history for one session, use the per-session log stream instead
  ([streaming guide](docs/guide/streaming.md)).

- **Running the in-repo sandbox against a different server needs `--no-launch-profile`.** The
  checked-in `launchSettings.json` prefills `OPENCODE_SANDBOX_ENDPOINT` at port 4096, and
  `dotnet run` applies the default profile unless told otherwise — so without the flag the sandbox
  silently addresses 4096 whatever your environment says. The prefill stays deliberately: it is
  what makes zero-argument F5 work against a local server. See
  [`tests/OpenCode.Sdk.Sandbox/README.md`](tests/OpenCode.Sdk.Sandbox/README.md).

## Developing

We appreciate contributions in the form of feedback, bug reports, and pull requests. Read
[CONTRIBUTING.md](.github/CONTRIBUTING.md) first — it carries the full gate and the commit
convention.

### Building the Project

```bash
git clone --recurse-submodules https://github.com/Blind-Striker/opencode-sdk-dotnet.git
cd opencode-sdk-dotnet
dotnet build --configuration Release
```

`external/` holds read-only upstream submodules used as protocol evidence and as the pinned-server
test fixture; `--recurse-submodules` is what makes the fixture-backed tests runnable.

### Sandbox Application

[`tests/OpenCode.Sdk.Sandbox`](tests/OpenCode.Sdk.Sandbox) is a committed playground that drives
the SDK against a real `opencode2 serve` under a debugger — the standing breadth walkthrough, the
SSE stream modes, the PTY legs, and the standalone-launcher demo. Its
[README](tests/OpenCode.Sdk.Sandbox/README.md) documents every mode.

```bash
dotnet run --project tests/OpenCode.Sdk.Sandbox -- --standalone
```

### Running Tests

```bash
dotnet test --configuration Release --no-build
```

The full completion gate — analyzers, formatting, and the suite — is
[`docs/engineering/quality-gates.md`](docs/engineering/quality-gates.md).

## Community

Got questions or wild feature ideas?

👉 Open an [issue](https://github.com/Blind-Striker/opencode-sdk-dotnet/issues) — bug reports,
questions, and proposals all land there for now.

## Changelog

Please refer to [`CHANGELOG.md`](CHANGELOG.md) to see the complete list of changes for each release.

## License

Licensed under MIT, see [LICENSE](LICENSE) for the full text.

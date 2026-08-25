# opencode SDK for .NET

> **Unofficial.** This project is not affiliated with or endorsed by the
> [opencode](https://opencode.ai) team.

A strongly typed .NET client for the [opencode](https://github.com/anomalyco/opencode)
HTTP API — the surface every opencode front-end (TUI, desktop, web UI, plugins) goes
through — generated from a pinned OpenAPI specification and backed by a hand-written
transport runtime.

## Status

Active pre-release development; nothing is published to NuGet yet.

The generator and runtime foundations are implemented and exercised against a real
opencode v2 server. The generation profile currently selects 47 of the 120 operations
in the pinned document and grows in reviewed family batches; packaging stays blocked
until the generated surface is ready for release.

Available today:

- Generated clients, request models, response envelopes, routes, and JSON metadata
- Health, session, message, shell, event, integration, MCP server, and pty API slices
- Session actions with typed request unions (prompt, fork, compact, permissions, export, …)
- Typed API exceptions with per-call `NoThrow` support
- Global and per-session server-sent event streams
- Cursor-based asynchronous pagination
- Dependency injection through `OpenCode.Sdk.Extensions` (`AddOpenCode(...)`)
- Source-generated `System.Text.Json` serialization without reflection fallback
- `netstandard2.0`, `net472`, `net8.0`, `net9.0`, and `net10.0` targets

Not available yet:

- NuGet packages
- The local `opencode serve` launcher
- The planned MCP server
- Complete coverage of the pinned HTTP API

## Example

```csharp
using OpenCode.Sdk;

using var client = new OpenCodeClient(new OpenCodeClientOptions
{
    Endpoint = new Uri("http://127.0.0.1:4096"),
    Password = Environment.GetEnvironmentVariable("OPENCODE_PASSWORD"),
});

var response = await client.GetHealthAsync();

Console.WriteLine(response.Health.Version);
```

## Design

The SDK uses a repository-owned generator because opencode's OpenAPI dialect contains
discriminator-free unions and other shapes that existing .NET OpenAPI generators did
not represent faithfully. The pinned OpenAPI document is the sole protocol input
([`spec/SNAPSHOT.md`](spec/SNAPSHOT.md) owns the exact pin and refresh procedure);
generated output is committed, reviewed as source, locked by a public-API baseline,
and regeneration-verified.

Current architecture and engineering rules:

- [Protocol and generation](docs/architecture/protocol-and-generation.md)
- [Client runtime](docs/architecture/client-runtime.md)
- [Platform and packaging](docs/architecture/platform-and-packaging.md)
- [Roadmap](docs/ROADMAP.md)

Historical research and decision evidence lives under
[`docs/research/`](docs/research) and [`docs/adr/`](docs/adr).

## Building

```bash
dotnet build --configuration Release
dotnet test --configuration Release --no-build
```

See [`AGENTS.md`](AGENTS.md) for repository development conventions.

## Collaboration

Issues, design feedback, and contributions are welcome — and collaboration with the
upstream opencode team is especially welcome if an official or semi-official .NET SDK
moves forward.

## License

[MIT](LICENSE) © Deniz İrgin

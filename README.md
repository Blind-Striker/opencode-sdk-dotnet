# opencode SDK for .NET

> **Unofficial.** This project is not affiliated with or endorsed by the
> [opencode](https://opencode.ai) team.

A .NET SDK for [opencode](https://github.com/sst/opencode) — a typed client for
the HTTP API that every opencode front-end (TUI, desktop, web UI, plugins) goes
through — with an MCP server planned on top of it.

**Status: early development.** Nothing is published to NuGet yet; the public API
surface is still being designed. See the
[roadmap](https://github.com/Blind-Striker/opencode-sdk-dotnet/blob/master/docs/ROADMAP.md)
and [research notes](https://github.com/Blind-Striker/opencode-sdk-dotnet/tree/master/docs/research)
for the current direction and its evidence.

## Planned packages

| Package | Description |
| --- | --- |
| `OpenCode.Sdk` | Core: typed HTTP client, SSE event streaming, `opencode serve` launcher |
| `OpenCode.Sdk.Extensions` | DI integration: `AddOpenCode()`, singleton client family, options binding |

## License

[MIT](LICENSE) © Deniz İrgin

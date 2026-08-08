# Monorepo: the MCP server lives in this repository; packages version independently

Date: 2026-08-08

The MCP server is developed in this repository, not a separate one. It is by design a thin
adapter over our own SDK, and that architecture wants compile-time coupling: SDK breaking
changes surface in the same CI run instead of after a publish (the cross-repo
private-internals dependency was the failure mode that sank the unofficial `opencode-mcp`),
the consumer-driven legacy-test scope (ADR-0005) stays mechanically derivable, and the repo's
infrastructure — analyzer wall, three-OS CI, real-process integration harness, docs
discipline — is paid for once.

Versioning and release: every package (`OpenCode.Sdk`, `OpenCode.Sdk.Extensions`, the MCP
server, future additions) versions independently — no lockstep family, and no alignment with
upstream opencode versions (alignment was weighed and rejected: it would force our own
features onto patch releases; the 2.0 rename wave is absorbed by an explicit breaking major
instead — research log Q24). Intra-repo compatibility is expressed through ordinary NuGet
dependency ranges. CD publishes per-merge (nightly) to GitHub Packages for all packages;
NuGet.org releases run through a manual pipeline. No monorepo build tooling (Nx,
dotnet-affected) at this scale; a small affected-style tool may be written if the need
materializes.

## Consequences

- Distribution of the MCP server as a NuGet `McpServer`-type package is evaluated in the MCP
  design phase (ROADMAP).
- A CI leg that packs the SDK and restores the MCP server against the local feed recovers the
  "dogfood the published artifact" benefit a separate repo would have had.

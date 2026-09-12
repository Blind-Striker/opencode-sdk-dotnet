# Platform and Packaging Architecture

Date: 2026-09-08

Canonical current rules for target frameworks, package boundaries, repository shape, versioning,
distribution, dependencies, and licensing.

## Target frameworks

The package matrix is:

```text
netstandard2.0;net472;net8.0;net9.0;net10.0
```

`net472` owns .NET Framework-specific compile and runtime behavior. `netstandard2.0` is the broad
compatibility bridge and has no runtime of its own; net472 legs proxy its downlevel behavior.
`net11.0` is a post-1.0 light-up, not a current target (ADR-0002).

Modern C# on downlevel targets is deliberate and supported inside this repository by the private,
source-only Polyfill package. Exact package versions belong to `Directory.Packages.props`, not this
document.

### WebSocket support

`ClientWebSocket` backs the PTY session door (`client-runtime.md`). It resolves on
`netstandard2.0` and on `net472` from the platform's own reference set, so **no package reference
is added for it** on any target — the dependency rule below is satisfied without a new dependency.

`net472` is the constrained leg: its `ClientWebSocket` is a thin wrapper over the operating
system's WebSocket stack and therefore requires **Windows 8 / Windows Server 2012 or later**. A
PTY session on an older Windows fails at connect rather than degrading. CI's Windows leg covers
that target.

Two separate API vintages shape the downlevel legs, and conflating them would misdate both:

- The `Memory`-based receive and send overloads arrived with .NET Core 2.1 and are absent from
  `netstandard2.0` and `net472`, so those legs use the `ArraySegment` overloads. This is a
  buffer-shape difference only.
- `ClientWebSocketOptions.CollectHttpResponseDetails` and `ClientWebSocket.HttpStatusCode` — the
  pair that recovers a refused upgrade's HTTP status — are .NET 7 and later. Both downlevel legs
  therefore report a failed upgrade without its status (`client-runtime.md`).

## Packages

- `OpenCode.Sdk` is the core typed client, and the local server launcher ships inside it
  (ADR-0001).
- `OpenCode.Sdk.Extensions` owns dependency-injection registration. DI dependencies do not enter the
  core package.
- The two names above are assembly and namespace names. The published package ids are
  `OpenCodeAI.Sdk` and `OpenCodeAI.Sdk.Extensions`, because nuget.org reserves the `OpenCode.`
  prefix for an unrelated owner. Each packable project sets its own `PackageId`; the assemblies,
  the public namespaces, and therefore consumer source are unaffected, and a project reference
  packs as a dependency on the corresponding package id.
- Exact package references and dependency versions are read from project files and
  `Directory.Packages.props`. Documentation records policy, not a second version inventory.
- A future package is added only for a real distribution boundary; repository layout alone does
  not justify another artifact.

## Dependencies

- Terminal sessions use `System.Threading.Channels` for their internal receive queue. The
  SDK declares that bridge package directly for `net472` and `netstandard2.0`; modern targets
  use the inbox implementation. Channel types are not part of the public SDK API.

- Declare explicitly every package this repository's source uses directly, that appears on a
  public surface, or that is version-pinned for behavior; trust the transitive graph otherwise.
- Downlevel bridge packages (`System.Memory`, `System.Buffers`, `System.Collections.Immutable`,
  `Microsoft.Bcl.*`) are conditioned to the target frameworks that need them; modern targets use
  the inbox APIs.
- A new shipped dependency is a maintainer decision recorded with its consumer; no package is
  added for a capability no scheduled work consumes.

## Repository and versioning

The SDK and planned MCP server share one repository. The MCP server remains a thin adapter over the
SDK, so its implementation compiles against SDK API changes in the same build (ADR-0006).

Every package versions independently. Package versions do not align with upstream opencode and do
not move in lockstep with one another. Intra-repository compatibility uses ordinary NuGet dependency
ranges (ADR-0006).

The release policy is per-merge publication to GitHub Packages and a manual pipeline for NuGet.org.
Pre-1.0 numbering and concrete workflow mechanics remain operational decisions until their release
work is scheduled (ADR-0006).

## Licensing

Repository packages use the MIT license and ship it through `PackageLicenseFile`. Project and pack
files own the mechanical packaging configuration.

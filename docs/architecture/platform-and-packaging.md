# Platform and Packaging Architecture

Date: 2026-08-18

Canonical current rules for target frameworks, package boundaries, repository shape, versioning,
distribution, dependencies, and licensing.

## Target frameworks

The package matrix is:

```text
netstandard2.0;net472;net8.0;net9.0;net10.0
```

`net472` owns .NET Framework-specific compile and runtime behavior. `netstandard2.0` is the broad
compatibility bridge and has no runtime of its own; net472 legs proxy its downlevel behavior.
`net11.0` is a post-GA light-up, not a current target (ADR-0002).

Modern C# on downlevel targets is deliberate and supported inside this repository by the private,
source-only Polyfill package. Exact package versions belong to `Directory.Packages.props`, not this
document.

## Packages

- `OpenCode.Sdk` is the core typed client. The local server launcher belongs in this package when
  its milestone lands (ADR-0001); `docs/ROADMAP.md` owns delivery status.
- `OpenCode.Sdk.Extensions` owns dependency-injection registration. DI dependencies do not enter the
  core package.
- Exact package references and dependency versions are read from project files and
  `Directory.Packages.props`. Documentation records policy, not a second version inventory.
- A future package is added only for a real distribution boundary; repository layout alone does
  not justify another artifact.

## Dependencies

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

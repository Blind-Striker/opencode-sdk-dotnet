# Launcher lives in the core package

Date: 2026-08-08

`OpenCode.Sdk` itself ships the `opencode serve` process launcher (start, readiness probe,
stop), hand-rolled on `System.Diagnostics.Process` with no process library. Upstream sets the
parity bar — `@opencode/client` ships `Service.ensure()`, which spawns `opencode serve --service`
from inside the client package — and the MCP C# SDK does the same (`StdioClientTransport` spawns
inside `ModelContextProtocol.Core`; kept as reference implementation). Evidence and lifecycle
anatomy: internal research, 2026-08-08, ".NET SDK design decisions: packaging, process management,
TFMs"; research log Q12.

The background-service door's process control (ADR-0026) binds one function of the platform C
library, `kill(2)`, for the Unix signal rungs that `System.Diagnostics.Process` cannot send; that
is a platform primitive every process already has loaded, not a process library, and this record's
rule is unchanged by it.

The background-service door's contender spawn (ADR-0027) binds the platform's own process-creation
primitives, `CreateProcessW` and `posix_spawnp`, for the detached session that
`System.Diagnostics.Process` cannot open; that is the same class of platform primitive every
process already has loaded, not a process library, and this record's rule is unchanged by it.

## Considered options

- **Separate launcher package** — rejected: upstream parity argues for core, and the launcher
  is the SDK's own bootstrap path (a client without a server is useless in the local-first
  case).
- **CliWrap** — rejected for the product: the lifecycle is one known binary with known
  arguments; a dependency is not warranted. The rule is product-scoped — repo tooling may use
  CliWrap freely.
- **.NET 11 process APIs** (`ReadAllLinesAsync`, `KillOnParentExit`) — net11-only; planned as
  light-up, not baseline.

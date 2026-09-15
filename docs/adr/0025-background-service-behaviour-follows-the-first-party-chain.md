# Background-service behaviour follows the accepted-pin first-party chain

Date: 2026-09-16

The registration file, its channel-dependent path, legacy migration, the health probe, and the
Ensure/Stop lifecycle are not in the OpenAPI document. The SDK ports the first-party CLI/Effect
chain at the accepted pin (`ServerConnection.resolve` -> `ServiceConfig.options` -> `Service` ->
`ServiceRegistration`/`ServerProcess`) and pins every consumed file in `spec/source-watch.json`;
the public Promise helper and the Desktop integration are comparison evidence only, because the
Promise module resolves no channel and the CLI is the only writer of the files the SDK reads.
Where the SDK cannot mirror a compile-time fact (channel, installed version, self-spawn command)
it carries a named projection in the design and never a hidden default.

## Considered options

- Mirror the public Promise helper: simpler, but it hardcodes `<state>/opencode/service.json` and
  drifts from what the installed CLI actually does. Rejected.

Reversal: upstream moves registration and lifecycle into a package the client SDKs consume
directly, or replaces the file-based registration with another discovery contract.

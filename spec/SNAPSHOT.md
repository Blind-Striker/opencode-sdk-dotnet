# OpenAPI Snapshot

Date: 2026-08-08

`openapi.json` is a pinned copy of the upstream opencode OpenAPI 3.1 document. The SDK is built
against this snapshot, not against the live submodule.

| Fact | Value |
|---|---|
| Upstream file | `external/opencode/packages/sdk/openapi.json` |
| Submodule commit | `d7b115f623760e68a4749d16508a9eca350f246f` |
| Upstream tag | `v1.18.15` |

## Refresh procedure

1. Update the `external/opencode` submodule to the desired upstream commit.
2. Copy `external/opencode/packages/sdk/openapi.json` over `spec/openapi.json`.
3. Update the table above (commit, tag) and the `Date:` line.
4. Review the diff; regenerate the model layer once codegen exists.

A dedicated spec-refresh tool is planned — see repo tooling in `docs/ROADMAP.md`.

# Documentation

Date: 2026-08-18

Canonical rules for documentation ownership, authority, lifecycle, references, and lossless
maintenance.

## Document roles

| Role | Homes | Meaning |
|---|---|---|
| Agent entry point | `AGENTS.md` | Universal operating rules and task-to-canon routing only |
| Current architecture | `docs/architecture/` | Normative current technical decisions |
| Current engineering practice | `docs/engineering/` | Normative current authoring, quality, workflow, and documentation rules |
| Decision rationale | `docs/adr/` | Accepted decision context, trade-offs, consequences, and reversal triggers |
| Dated evidence | `docs/research/` | Historical question-to-finding-to-decision chain; may intentionally contain superseded positions |
| Domain vocabulary | `CONTEXT.md` | Current terms and explicitly avoided synonyms |
| Operational state | `docs/ROADMAP.md` | What is done, next, open, and known to be incomplete; shrinks as work lands |
| Protocol provenance | `spec/SNAPSHOT.md` | Exact upstream pin and refresh procedure |
| Agent-only operation | `docs/agents/` | Guidance needed only by coding agents |
| Session continuation | `docs/agents/handover-prompts/` | Temporary live handoff, consumed against Git and deleted when its work ships |
| Transient reference | `docs/superpowers/` | Vision, plans, and rationale that are not canonical or operational authority |

Repository files and current canonical documents beat memory, dated research, transient plans, and
handoffs. If two current canonical sources appear to disagree, do not pick a winner silently; use
the deviation protocol.

## One canonical home

Every current fact has one canonical owner. Other appearances are short relays that link to the
owner instead of restating the full rule. Change-prone values are read from their mechanical source
instead of copied into prose.

Documentation refactors are lossless relocations, not summarization exercises. Preserve useful,
unique information in an appropriate canonical, rationale, evidence, or operational home. Delete a
passage only after proving it is an exact duplicate, obsolete without historical value, or retained
in a more authoritative source. Git history is not a substitute for accessible, useful canon.

Current-state documents describe the status quo rather than narrating their amendment history.
Dated research preserves the history and may state decisions later superseded; it must never be
used as current policy without following its links to current canon. ADRs record decision rationale,
not work-queue state.

## Dates and lifecycle

Every hand-written document under `docs/` carries a `Date:` line.

- In current-state and operational documents, `Date:` is the latest substantive update.
- In ADRs, `Date:` is the date of the decision represented by the current record; a material
  decision revision moves it, while an editorial link correction does not.
- In research, `Date:` identifies the evidence snapshot or the latest session included by a
  chronological log. Editorial corrections do not rewrite the historical evidence date.

A sentence that must change when a task completes belongs in `docs/ROADMAP.md` or a temporary
handoff, not in evergreen architecture or engineering canon. Keep affected documentation current in
the same change as code; documentation is not follow-up work.

## References

References point one way: documentation may cite code, but code artifacts never cite documentation.
Comments in source, project files, `.editorconfig`, workflows, and generated files explain the
status quo locally; they do not point to movable prose or narrate decision history.

Audience decides placement. `docs/agents/` contains only agent-specific operation; knowledge shared
by humans and agents belongs in architecture, engineering, ADR, research, domain, or operational
documents.

ADRs are created lazily under the criteria in `docs/adr/README.md`. Handovers are consumed against
live Git and GitHub, then deleted when the follow-up ships or a newer handoff supersedes them.

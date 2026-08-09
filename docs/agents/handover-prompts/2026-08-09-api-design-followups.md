# Handover: API design session follow-ups

Date: 2026-08-09

Cross-session state left by the public-API design session (2026-08-08/09, ROADMAP queue
item 1). Consume against live git; delete when the follow-ups ship.

## Produced (committed in the session-closing doc pass)

- `docs/superpowers/specs/2026-08-09-public-api-design.md` — the design spec. All
  decisions sealed one-by-one with the maintainer during the session; §15 lists deferrals.
- `docs/research/10-v2-to-2.0-operation-mapping.md` — background-research output; corrects
  doc 09's direction (the "2.0 branch" is an April-2026 ancestor, not the destination).
- `.scratchpad/api-design-session-notes.md` — session working notes (gitignored, not
  committed). Superseded by the spec; keep until the grill session ends if useful, then
  delete.

## Doc pass — completed 2026-08-09, same session (maintainer-approved, single commit)

- Spec + research doc 10 + this handover committed.
- `docs/ROADMAP.md`: status + queue item 1 updated (design done; remaining: grill →
  generator session → writing-plans); open questions resolved by the spec removed
  (typed event model, HttpClient ownership, directory targeting, `pty.connect`, auth
  shape, CS1591 parked decision); launcher deep-dive items added.
- `docs/adr/0005-both-api-surfaces.md`: deletion premise re-grounded per doc 10 (no
  verified rename wave; next major's shape/date unverified; live signals carry the
  direction). `AGENTS.md` locked-decision statement aligned (docs 08, 09, 10).
- `.editorconfig`: guard comment added recording that MA0053's
  `class_with_virtual_member_shoud_be_sealed` option is deliberately off (mock seam).

## Session sequence agreed with the maintainer

1. **Grill session** (next; fresh context, no brainstorm history — interrogation, not
   defense): target = the spec. ADR candidates already identified: error-model deviation
   (spec §4.4), generation boundary (§8.4), unknown-variant tolerance rule (§11.2).
2. **Generator architecture session** (after the grill): parser/IR, emission layering,
   curation-config format, `.g.cs`-vs-on-merit mechanics, spec-refresh tooling, emitter
   test strategy.
3. **writing-plans**: multi-phase implementation plan (transport core → generator →
   model/surface emission → SSE/event model → launcher → Extensions/packaging is the
   rough shape; the plan session decides).

## Lessons for the next driver

- Onboard against the FULL Sources of Truth chain before designing/grilling — this
  session got caught twice referencing designs whose depth lived in unread docs
  (launcher anatomy in doc 06 §3, streaming decisions in doc 02). Read the research log
  (doc 00) end-to-end first; it indexes everything.
- Conversation in Turkish, artifacts in English; align on structure before writing;
  every canonical-doc edit needs a per-edit OK; commits need maintainer approval.

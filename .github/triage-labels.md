# Triage Labels

Date: 2026-09-08

What the labels on this repository's issues mean. Every new issue starts at `needs-triage`; the
rest describe where it went from there.

| Label | Meaning |
|---|---|
| `needs-triage` | Filed, not yet evaluated. The maintainer decides what it is and where it goes. |
| `needs-info` | Evaluated, but it cannot move until the reporter answers something. Stale ones are closed. |
| `ready-for-human` | Specified enough to work on, and it needs a person: a design call, a canon change, or a judgement the issue cannot state for you. |
| `ready-for-agent` | Specified enough that the work is mechanical — the change, the files, and the passing condition are all named in the issue. |
| `bug` | Behaviour that contradicts what is documented. |
| `enhancement` | Something the SDK does not do yet. |
| `performance` | Speed or allocation work. Claims here are settled by the benchmark suite, never by inspection. |
| `documentation` | Only prose changes. |
| `question` | A usage question. Answered, then closed. |
| `good first issue` | Small, self-contained, and does not require reading the generator to start. |
| `help wanted` | Would be welcome from anyone; not on the maintainer's own queue. |
| `duplicate`, `invalid`, `wontfix` | Closed, with the reason in a comment. |

`ready-for-human` and `ready-for-agent` split the same state — specified and actionable — by what
kind of attention the work needs, not by who is allowed to take it. Anyone may pick up either.

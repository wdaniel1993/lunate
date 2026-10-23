## ADR: the gate has two stages, and the full one is required at review

**Status:** proposed (2026-10-23)

**Context.** `AGENTS.md` required the full `scripts/verify.sh` "before a change is done". Agents applied it to every commit, which serialises work behind a 20-minute command and still leaves "done" undefined between commits.

**Decision.** Name both stages in `AGENTS.md`: fast gate while iterating, full gate before ready-for-review or done, `ci-ok` green before merge. Content of both gates unchanged.

**Consequences.** Intermediate commits are cheap; the review hand-off and the merge each keep a hard gate. A reviewer can verify the claim with one command (`scripts/verify.sh`) rather than trusting a summary.

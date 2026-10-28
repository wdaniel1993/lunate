# ADR review — context-hygiene

ADR review completed for this change.

**In-force ADR context reviewed:** 0003 (own loop on `IChatClient`), 0012 (tool contract and
declaration adapter), 0013 (agent loop), 0015 (session format), 0016 (workspace boundary), 0018 and
its addenda (session schema 2, images, branching), 0021 (per-run workspaces), 0022 (model catalog
and provider auth). 0005 (budgets) and 0010 (provider caching) constrain the measurement style but
not this decision.

**New durable decision recorded:** `adr/0012-addendum-context-hygiene.md`, status **proposed**,
awaiting maintainer sign-off. It amends ADR-0012 decision 3 (the 30,000-character bound) rather than
editing it, and records the context-admission order, the token-threshold spill, tool-authored
concision, the read ledger, and masking before the context. The prior ADR and the #196 boundary
stay as written history; the addendum is the live commitment once accepted.

**Why an ADR rather than a tactical choice:** the change moves where the loop decides what enters
the model's context, adds one public result member and two options, and reverses a boundary the repository
documents ("the in-memory result keeps what the model saw"). Future changes inherit that order. It
is not reversible by reverting one helper.

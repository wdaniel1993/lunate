# ADR Review Manifest

- Status: completed
- Review date: 2026-11-14

## Review Summary

ADR review completed for this change.

The seam this change implements was introduced by `#626` and its shape was decided by the S-7 spike
(`docs/dev/spikes/S-7/report.md`), which explicitly concluded that no new ADR is warranted: the
decision is a scope decision on the execution-environment cards, not a new repository-level
architecture rule, and it is fully reversible once the seam exists. This change keeps that shape —
one long-lived container per session, no per-call container, no path translation, no in-container
guard — so the moment the spike named for an ADR ("if #627 changes the shape further, for example a
per-call container instead of a per-session one") has not arrived.

The security boundary that the card held open (`BashGuard` versus an in-container guard) was decided
by the maintainer on `#822` and is recorded in the `execution-environment` specification and
`docs/execution-environments.md` / `docs/security.md`, not in a new ADR.

## In-Force ADRs Reviewed

- `adr/0003-the-agent-loop-runs-tools.md` — the loop, approvals and cancellation stay in
  `Lunate.Agent`; the container only changes where the tool's process runs.
- `adr/0017-extension-contract-and-hooks.md` — the extension/MCP process path keeps consuming
  `IProcessRunner`, which still delegates to one execution environment.
- `adr/0035-tool-environment-pass-through.md` and
  `adr/0036-tool-child-credential-shape-strip.md` — the tool child's environment policy is unchanged;
  the container receives only explicitly forwarded variables, never the inherited host environment.

## New Durable ADRs Created

- None - no major durable architectural decisions were introduced.

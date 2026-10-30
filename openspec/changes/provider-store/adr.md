# ADR Review Manifest

- Status: completed
- Review date: 2026-10-30

## Review Summary

ADR review completed for this change. The provider-instance/profile/usage model diverges from ADR-0022's preset-instance and API-key-only model, and schema-4 usage attribution extends the pinned session contract. These are durable architectural decisions requiring maintainer sign-off before apply. This proposal-only card is constrained to files under `openspec/`, so it does not create a repository-level ADR. Before implementation, record the accepted decision in the repository ADR sequence; the next available number at review time is 0027. The new ADR must explicitly supersede ADR-0022 and account for the schema-4 extension to ADR-0015, without editing either historical ADR.

## In-Force ADRs Reviewed

- ADR-0001 — single-file releases and budgets
- ADR-0002 — layering and Microsoft.Extensions.AI types
- ADR-0003 — loop ownership
- ADR-0004 — terminal input support
- ADR-0005 — startup budget calibration and startup-floor addendum
- ADR-0006 — Roslyn placement
- ADR-0007 — live-area concurrency
- ADR-0008 — release publish flags (including its limited supersession of ADR-0001 wording)
- ADR-0009 — memory budgets
- ADR-0010 — Anthropic adapter
- ADR-0011 — repository quality tooling
- ADR-0012 — tool contract
- ADR-0013 — agent loop
- ADR-0015 — pinned session format
- ADR-0016 — workspace boundary
- ADR-0017 — extensibility architecture and accepted command, extension, model-registry, package, remote-host, and skills addenda
- ADR-0019 — terminal UI choice
- ADR-0020 — ACP SDK choice
- ADR-0021 — per-run workspaces
- ADR-0022 — model catalog and provider auth
- ADR-0023 — first-party extensions
- ADR-0024 — bash guard
- ADR-0025 — verified edits

Proposed ADRs in the folder, including ADR-0026, were reviewed as context but are not treated as in-force decisions. No accepted ADR is rewritten or renumbered.

## New Durable ADRs Created

- None in this proposal-only card. The change-local design and specs are the proposal for review; the repository-level ADR must be recorded and accepted by maintainers before apply.

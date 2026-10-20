# ADR Review Manifest

- Status: completed
- Review date: 2026-10-20

## Review Summary

ADR review completed for `provider-presets`. The change introduces one new durable record,
`adr/0022-model-catalog-and-provider-auth.md`. No existing ADR is edited or superseded; ADR-0002
(MEAI types), ADR-0010 (Anthropic adapter), ADR-0015/0018 (session format) and ADR-0021 (workspaces)
remain in force and constrain the design.

## In-Force ADRs Reviewed

- ADR-0001 — no-AOT single-file budgets
- ADR-0002 — layering and MEAI model types
- ADR-0003 — loop owns tool calls
- ADR-0004 — mintty input support
- ADR-0005 — budget calibration
- ADR-0006 — Roslyn placement
- ADR-0007 — live-area concurrency
- ADR-0008 — release publish flags
- ADR-0009 — memory budgets
- ADR-0010 — Anthropic adapter (plus the prompt-caching addendum)
- ADR-0011 — repo quality tooling
- ADR-0012 — tool contract
- ADR-0013 — agent loop
- ADR-0014 — C# backend interface
- ADR-0015 — session format pinned by golden files
- ADR-0016 — workspace boundary
- ADR-0017 — extensibility architecture (plus the model-registry, remote-host and skills addenda)
- ADR-0018 — session schema 2
- ADR-0019 — Xenoatom terminal UI
- ADR-0020 — ACP SDK choice
- ADR-0021 — per-run workspaces and per-workspace services

## New Durable ADRs Created

- `adr/0022-model-catalog-and-provider-auth.md` — the models.dev-generated catalog, schema-2
  pricing/capability fields, provider presets and id namespace, API-key auth (no subscription
  OAuth), display-only base-rate pricing, and additive session usage lanes.

## Notes

- ADR-0015/0018 are honored through an additive schema 3 usage object; schema 1/2 goldens stay
  byte-identical and no prior ADR is edited.
- ADR-0017 is unchanged because the event and extension surfaces keep their shapes; the model
  catalog and presets are core composition, not extension API.

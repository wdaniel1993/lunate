# ADR Review Manifest

- Status: completed
- Review date: 2026-10-31

## Review Summary

The sign-off settled a durable decision that the earlier drafts deliberately left open: the decision
layer is a **general-purpose primitive** over the Jev-compatible API, its contract is extension-facing,
and approvals, questions, routing and a `decide` tool are consumers of it — with escalation, not
veto, as the approval gate's only action. That is recorded as **ADR-0028** in this change.

The decisions ADR-0028 builds on are already in force: user-owned `decision` selection, technical
contract validation with retry-once, `native|adapter` provenance, schema-4 attribution (ADR-0027),
the loop's ownership of the approval seam (ADR-0003, ADR-0013), and the extension-event surface
(ADR-0017).

The ADR-0027 duplicate is resolved: the amended record
(`adr/0027-provider-store-model-profiles-and-usages.md`) governs, the earlier draft was deleted under
#339 (PR #370), and the docs gate now checks that ADR numbers are unique and dense.

## In-Force ADRs Reviewed

- `0002-layering-and-meai-model-types.md` — model types come from Microsoft.Extensions.AI; the
  primitive's contracts are its own types, not a parallel message type.
- `0003-loop-own-vs-maf-harness.md` — the loop owns running tools and everything between tool calls,
  so it owns propose/validate/decide/record and the approval seam.
- `0010-anthropic-adapter.md` — provider adapter paths are the background for the `native` versus
  `adapter` method labels.
- `0013-agent-loop.md` — approval seam placement and the approval-request surface the decision
  subject rides on.
- `0015-session-format.md` — a persisted extension requires an explicit schema version; the
  schema-4 attribution extension is recorded by ADR-0027, so no new session-format decision here.
- `0017-extensibility-architecture.md` — extensions consume the platform through Abstractions
  services and observe through events; `IDecisionService` follows that surface rather than a new
  channel.
- `0027-provider-store-model-profiles-and-usages.md` — the governing accepted record: user-owned
  `decision` selection, contract validation, provenance, opt-in shadow mode, tighten-only invariant,
  schema-4 attribution.
- ADR-0022 remains superseded; its preset-only provider assumptions no longer constrain this design.

## New Durable ADRs Created

- `adr/0028-decision-layer-primitive.md` — the decision layer is a general primitive: contract shape
  (state plus named questions, three kinds), public `IDecisionService` surface in Abstractions, four
  opt-in consumers, escalate-not-veto approval semantics, and the unchanged safety invariants.

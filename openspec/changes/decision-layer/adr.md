# ADR Review Manifest

- Status: completed
- Review date: 2026-10-31

## Review Summary

This change is design-only: no durable architectural decision is settled here beyond the
sign-off the design already requests. The decisions that would outlive the change — user-owned
model choice for `decision`, technical contract validation with retry-once, `native|adapter`
provenance, schema-4 attribution, and the tighten-only approval invariant — are already recorded
in the accepted ADR-0027 records and ADR-0015. The one surface not yet decided (which consumers
act in 0.2, and whether the primitive's contract types are public) stays a blocking sign-off item
in the change's design; an ADR would be written at implementation sign-off if the maintainer's
outcome settles a decision the 0027 records do not cover.

No new record under `adr/` was created: creating ADR-0028 now would either duplicate
ADR-0027's decisions or record an answer to a question still open on #183.

## In-Force ADRs Reviewed

- `0002-layering-and-meai-model-types.md` — model types come from Microsoft.Extensions.AI; the
  primitive's contracts are loop-owned types, not parallel message types.
- `0003-loop-own-vs-maf-harness.md` — the loop owns running tools and everything between tool
  calls, so it owns propose/validate/decide/record and the approval seam.
- `0010-anthropic-adapter.md` — provider adapter paths are the background for the `native`
  versus `adapter` method labels.
- `0013-agent-loop.md` — approval seam placement and the approval-request surface the decision
  subject rides on.
- `0015-session-format.md` — a persisted extension requires a explicit schema version; the
  schema-4 attribution extension is recorded by the accepted 0027 records, so no new
  session-format decision is created here.
- `0017-extensibility-architecture.md` — consumers observe through the extension-event surface;
  front ends consume decisions via events and configuration, not direct references.
- `0027-provider-store-and-model-profiles.md` — user-owned `decision` selection, contract
  validation, provenance, opt-in shadow mode, tighten-only invariant, schema-4 attribution.
- `0027-provider-store-model-profiles-and-usages.md` — second accepted record with the same
  decisions from the `provider-store` sign-off; the numbering collision is tracked by the
  `adr-numbering` change and #339's follow-up.
- ADR-0022 remains superseded; its preset-only provider assumptions no longer constrain this
  design.

## New Durable ADRs Created

- None - the design settles no decision outliving this change; the open items are blocking
  maintainer sign-off, listed under `Sign-off (blocking)` in this change's `design.md`.

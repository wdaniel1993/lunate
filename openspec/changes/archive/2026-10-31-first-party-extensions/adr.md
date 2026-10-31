# ADR Review Manifest

- Status: completed
- Review date: 2026-10-29

## Review Summary

ADR review completed for this change. The tier is a new durable commitment ("what supported promises"),
recorded as ADR-0023 from the maintainer's ruling on #242. It composes with the in-force extension
architecture without touching it: the tier is a repository and packaging contract, not a runtime scope.

## In-Force ADRs Reviewed

- ADR-0017 (and addenda): extension architecture — the tier keeps extensions ordinary at load time; no
  supersession needed.
- ADR-0021: per-run workspaces / per-workspace services — no interaction; the promoted extensions use
  none of the new service primitives.
- ADR-0022: model catalog / provider auth — review confirmed `model-router` stays unpromoted (its
  provider + virtual-model surface needs a configuration story that does not exist).

## New Durable ADRs Created

- [`adr/0023-first-party-extensions.md`](../../../adr/0023-first-party-extensions.md)

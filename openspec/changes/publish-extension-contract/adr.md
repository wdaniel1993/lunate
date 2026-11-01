# ADR Review Manifest

- Status: completed
- Review date: 2026-11-01

## Review Summary

ADR review completed for this change. Package publication does not introduce a new architecture,
dependency, or extension API shape. The stable package pair and its `ExtensionApi` versioning are
already set by ADR-0002's accepted addendum; the publication ordering is set by ADR-0026. The design's
deprecation wording follows the existing major-version and type-forward policy. The highest existing
ADR sequence is 0028; no new repository-level ADR was warranted.

## In-Force ADRs Reviewed

- ADR-0001 through ADR-0013, including accepted addenda and amendments (ADR-0008 amends the release
  shape in ADR-0001; ADR-0012 has its context-admission addendum).
- ADR-0015 and ADR-0016.
- ADR-0017 and its accepted addenda for extensions, packages, remote host, model registry, skills,
  commands, and the write-verifier extension point.
- ADR-0019 through ADR-0027, including ADR-0027's partial supersession of ADR-0022 and ADR-0026's
  publication-order condition.

Proposed ADRs were reviewed as drafts, not treated as in-force decisions.

## New Durable ADRs Created

- None — no major durable architectural decisions were introduced.

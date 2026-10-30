# ADR index — verified edits

The durable decision for this change is ADR-0025
([`adr/0025-verified-edits.md`](../../../adr/0025-verified-edits.md)), amended with maintainer
sign-off on 2026-10-30: C#-only first slice, staged content compiled in memory with source
generators and the workspace-loaded TFM, multiset refusal baseline, configurable verification,
bounded visible progress, and a public manifest-declared verifier seam. ADR-0017's append-only
addendum records the seam placement. These decisions do not supersede ADR-0006 (in-process
Roslyn), ADR-0012 (tool contract), ADR-0014 (backend interface) or ADR-0021 (per-workspace services
and laziness).

In-force ADR context reviewed: 0005 (budget calibration — the latency-budget rule), 0006 (Roslyn
in-process, no server process), 0012/0014 (tool contract and backend interface), 0017 and its
addenda (the landed extension contract this design places the check beside), 0021 (per-workspace
services and the first-use laziness rule), 0023 (the supported extension tier — where a
second-language LSP post-write half already exists), 0024 (the latest in-force ADR).

This artifact exists to satisfy the `intent-driven` schema. It does not restate ADR-0025: the
repository-level ADR is the one home for the decision.

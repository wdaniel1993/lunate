# ADR index — verified edits

The durable decision for this change is the **proposed** ADR-0025
([`adr/0025-verified-edits.md`](../../../adr/0025-verified-edits.md)): C#-only first slice, staged
content compiled against its project in memory, refuse-only-introduced-errors with a retry cap, a
warm budget with a loud unverified degradation, and the guard living in the `write`/`edit` tools.
It relates to ADR-0006 (in-process Roslyn), ADR-0012 (tool contract), ADR-0014 (backend
interface) and ADR-0021 (per-workspace services, laziness); it does not supersede any of them.

In-force ADR context reviewed: 0005 (budget calibration — the latency-budget rule), 0006 (Roslyn
in-process, no server process), 0012/0014 (tool contract and backend interface), 0017 and its
addenda (the landed extension contract this design places the check beside), 0021 (per-workspace
services and the first-use laziness rule), 0023 (the supported extension tier — where a
second-language LSP post-write half already exists), 0024 (the latest in-force ADR).

This artifact exists to satisfy the `intent-driven` schema. It does not restate ADR-0025: the
repository-level ADR is the one home for the decision.

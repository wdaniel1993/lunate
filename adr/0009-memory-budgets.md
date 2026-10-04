# 0009 — Memory budgets: idle, working set, Roslyn (method and values)

- Status: accepted — 2026-10-04 (maintainer sign-off)
- Date: 2026-10-04
- Evidence: `docs/spikes/S-5/evidence/mem-decomposition.txt` (probe: `docs/spikes/S-5/mem-probe.sh`), S-3 report (method + decomposition), S-4 report addendum (Roslyn)
- Relates to: ADR 0005 (startup budgets — same calibration rule), ADR 0001 (single-file releases), ADR 0008 (no single-file compression)

## Context

The idle budget (100 MB) was a guess. Reference points for comparable agents
(maintainer's brief, 2026-10-04):

- Pi (Node): ~91.5 MiB RSS at ready per a published comparison
  (bigfish1913/pi-rust #29); ~170 MB peak during a session with 30 short bash
  tool calls and no extensions, 172–291 MB with single extensions, ~1 GB with
  a leaking extension (lmilojevicc/pi-zentui #156).
- Quick local measurement (Linux x64 container, no model configured, 8 s idle,
  older Pi npm release, not a benchmark): Pi 0.73.1 idle TUI 158 MB;
  Tau 0.4.7 (Python + Textual) idle TUI 75 MB.
- Lunate: 82 MB idle peak RSS in the S-5 baseline harness (osx-arm64, single
  file + R2R, uncompressed).

Conclusion: Lunate's idle is fine for its class; **growth during work is the
real risk**. The budgets below gate idle loosely and put the weight on
working-set and Roslyn growth. The full decomposition (runtime, packaging,
R2R, Spectre, GC heap) is in the S-3 report addendum.

## Decision (proposed)

- **Idle**: gate **150 MB** peak RSS (82 × 1.5 ≈ 123, rounded up to the next
  50), target **100 MB**. The target is reported, not enforced; per-OS values
  (RSS accounting differs across OSes), like the startup budgets.
- **Working set**: peak RSS while replaying a recorded session with ~30 tool
  calls through the real loop and TUI. Placeholder gate **250 MB** until
  calibrated; calibration is enabled once T-09 (loop) and T-22 (TUI wiring)
  exist. The run uses `ReplayChatClient` and a committed fixture, so it needs
  no API keys and runs in CI.
- **Roslyn**: peak RSS after loading a solution and after 10 edit→diagnostics
  cycles, as a separate lazy state. Calibrated from the S-4 addendum: median
  load 489 MB, edits 484 MB on a 47-project workspace (process tree) → gate
  **750 MB** (489 × 1.5 ≈ 734, rounded up) for that reference workspace class.
- **Rule**: where data exists, median + ~50%, rounded up to the next 50 MB —
  the same rule as ADR 0005's startup budgets.
- **Method**: peak RSS is sampled **from outside the process** with the same
  sampler on all OSes (`scripts/memory.sh`); budgets are per OS because RSS
  accounting differs. `scripts/memory.sh` ships the idle mode now (report mode
  until the TUI subject exists in T-18), with `workingset` and `roslyn` modes
  stubbed as TODO carrying the enabling card numbers (T-22, T-25).
- **Ownership**: values live with the CI matrix (`memory_gate_mb`) and the
  script defaults; re-calibration follows the S-3 discipline.

## Alternatives considered

- **Recalibrate idle upward only / drop memory budgets**: rejected — idle was
  never the risk; growth (tool loops, Roslyn) is, and it needs named budgets
  before it can regress silently.
- **Enforce idle as a hard gate now**: rejected — no real TUI subject exists
  before T-18; idle reports until then.
- **Adopt `DOTNET_GCConserveMemory` / server-GC switches**: rejected — no
  measured effect (the heap is ~25 KB; the floor is runtime + R2R images).
- **Extend ADR 0005 (startup) to cover memory**: rejected — 0005 is scoped to
  startup calibration; memory has a different method and lifecycle.
  Cross-referenced instead.

## Consequences

- The guide's budget table carries the three budgets; `scripts/memory.sh`
  provides the sampler; CI wires `memory_gate_mb` per OS.
- T-18/T-19 flip idle from report to gate once the real TUI subject exists;
  T-22 calibrates working set; T-25 calibrates Roslyn against fixture
  solutions (smaller than the Polly reference — the gate may tighten).
- Re-open when the real measurements land, or if a later .NET runtime changes
  the floor materially.

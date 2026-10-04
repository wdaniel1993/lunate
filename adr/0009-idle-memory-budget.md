# 0009 — Idle-memory budget: keep 100 MB, scoped; the floor is the runtime

- Status: proposed — awaiting maintainer sign-off
- Date: 2026-10-04
- Evidence: `docs/spikes/S-5/evidence/mem-decomposition.txt` (probe: `docs/spikes/S-5/mem-probe.sh`), S-3 report addendum
- Relates to: ADR 0005 (startup budgets), ADR 0001 (single-file releases), S-4 (Roslyn memory)

## Context

The S-5 baseline idles at 82.1 MB peak RSS against the guide's 100 MB
idle-TUI budget, and the budget had no decomposition or owner. The
decomposition (macOS arm64, single-file ReadyToRun, uncompressed):

- `hello` single-file R2R floor: ~70.4 MB — the .NET runtime plus the R2R
  image, before any product code;
- + Spectre.Console: ~80.4 MB; the managed heap stays negligible (~25 KB);
- GC knobs change nothing: `DOTNET_GCConserveMemory=9` ~74.8 MB,
  `DOTNET_TieredCompilation=0` ~75.1 MB, `DOTNET_gcServer=1` ~76.7 MB
  (slightly worse) — all within noise of the ~74.9 MB default.

Peer agents in this space run on Node or Python runtimes whose floors are of
the same order; ~80 MB is the runtime's price for the packaging, not waste in
the product code.

## Decision (proposed)

- **The 100 MB idle budget stands, scoped explicitly to the TUI without the
  Roslyn extension loaded** (measured range ~75–82 MB; the headroom covers the
  real TUI).
- **No GC or runtime settings are adopted for `Lunate.Coding`** — the evidence
  shows no meaningful lever; adding knobs without measured effect would be
  cargo cult.
- **Roslyn-loaded memory is a separate, lazy state** (S-4: ~0.5 GB peak on a
  47-project workspace) and gets its own budget row when T-25 lands.
- Memory levers, if ever needed, are runtime/packaging choices (trimming,
  single-file layout), not application flags.

## Alternatives considered

- **Recalibrate the budget upward (e.g. 120 MB)**: rejected for now — the
  measured range sits under the current budget with ~20 MB headroom; a
  recalibration without a real-TUI measurement would be noise-driven.
- **Adopt `DOTNET_GCConserveMemory` / server-GC switches**: rejected — no
  measured effect (the heap is ~25 KB).
- **Chase the runtime floor (trimming/AOT-like options)**: out of scope —
  ADR 0001 settled no-AOT for JIT/extension reasons.

## Consequences

- The guide's budget table carries the scoped wording and the decomposition
  note (S-5 evidence).
- T-18/T-19 re-measure the real TUI idle against this budget; if the real UI
  materially exceeds it, re-open with measurements (the S-3 re-calibration
  discipline applies).
- Re-open if a later .NET runtime changes the floor materially.

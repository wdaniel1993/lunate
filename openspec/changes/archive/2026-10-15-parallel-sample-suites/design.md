# Design

## D1 — Scope: all seven sample suites; core suites out of scope

The card names "both sample suites" (lsp-diagnostics + mcp-from-extension — the only two that existed when it was written); five more were serialized by convention afterwards (`subagents`, `ui-showcase` 2026-10-13; `code-mode` 2026-10-13; `model-router` 2026-10-14; `worktree-tasks` 2026-10-14). The maintainer's goal ("the suites run in parallel again, reliably") is class-level, so the change covers all seven sample suites. The four core suites and the two named collections serialize from different failure history (protocol probe round-trips `7d5e8e3`; ALC-collection and T-46 MCP process load) — untouched; a follow-up card can run the same protocol against them.

## D2 — Evidence protocol (the acceptance)

Per suite, in order; stop at the first configuration that holds:

1. **Full parallel** (attribute removed): local evidence = the suite binary run 5× in a row with parallel collections on, all green. CI evidence = the PR's CI run (all legs).
2. **Bounded** (`[assembly: Parallelization(MaxThreads = 2)]`): if a suite fails on a CI leg with the load-dependent signature (different failing sets across legs; timeouts/starved starts — never a deterministic product bug), drop to this and repeat the evidence.
3. **Restore `Mode = None`** with a comment in the suite's `AssemblyInfo.cs` naming the failing leg + test + date — only if bounded also fails.

Acceptance = **three consecutive green CI runs** on the final tree (each push re-runs all legs; the archive commit counts as one of the three). "Consecutive" is per run, not per leg: one red run resets the streak.

## D3 — Hazard review (why parallel is expected to hold)

- **Ports**: `FakeOpenAiEndpoint` (model-router) binds `TcpListener(IPAddress.Loopback, 0)` — ephemeral, parallel-safe by construction (a recorded deviation from the design's HttpListener wording).
- **Paths**: every suite has its own `TempDirectory` helper — per-test temp dirs; no cwd changes, no `~/.lunate` use in any of the seven (grep-verified).
- **Markers**: lsp-diagnostics markers are per-host temp paths (`TestSupport.MarkerPath(host)`), not fixed names.
- **Process-global env**: `code-mode`'s `The_child_environment_is_a_scrubbed_allow_list` sets `LUNATE_CODEMODE_SECRET` for the duration of one test. Benign under parallelism **because the mechanism under test is an allow-list scrubber**: every child in the suite gets only allow-listed variables, so the temporary variable cannot reach any other test's child, and no other test reads it in-process. Kept as-is, documented here (isolating it into a serial collection would test the same thing more slowly).
- **The known risk**: process-start starvation on the small Windows runners (the original failure). That is exactly what the D2 ladder measures; the round-trip budgets widened in `09f6988` stay.

## D4 — Non-goals

- No product code, no new packages, no spec deltas.
- Not a re-calibration of perf budgets (the hyperfine-2.x fix already landed separately in `18367ef`).
- The `lunate-dev` skill's "process-spawning suites must serialize" rule is updated **after** the outcome is known (tasks 6.1) — it currently states the old reality.

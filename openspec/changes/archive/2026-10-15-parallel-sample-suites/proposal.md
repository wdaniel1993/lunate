# Test isolation: restore parallel execution for the process-spawning sample suites

## Why

Card `t_f49da4ba` (maintainer ask, Daniel, 2026-10-12 during T-46 sign-off): "serializing is fine for now, but per-test isolation (temp dirs, unique markers, no shared state) should make these suites parallel-safe again." The serialization landed for cause — `lsp-diagnostics` (`09f6988`) and `mcp-from-extension` (T-46) both starved process startup on the Windows CI runners under parallel collections — and every sample suite created since then copied the `Parallelization(Mode = None)` convention defensively. The card enumerates "both sample suites" because it predates five of them; **seven** sample suites are serialized today.

## What Changes

- **The serialization attributes are removed** from all seven sample test suites (`subagents`, `model-router`, `lsp-diagnostics`, `code-mode`, `mcp-from-extension`, `ui-showcase`, `worktree-tasks`) — xUnit v3 default parallel collections.
- **Where full parallelism does not hold on the Windows legs, the suite is raised, not restored blindly**: `[assembly: Parallelization(MaxThreads = 2)]` (bounded) is the fallback the card names; only if that still fails does a suite keep `Mode = None` **with the reason documented in its `AssemblyInfo.cs`**. The adjustment ladder and its evidence are design D2.
- **Evidence, not assertion**: local repeated parallel runs per suite + **three consecutive green CI runs** (all legs, Windows legs being the witnesses) on the final tree — the card's acceptance.

## Impact

- Test configuration only — no product behaviour, no public API, no packages, **no spec deltas** (`skip_specs`; the mcp-carry precedent). The gate and CI already run all seven suites (they appear in every verify pass).
- Hazard review (design D3): per-test temp dirs everywhere, ephemeral TCP ports in the fake endpoint, no cwd changes; one process-global env mutation (`code-mode`, child-scrubber test) is benign by the allow-list scrubber under test — kept, documented.
- Out of scope (recorded, not touched): the four core suites (`Protocols`, `Agent`, `Extensibility`, `Ai`) and the `roslyn-workspaces` / `mcp-processes` collections serialize from *different* failure history (probe round-trips, ALC collection, T-46 process load); the same protocol could be applied later as a follow-up card.

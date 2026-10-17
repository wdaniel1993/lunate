# Hook timing

## Why

`docs/spec/extensibility.md` promises: "Per-extension load time and per-hook time are recorded; warn above a threshold." The load half landed with the `/extensions` view (per-extension load time, `(slow)` above 500 ms). The hook half was deferred from that sign-off as its own piece (Daniel, 2026-10-16): measure per-hook dispatch in `HookRunner`, surface where hooks show, same threshold discipline.

Today a slow extension handler is invisible: a `ToolCalling` handler that adds 800 ms to every tool call shows up only as "the agent feels slow". The per-handler timeout (5 s default) catches hangs, not drag.

## What changes

- `HookRunner` times every handler dispatch and aggregates per (extension, hook) for the session: call count, total, last and max milliseconds. One warning is logged the first time a dispatch crosses the slow threshold (configurable, default 500 ms — the same discipline as the load marker); later crossings are counted, not logged again.
- The aggregate is surfaced where hooks already show: a loaded extension in `/extensions` that crossed the threshold appends its slowest such hook (`hook ToolCalling 812 ms (slow)`), and the `--json` form carries a per-hook `hookTimings` array.
- Remote extensions are measured at the same point, so their recorded time includes the child-process round trip by construction.

No new packages. No wire or manifest changes.

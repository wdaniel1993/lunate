# Tasks — hook timing

- [ ] **Timing funnel.** `HookRunner` gains one timing wrapper around the per-handler call (the `CallVoidAsync`/`CallAsync` funnel): per (extension id, hook) aggregate `{calls, totalMs, lastMs, maxMs}`; timed-out and cancelled dispatches counted; snapshot exposed as `HookTimings` (public type; `PublicAPI.Unshipped.txt`).
- [ ] **Warn once.** `HookRunnerOptions.SlowHookThreshold` (default 500 ms); the first crossing per (extension, hook) per session logs one warning naming the extension, the hook and the elapsed time; later crossings counted only.
- [ ] **View.** Loaded `/extensions` segments append `hook <Hook> <ms> ms (slow)` for the slowest over-threshold hook when one crossed; `--json` gains `hookTimings` (per dispatched hook) for sessions; the register-only subcommand stays unchanged (no dispatches). Culture-invariant numbers.
- [ ] **Plumbing.** The session passes the runner's snapshot to the view call sites (interactive `/extensions` and the session-mode JSON path) the same way registered counts flow today.
- [ ] **Tests.** Unit: aggregation per pair; warn-once (repeated crossings → one warning, full count); below-threshold silent; timeout/cancellation counted; remote round trip included (scripted child); threshold option honored. View: segment suffix + JSON contract; inspect mode carries no `hookTimings`; culture pass (`de-AT`) unchanged.
- [ ] **Docs.** `docs/spec/extensibility.md` per-hook sentence now describes what landed; `docs/extensions-remote.md` notes remote timings include the round trip (one sentence).
- [ ] **Gate + review.** Full `verify.sh` (both culture passes), adversarial review, archive.

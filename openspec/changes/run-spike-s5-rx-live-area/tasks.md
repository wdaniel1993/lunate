## 1. Scaffolding

- [x] 1.1 Create `docs/spikes/S-5/` (throwaway, net10.0, outside `lunate.sln`): shared scenario harness — replayed `AgentEvent` fixture, fake console with a scripted key sequence, one resize event, spinner timer; pin all package versions
- [x] 1.2 Variant A: plain async — `Channel<T>` merge of sources, `PeriodicTimer` frame clock
- [x] 1.3 Variant B: System.Reactive — merged observables, `Sample`/`Throttle`, `TestScheduler` in tests
- [x] 1.4 Variant B+: ReactiveUI view models for status footer and approval prompt, subscribed by a render function; no view bindings

## 2. Measurements

- [x] 2.1 Startup delta and idle memory vs a baseline, single-file ReadyToRun (S-3 method); added assemblies
- [x] 2.2 Lines of code per variant, excluding tests
- [x] 2.3 Shared test list for all variants including the time-based tests (spinner frame, throttling, Ctrl+C window); each suite run 50×; flake report
- [x] 2.4 10,000-delta burst: no lost or reordered events; cancel leaves a clean state
- [x] 2.5 Readability: reviewer subagent explains each variant's flow in five sentences; hard-to-follow list
- [x] 2.6 Dependency health: versions, licences, release cadence, ReactiveUI initialisation cost (Splat)

## 3. Report and ADR

- [ ] 3.1 `docs/spikes/S-5/report.md` with check matrix, evidence files, surprises, limits
- [ ] 3.2 Draft `adr/0007-<name>.md` (proposed — awaiting maintainer sign-off) with the A/B/B+ recommendation

## 4. Close

- [ ] 4.1 `openspec validate run-spike-s5-rx-live-area --type change --strict`; commit per group
- [ ] 4.2 Maintainer sign-off on ADR-0007; archive

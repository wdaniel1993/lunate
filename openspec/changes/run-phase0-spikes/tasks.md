## 1. Scaffolding

- [x] 1.1 Create `docs/spikes/` structure (S-1..S-4 folders plus a README explaining the throwaway rules)
- [x] 1.2 Confirm spike projects are excluded from `lunate.sln` and the verify gate (the architecture test only inspects `src/`)

## 2. S-1 — loop: MAF harness vs own loop

- [x] 2.1 Build the minimal own-loop agent (IChatClient, four tools, stub stream) and the minimal MAF-harness agent (optional features off), both driven by the shared stub chat client
- [x] 2.2 Run the six checks: startup/idle memory; prompt size (under 1k tokens with everything off); event stream incl. steering and cancel; approval hooks; replay determinism; API-churn review
- [x] 2.3 Write `docs/spikes/S-1/report.md` with the check matrix and the recommendation (own loop / harness / borrow parts)
- [x] 2.4 Draft `adr/0003-loop-own-vs-maf-harness.md` for maintainer sign-off

## 3. S-2 — Git Bash (mintty) input

- [x] 3.1 Build the raw-key console spike; automate the redirected-input behaviour
- [ ] 3.2 Document and run the manual check procedure in a real Git Bash window on Windows (maintainer)
- [x] 3.3 Write `docs/spikes/S-2/report.md`; draft `adr/0004-mintty-input-support.md`

## 4. S-3 — startup baselines and budget calibration

- [x] 4.1 Dispatch the CI matrix several times; collect startup medians per OS runner; measure the dev machine
- [x] 4.2 Derive budgets (median + roughly 50% headroom, rounded); update `scripts/perf.sh` default and the CI matrix; document the methodology in `docs/spikes/S-3/report.md`
- [x] 4.3 Draft `adr/0005-budget-calibration.md`

## 5. S-4 — MSBuildWorkspace reality check

- [x] 5.1 Build the workspace spike: `MSBuildLocator` + fixture solution (console + library + tests); measure cold load and edit-to-diagnostics latency
- [x] 5.2 Repeat with one large real OSS solution (pinned by commit) and from a published single-file build; record failure modes (missing restore, broken references)
- [x] 5.3 Write `docs/spikes/S-4/report.md`; draft `adr/0006-roslyn-placement.md`

## 6. Close

- [ ] 6.1 Maintainer sign-off on each proposed ADR; finalize the change's `adr.md` manifest with the actual ADR files
- [x] 6.2 Run `openspec validate run-phase0-spikes --type change --strict`; commit per spike

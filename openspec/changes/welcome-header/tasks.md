# Tasks — welcome header

## Implementation

- [ ] 1. Header renderer (pure function of capabilities/width/state → lines) with the full block (V2 wordmark, version, model + context window, `~`-shortened workspace, C# line when detected, counts placeholder, hint line) and the single-line ASCII fallback.
- [ ] 2. Wire into `InteractiveSession.RunAsync`: print once on the first frame via the live area's scrollback path; composition keeps running; counts update line on completion.
- [ ] 3. First-run path: detect the three provider/key states; render the actionable fix form; no counts/hints block in that state.
- [ ] 4. Capability matrix: width ≥ 80 + Unicode gate, `NO_COLOR`, no-ANSI/mintty/dumb terminal; never in print/ACP/`--json`.
- [ ] 5. `startupHeader` setting + `LUNATE_STARTUP_HEADER` env override (settings precedence + validation); `/new` reprints.
- [ ] 6. `/help`: command registry (name, group, description), grouped listing (session / model / extensions / files / quit), hint line gains `/help`.
- [ ] 7. Wordmark assets: README logo pair (light/dark) + social preview 1280×640, derived from the rendered V2 mark.

## Tests

- [ ] 8. Snapshot matrix: widths {80, 120, 40} × Unicode/ASCII × color/NO_COLOR (12 cells) + first-run variant + `startupHeader: false` + `/new` reprint + print/ACP/`--json` absence.
- [ ] 9. Registry test: every dispatchable built-in command has a description (and no orphan descriptors); `/help` output groups and descriptions asserted.
- [ ] 10. Production composition test: real composition, header present, budget green.

## Gate

- [ ] 11. `scripts/verify.sh` green on the final tree; startup budget re-measured by the perf gate.
- [ ] 12. Specs updated (`tui`: welcome header added, slash-command list gains `/help`; `agent-config`: `startupHeader`); `openspec validate --all --strict` green.

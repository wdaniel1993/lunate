# Tasks — turn echo

## 1. The user echo

- [x] 1.1 `UserEchoRenderer` (Tui, pure): `> ` user-coloured prefix, plain text, two-space hanging indent, width wrapping/clipping, blank line after; unit tests (short, wrapped, long word, NO_COLOR, 16-color, empty).
- [x] 1.2 Commit the echo in `SubmitPromptAsync` on the processed text; the scrollback and the `TranscriptUserEntry` carry identical text; composition test (the real host shows the prompt before the answer).
- [x] 1.3 Refresh the end-to-end and scripted-session goldens (one echo block per prompt); steering's `» ` echo unchanged and re-asserted.

## 2. The working indicator

- [x] 2.1 `LiveAreaState` carries the run's token deltas and the tool summary; the session posts them (run start baseline, usage events).
- [x] 2.2 The activity line: spinner + label (thinking / tool + summary / working) + elapsed + `↑in ↓out` + `esc to interrupt` (only while cancellable); narrow band drops the tokens and the hint; culture-invariant formatting; unit tests per state and band.
- [x] 2.3 Composition test: a scripted run shows the thinking line with the hint, then the tool line with the tool summary.

## 3. The welcome header check

- [x] 3.1 Verify the welcome block against the theme and the breakpoints (snapshot matrix); fix only disagreements, with refreshed goldens. Verdict: one theme disagreement fixed (wordmark used ANSI cyan; now the `#5fd7d7` accent); 60/80/100 pinned in the matrix; block, first-run and single-line text goldens unchanged.

## 4. Snapshots, media, docs

- [x] 4.1 Golden snapshots for the echo and the activity line per breakpoint (40×20 / 60×30 / 80×24 / 120×40) and colour mode.
- [x] 4.2 `docs/media/tapes/features-turns.tape` (two turns; replay client) + GIF + README gallery entry.
- [ ] 4.3 `docs/guide.md`: a "Turns" paragraph (the echo, the activity line, the interrupt hint).
- [ ] 4.4 Startup budget unchanged — verified by the change gate.

# Media set

What `README.md` and the docs embed, where the frames come from, and which build produced
them. The GIFs are manual artifacts in the repository — re-render them through the capture
harness when the UI changes, never leave an asset showing a UI that no longer exists.

## Capture build

The current asset set was captured from a Release build of the repository at **`abb76346`**
(the release notes themselves show `0.1.0+abb7634673df85d08bda7dc83837cbb3f130fc32`, so the
welcome header in every GIF names this build). The scripted reasoning tape and the demo tape
replay committed fixtures through `LUNATE_REPLAY` with `LUNATE_DATE=2026-10-18` pinned — no
provider is called during a render.

## Assets

| Path | Shown by | Tape |
| --- | --- | --- |
| `demo.gif` | the README hero | [`tapes/demo.tape`](tapes/demo.tape) |
| `features/input.gif` | `README.md` — input box and autocomplete | [`tapes/features-input.tape`](tapes/features-input.tape) |
| `features/turns.gif` | `README.md` — two turns, user echo, tool block | [`tapes/features-turns.tape`](tapes/features-turns.tape) |
| `features/narrow.gif` | `README.md` — 40-column frame | [`tapes/features-narrow.tape`](tapes/features-narrow.tape) |
| `features/approval.gif` | `README.md` — the approval panel | [`tapes/features-approval.tape`](tapes/features-approval.tape) |
| `features/tools.gif` | `README.md` — tool blocks and `Ctrl+O` | [`tapes/features-tools.tape`](tapes/features-tools.tape) |
| `features/diagnostics.gif` | `README.md` — `cs_diagnostics` after an edit | [`tapes/features-diagnostics.tape`](tapes/features-diagnostics.tape) |
| `features/trust.gif` | `README.md` — the extension trust prompt | [`tapes/features-trust.tape`](tapes/features-trust.tape) |
| `features/extensions.gif` | `README.md` — `/extensions` in a session | [`tapes/features-extensions.tape`](tapes/features-extensions.tape) |
| `features/thinking.gif` | `README.md` — reasoning streaming | [`tapes/features-thinking.tape`](tapes/features-thinking.tape) |
| `features/bash.gif` | kept in the set, not embedded | [`tapes/features-bash.tape`](tapes/features-bash.tape) |
| `features/follow-up.gif` | kept in the set, not embedded | [`tapes/features-follow-up.tape`](tapes/features-follow-up.tape) |
| `first-run.gif` | not on disk yet — see [`first-run.md`](first-run.md) | recorded by hand with a real key |
| `brand/*` | `README.md` logo, social preview | not taped |

## Re-rendering

`scripts/render-media.sh` is the only way to produce the GIFs above: it regenerates every
committed tape against a Release build, keyless and deterministic. Requirements and the
re-record procedure (provider key) are documented in the script's header. The frame is always
1400×700, font size 16, padding 12, Dracula (`features-narrow` at 450×620); change it nowhere
else. Every GIF must stay under 5 MB — the README media guard rejects bigger files.

Two tapes drive their moment deliberately: `features-input` filters by text (`/cop` then
`Tab`) instead of counting rows, so it stays correct when the command table changes;
`features-narrow` waits 40 s after the approval so the turn finishes before its `/` list opens.
`features-bash` and `features-follow-up` replay an exchange whose fixture digest no longer
matches, so both show the app's own `run failed: fixture mismatch` notice — the same broken
choreography the previous capture had, not a new defect.

# Design — turn echo

## D1. The user echo block (pure renderer)

`UserEchoRenderer` renders the submitted text as one scrollback block:

- first line `> <text>`, the prefix in the theme's `user` colour, the text plain;
- continuation lines indented two spaces (the hanging indent), wrapped at the terminal width with `CellText` word wrapping;
- one blank line after the block (the turn spacing);
- `> ` in every mode (no Unicode variant — the chevron is the ASCII-safe anchor); in NO_COLOR and 16-color the prefix alone distinguishes the block.

Long single words clip at the width (the tool-block rule); paths are not shortened here — the user's words are shown as typed.

## D2. The commit point

The echo commits in `SubmitPromptAsync` on the processed text (after the input pipeline, before the run starts) — the same text as the transcript entry, so the viewer and the scrollback always agree. Steering keeps `EchoSteering`'s dim `» ` form: it is not a new turn. Replayed fixtures include the echo (it is a session-side commit, not an event), so the end-to-end goldens gain exactly one block per scripted prompt.

## D3. The working indicator

One activity line in the live area, above the input:

- **Idle/thinking**: `⠹ thinking… <elapsed>s · ↑<in> ↓<out> · esc to interrupt`.
- **Running a tool**: `<spinner> <tool name> <summary> · <elapsed>s · ↑<in> ↓<out> · esc to interrupt`.
- **Generic work** (a run with neither): `<spinner> working… <elapsed>s · ↑<in> ↓<out> · esc to interrupt`.

The token deltas are the current run's usage — the session records the usage at the run's start and the indicator shows the difference; they are culture-invariant (`1.2k`, `180`) and omitted before the first usage event. `esc to interrupt` shows only while a turn is cancellable. Narrow band (<60) keeps `spinner label elapsed` and drops the tokens and the hint.

Sources: `LiveAreaState` gains the run's deltas and the tool summary; the session posts them with the existing status updates.

## D4. The welcome header

A verification pass against the theme and the width bands, using the snapshot matrix; fix only what disagrees (no redesign). Any change lands with refreshed goldens.

## D5. Turn spacing

One blank line between blocks inside a turn (user echo, assistant text, tool blocks) and between turns — the existing commits already separate assistant text and tool blocks; the echo adds the opening block only, so no other spacing changes.

## D6. The left rail — open design question

tau and opencode anchor the conversation with a one-column left rail; pi does not. The signed-off visual spec is borderless and has no rail. Recorded as an open question for the maintainer; nothing in this change depends on it. If adopted later, it is a visual-design revision with its own sign-off.

## D7. Tape and docs

A `features-turns` tape driven by the replay client showing two turns: the prompt, the echo, the tool block, the answer, the second prompt. Guide: a short "Turns" paragraph describing the echo and the activity line.

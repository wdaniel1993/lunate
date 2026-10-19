# Proposal — turn echo

## Why

Turns do not read like a chat. The submitted message is never written to the scrollback: a turn opens with the assistant's text, so the user cannot see what they asked, and successive turns blur together (the transcript entry exists, the committed block does not). The signed-off visual spec's finished-turn snapshot opens every turn with `> Add validation …` on the tinted user colour with a two-space hanging indent — that block was never built.

The live working state is equally flat: the activity line is a bare spinner plus a name, with no elapsed time, no token deltas and no interrupt hint, while the reference agents (tau, opencode, pi) show the running action with its duration and a usable `esc to interrupt`.

## What changes

- **The user echo commits to the scrollback** as the first block of every turn: `> ` prefix in the theme's `user` colour, the wrapped continuation indented two spaces, one blank line after (the turn spacing). Steering keeps its distinct dim `» ` echo.
- **The working indicator shows the action, the elapsed time and the token deltas**, with `esc to interrupt` on the same line: `⠹ thinking… 3s · ↑1.2k ↓180 · esc to interrupt`, `⠸ edit TodoList.cs · 12s · esc to interrupt`.
- **The welcome header is checked against the theme and the width bands** (a verification pass; fixes only where the snapshot matrix disagrees).
- Snapshot tests per breakpoint and colour mode, a production composition test (the real host echoes the prompt), a VHS tape, startup budget unchanged.

## Non-goals

- The left rail that tau and opencode use to anchor the conversation: a design question outside the signed-off visual spec, recorded in the design (D6) and not implemented here.
- The approval panel: card 6.

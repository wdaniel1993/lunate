# Proposal: tool blocks — compact headers and a transcript viewer

## Why

Tool calls are the busiest thing on a coding agent's screen, and today every one of them renders
the same way: a header that says `tool read … ok`, a first-and-last excerpt of the output, and no
way to see anything the excerpt hid. That works for a glance and fails for everything else. A
`read` block echoes file content the user just asked to read; a `bash` block shows the first eight
lines of a build log and hides the error; an `edit` block makes you hunt for what changed; and
when the excerpt cuts, the hidden lines are simply gone until the session file is opened by hand.

The reference study (pi, tau) showed the shape that works: one compact line per call with the
information that identifies it, output reduced to what matters, and a visible affordance — `ctrl+o`
— that opens the full record on demand. This change brings that to lunate: per-tool compact
headers, an excerpt rule per tool, the cut affordance, mid-shortened paths, and a full-screen
transcript viewer over the session's complete record.

## What changes

- The block header gains a status glyph (`✓` / `✗`, ASCII `+` / `x`) and a per-tool compact
  summary with meta: `read` → path + line range, no content; `bash` → command + exit code +
  duration + the output's last lines; `edit`/`write` → path + `+N −M` + the diff; `cs_*` →
  severity counts + the top items; MCP/extension tools → name + short args.
- The elision marker becomes an affordance: `… +N lines · ctrl+o` (warning colour).
- Paths shorten in the middle (`~/dev/l…/Session.cs`), never wrap.
- `Ctrl+O` opens a full-screen transcript viewer over the session's full record — every tool
  output, thinking block and diff — opened at the latest entry, `q`/`Esc` returns.
- The spawning block's carried subagent result renders as one indented group (the child stays
  quiet in scrollback, per the existing contract).
- Per-card rules: snapshot tests per breakpoint and colour mode, a production composition test,
  a VHS tape driven by the replay client, startup budget unchanged.

## What it is not

- No new packages (Spectre, the existing renderers and the key pump cover it).
- No change to what the session file records; the viewer is a view over the session's own record.
- No child-run scrollback: the child transcript stays reachable through its child session; the
  spawning block shows the child's final result, now indented as one group.

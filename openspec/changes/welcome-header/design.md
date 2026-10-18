# Design — welcome header

## D1 — Render path: a pure renderer, printed through the live area's scrollback

The header is a pure function of `(capabilities, width, header state) → renderable lines` (a small record carrying wordmark, version, model, context window, workspace, C#-detected flag, counts or placeholder, first-run state, hint line). The session prints it through the existing scrollback path of the live area (`LiveArea` already renders into scrollback above the live region) immediately after `_live.Start()` and before composition begins. No new rendering pipeline, no alternate screen, no animation.

The counts line is separate from the header block: while composition runs it renders the `…` placeholder; once composition completes, the session prints **one** update line (`extensions 4 · MCP 2 · skills 7`) below — never a redraw of the header block (scrollback-safe, per the signed-off design).

## D2 — Wordmark: V2 (chosen in sign-off)

The sign-off picked **the small crescent lockup (V2)**: a small crescent built from block characters above the lowercase `lunate` name, used in the full block. The single-line ASCII fallback stays plain text (`lunate v0.1.0 …`) — no Unicode art outside the full block. The same V2 mark becomes the README logo pair (light/dark) and the social preview (1280×640); assets are produced from the rendered terminal form so README and terminal match.

## D3 — `/help` from a command registry

Introduce one small descriptor table (`name`, `group`, `description`) as the single source for: the `/help` listing, and the test that every dispatchable built-in command carries a description (a command present in the dispatch switch but missing from the table fails the test, and vice versa). The dispatch switch itself stays where it is — the table is metadata, not control flow. Groups: session, model, extensions, files, quit.

## D4 — First-run detection

The header asks the existing auth/model resolution path whether any provider/key resolves. Three states: resolved (normal header), no key file (one fix: `lunate auth login <provider>`), env var present but not resolving (name the variable). Exactly one actionable path per detected case — never a list of alternatives. The first-run form prints no counts/hints block.

## D5 — Setting and environment

`startupHeader` joins `AgentSettings` (bool, default `true`), resolved from `~/.lunate/settings.json` with the `LUNATE_STARTUP_HEADER` environment override, following the existing settings precedence and validation rules. `/new` re-evaluates nothing: the setting is session-invariant; a session started with it on reprints the header on `/new` because `/new` is a session start.

## D6 — Budget

The header render is measured by the existing startup budget gate (`scripts/perf.sh` via the verify gate) and must stay within the current budget (target ≤ 3 ms render). The render is allocation-light by construction: one pass, string building only, no reflection, no I/O.

## D7 — What this is not

Not a splash screen, not interactive, not persisted: the header writes finished lines and never repaints them (except the single counts update line). Machine modes (print, ACP, `--json`) never construct it.

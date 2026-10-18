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

## D8 — deviations

Recorded while implementing the signed-off design (2026-10-18) where the code reality differs.

1. **No `lunate auth login <provider>` exists.** `Cli` has no `auth` verb (an unknown verb exits 2 without a message), so printing that line would send the first-run user into a silent failure. The first-run form names the fix the shipped code can honour: no key → `add the 'openai' key to ~/.lunate/auth.json`; an environment variable present but not resolving → `set OPENAI_API_KEY to a valid key`. Shape and rule (exactly one actionable fix, no list of alternatives) are unchanged. An `auth login` command can replace the string later without touching the header.
2. **ASCII punctuation in the single-line form.** The signed-off literal (`lunate v0.1.0 (C# · Roslyn) — /help for commands`) is what a *narrow but Unicode* terminal shows. A non-Unicode terminal (`TERM=dumb`, `LUNATE_ASCII`) gets `lunate v0.1.0 (C# - Roslyn) - /help for commands`: the session's output there must stay pure ASCII (the existing ASCII discipline, enforced over the session scrollback).
3. **The `/help` group `files` has no commands today.** The group vocabulary is implemented in that order (session, model, extensions, files, quit) and a group with no commands is not printed, so no empty heading appears until a file command lands.
4. **V2 renders as one mark line.** The signed-off block's first line is the single crescent `Ϲ` (U+03F9) above the name; D2's "block characters" prose is read as that lockup, not as multi-glyph art, because the sign-off pins the block line by line.
5. **D6's gate.** `scripts/perf.sh` measures the published binary's `--version`, which never reaches the interactive render. The header's budget is guarded instead by a `Category=Perf` test in the gate's perf step (first frame within 1 s, ~20× the observed cost), so a render that starts doing I/O fails the gate.
6. **The first frame names the configured model id.** The header prints before the extension registry resolves an extension-registered (or virtual) model, so that id carries no context window on the first frame; a catalog model shows the full `id · N ctx`. `/new` reprints the block after resolution, with the session's current model and exact counts (so `/new` shows no placeholder and needs no update line).
7. **`/help` prints into scrollback.** A live-area notice is one clipped line, so the grouped listing (a line per group and command) is written into scrollback through the same path as the header.
8. **Regenerated interaction goldens.** `end-to-end-scrollback.txt` gains both header blocks by design; `end-to-end-session-picker-frame.txt` changes because the header write clears the live frame before the picker opens (the visible picker lines are unchanged).


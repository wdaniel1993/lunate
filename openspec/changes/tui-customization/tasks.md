# Tasks — tui-customization

Four parts, in this order. Each part is a separate PR and is useful without the ones after it; the
change archives only when the last part lands.

## 1. Keymap

- [ ] 1.1 Load `~/.lunate/keymap.json`, overlaying `keys.json` by binding id; `$LUNATE_KEYMAP` and
      `--keymap <path>` select the file.
- [ ] 1.2 Refuse an unknown binding id with the id and up to three nearest catalogue ids; refuse a
      user binding that invents an id; allow an empty `keys` array to disable a binding.
- [ ] 1.3 Resolve conflicts: user file beats the catalogue with one startup line naming both ids; two
      user bindings in conflict fail the load.
- [ ] 1.4 Tests: overlay, unknown id, invented id, disable, catalogue conflict, user-user conflict,
      and one test that a rebound key reaches its intent through the real key reader.

## 2. Themes

- [ ] 2.1 Token set (`accent`, `muted`, `error`, `success`, `warning`, `code`, `diff.add`,
      `diff.remove`, `prompt`, `notice`) and the three built-ins; route every existing colour use
      through a token.
- [ ] 2.2 Parse `~/.lunate/theme.json` (ANSI name, 256 index, hex) and fail on an unknown token with
      the valid list.
- [ ] 2.3 Downgrade by capability: hex to 256, to 16, to none; `NO_COLOR` wins over any theme.
- [ ] 2.4 Tests: each colour form, each downgrade, unknown token, `NO_COLOR` precedence, and a golden
      of one transcript under each built-in theme.

## 3. Full screen

- [ ] 3.1 `--fullscreen` and `tui.fullscreen: true`; enter after the first frame, leave on every exit
      path including an exception.
- [ ] 3.2 Tests: enter/leave on normal quit, on `Ctrl+C`, and on a thrown exception; inline remains the
      default when neither flag nor setting is present.

## 4. Mermaid diagrams

- [ ] 4.1 Parser for `flowchart`/`graph` (`TD`/`TB`/`LR`, labelled nodes, edges with and without
      labels) and `sequenceDiagram` (participants, `->`, `-->`, `->>`, `Note`); everything else
      classified as unsupported.
- [ ] 4.2 Flowchart layout and renderer with the box-drawing set; sequence renderer with lifelines and
      message labels.
- [ ] 4.3 Fallback rules: unsupported type, parse failure and width overflow each render the source
      with the exact one-line note from the design; never a partial diagram.
- [ ] 4.4 ASCII mode swaps the glyph set without changing geometry.
- [ ] 4.5 Route a ```mermaid fence from the Markdown renderer to the diagram renderer; unsupported
      constructs in ordinary fences keep their current behaviour.
- [ ] 4.6 Tests: one golden per supported diagram type (TD, LR, sequence), the three fallbacks, ASCII
      mode, and a document mixing a diagram with code fences and prose.

## 5. Docs and gate

- [ ] 5.1 `docs/` page for the keymap, the theme tokens and the full-screen option; one example file
      each under `docs/` (not in the repo root).
- [ ] 5.2 `scripts/verify.sh` unchanged; the suites carry the new tests.
- [ ] 5.3 Archive the change into `openspec/specs/tui/spec.md` in the last part's PR.

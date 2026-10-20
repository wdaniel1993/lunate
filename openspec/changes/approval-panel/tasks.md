# Tasks — approval panel

## 1. The model and the detail

- [x] 1.1 `ApprovalPromptModel` gains `Detail` (prepared lines) and `WorkspaceRoot`; the session builds them from the `ApprovalRequest`: bash command (wrapped, ≤6 lines + marker), edit/write path + `DiffRenderer` diff, read path/range, other tools via `ToolArgsSummary`, unknown tools' raw args; unit tests per tool.
- [x] 1.2 The workspace root line appears only when it differs from the session's working directory; test.

## 2. The panel renderer

- [x] 2.1 Headline (`⚠ <tool>  <action>`, ASCII `!`), detail block (muted, indented, bounded, clipped), question (`Allow <tool>?  [y]es  [n]o  [a]lways this session`, key letters accented); markup escaped; unit tests per part.
- [ ] 2.2 The live-area integration: the panel renders above the input in the live frame; the frame-cap trims the detail first, then the headline, never the question (height tests at 20/24 rows).
- [ ] 2.3 The narrow band (<60): the question on its own line; every choice visible at 40 columns (goldens).

## 3. Composition, goldens, media, docs

- [ ] 3.1 Composition test on the real host at 40 columns: an edit approval shows the diff and all three choices; `a` decides and the always-memory applies.
- [ ] 3.2 Goldens per band (40/60/80/120) × colour mode for bash/edit/read/unknown/subagent.
- [ ] 3.3 `docs/media/tapes/features-approval.tape` (a turn triggering an edit approval; the panel with the diff; `a`); GIF + README gallery.
- [ ] 3.4 `docs/guide.md`: the approvals paragraph describes the panel (headline, detail, choices, narrow band).
- [ ] 3.5 Startup budget unchanged — verified by the change gate.

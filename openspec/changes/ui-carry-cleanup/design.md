# Design — ui-carry-cleanup

## D1 — Block-fallback determinism: two tests at the two real seams

The claim (TuiUserInteraction's contract): "the text is identical under the NO_COLOR and ASCII fallbacks — only colour attributes are dropped". Two seams prove it, because no single test covers both halves:

- **The strip (Tui.Tests, `LiveAreaTests`):** `LiveArea.WriteScrollback` renders through the production scrollback console, whose colour system comes from `TerminalCapabilities.Color` (the `AnsiConsole.Create` factory). Test: one area with `TrueColor` capabilities writes a styled `Markup` and its captured console writes contain colour escape sequences; another with `NoColors` writes the same markup and carries **no** escape sequences. The contrast matters — a one-sided "no ANSI" assertion would pass vacuously if the capture path never emitted ANSI at all.
- **The text (Coding.Tests, `TuiInteractionTests`):** the existing extension-block scenario (registered renderer → styled block above the live area) runs a second time with `environment` returning `NO_COLOR=1` and `LUNATE_ASCII=1` (the host's env seam feeds `TerminalCapabilities.Detect`). Assertions: the block's visible text appears and is **identical** to the default run's, and the whole scrollback text carries **no non-ASCII characters** (the block renderer adds no glyphs — the test guards that a future glyph addition fails under the ASCII config).

The T-33 pattern (capabilities record + env-seam detection + renderer/console consuming them) is followed; no new seams are added.

## D2 — The ACP block cell: reword to no-op (pinned choice)

The spec scenario said the machine modes "skip it (ACP logging it)". Reality: `AcpMode` never resolves the renderer registry; the `AcpUserInteraction.RenderExtension` implementation (which would log "not sent") is unreachable. Making it real means plumbing a renderer-match query through the ACP result pump (`Lunate.Protocols` — below Coding/Extensibility in the layering), which is not a tiny change and nothing today depends on the log line. Pin: **reword** — the scenario drops "(ACP logging it)"; the seam implementation stays as the contract's implementation, with no claim resting on it being invoked. (The docs matrix cell is already honest: ACP = "no".)

## D3 — NoticeLevel: document the uniform presentation (pinned choice)

`NoticeLevel` (Info/Warning/Error) is carried by every `UserNotice`; no implementation reads it — print/JSON write `lunate: <message>`, ACP logs the message, the TUI shows the message in the live area. The enum's own doc already says levels "may be presented differently in the future". Pin: **document** — the spec states the uniform presentation and the deferral explicitly, so the current shape is intentional rather than accidental, and the `lunate:` stderr format stays pinned for scripts. (Using the level would mean format changes in print/JSON or live-area styling — real behaviour churn for no current need; a later card can differentiate deliberately.)

## D4 — Delta, non-goals

- Delta: **ui-interaction** MODIFIED ("User interaction across modes"): the level sentence, the plain-text block sentence, the scenario reword, one new scenario ("The block text survives the fallbacks"). Original scenarios preserved verbatim otherwise.
- Non-goals: any behaviour change (no format, styling or ACP changes); the dead-seam question for `AcpUserInteraction.RenderExtension` (kept, documented by the reword); `NoticeLevel` differentiation (deferred deliberately).
- No ADR, no packages. No sign-off: the card (`t_08504f3c`) explicitly pre-authorizes the pins ("test/behaviour/word — whichever the draft pins").

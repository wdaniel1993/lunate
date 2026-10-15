# Resolve the T-40 review carry: block-fallback tests, the ACP block cell, NoticeLevel

## Why

Three non-blocking observations from the T-40 review, all about claims that were never pinned (card `t_08504f3c`): the styled-block NO_COLOR/ASCII determinism was claimed but untested; the spec's extension-block scenario claimed "ACP logging it" while nothing in ACP ever resolves a renderer (the `AcpUserInteraction.RenderExtension` seam implementation exists but is unreachable); and `NoticeLevel` is carried by every notice but read by no implementation (all modes render levels uniformly).

## What Changes

- **Block-fallback tests (the claim gets pinned).** Two tests: at the `LiveArea` scrollback seam (Tui.Tests), a styled write under `NoColors` carries no colour escape sequences while the same write under `TrueColor` does (the contrast proves the strip is live); at the session level (Coding.Tests), the existing extension-block scenario runs under `NO_COLOR` + `LUNATE_ASCII` and asserts the visible text is byte-identical to the default run and carries no non-ASCII glyphs.
- **The ACP block cell becomes honest (word).** The spec scenario drops "(ACP logging it)": extension blocks are skipped in the machine modes, full stop. The `AcpUserInteraction.RenderExtension` implementation stays as the seam's contract implementation, but no claim depends on it being invoked. (Making it real would need a renderer query plumbed through the ACP result pump in `Lunate.Protocols` — not a tiny change, and nothing today depends on the log line.)
- **`NoticeLevel` gets its pinned behaviour (word).** The spec states it: notices carry a level; every mode currently presents levels uniformly — print/JSON write one `lunate: <message>` line per notice, ACP logs the message, the live area shows the message — and level-specific presentation is deliberately deferred. No format changes; the existing `lunate:` stderr shape stays pinned.

## Impact

- Spec delta: **ui-interaction** (MODIFIED "User interaction across modes": the uniform-level sentence, the plain-text block sentence, the scenario reword + one new scenario).
- Two new tests; one doc comment left as-is. **No behaviour change anywhere** — no product code changes expected (if a test proves otherwise, the fix lands in this change).
- No new packages; no ADR (the card pins tests/words).

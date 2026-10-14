# Tasks — sample-ui-showcase

## 1. Sample (`samples/extensions/ui-showcase`)

- [x] 1.1 `UiShowcaseExtension`: manifest + factory registering the read-only `uikit__bars` tool (values → compact JSON result), the `uikit__bars` renderer (accent bars + dim values; empty for garbage; 40-glyph cap) and the usage-bar widget (accent < 80%, warning ≥ 80%, `ctx n/a` for unknown window; invariant formatting).
- [x] 1.2 README with the capability table (what the sample proves; the degradation story per mode).
- [x] 1.3 Test project scaffold (kit reference, AssemblyInfo parallelization, TempDirectory helper) + solution entries + gate layering edge.

## 2. Sample tests (kit, real loader)

- [x] 2.1 Registration through the real loader (renderer, widget, tool present).
- [x] 2.2 Renderer: good result → expected segment texts/styles; garbage + empty → empty list; cap holds.
- [x] 2.3 Widget: 25% → accent bar; ≥ 80% → warning; window 0 → `ctx n/a`.
- [x] 2.4 The sample's segments round-trip through `System.Text.Json` (AIJsonUtilities options).

## 3. Mode proofs (`tests/Lunate.Coding.Tests`)

- [x] 3.1 ProjectReference + copy step for the sample (D4); install helper.
- [x] 3.2 TUI: scripted turn calls `uikit__bars`; bars in the scrollback, widget segment in the footer.
- [x] 3.3 print: raw text only, no glyphs/widget, exit 0.
- [x] 3.4 JSON: stream pure vs a no-extension run (same event kinds, no UI artifacts); tool-result event carries the raw text.
- [x] 3.5 ACP: runs with the sample loaded; no UI artifacts on stdout.

## 4. Verify, review, archive

- [x] 4.1 `scripts/verify.sh` green (both cultures); `openspec validate --all --strict` green; csharpier/style/analyzers/markdownlint clean.
- [x] 4.2 Adversarial review; fix findings; focused re-review.
- [x] 4.3 Archive; push; PR.

# Tasks — ui-carry-cleanup

## 1. Block-fallback tests

- [ ] 1.1 Tui.Tests (`LiveAreaTests`): a styled scrollback write under `TrueColor` carries colour escape sequences; the same write under `NoColors` carries none (contrast assertion — the strip is live).
- [ ] 1.2 Coding.Tests (`TuiInteractionTests`): the extension-block scenario under `NO_COLOR` + `LUNATE_ASCII` (env seam) — visible text identical to the default run, no non-ASCII characters anywhere in the scrollback text.

## 2. Spec delta

- [ ] 2.1 `openspec/changes/ui-carry-cleanup/specs/ui-interaction/spec.md`: the uniform-level sentence, the plain-text block sentence, the scenario reword (drop "(ACP logging it)"), the new fallback scenario; original scenarios verbatim.
- [ ] 2.2 `openspec validate --all --strict` clean.

## 3. Verify, review, archive

- [ ] 3.1 `scripts/verify.sh` green (both cultures); csharpier/style/analyzers/markdownlint clean.
- [ ] 3.2 Adversarial review (small diff); fix findings.
- [ ] 3.3 Archive; push; PR.

# Tasks — extensions-view

## 1. Draft

- [x] 1.1 Proposal + design + tasks + spec deltas; `openspec validate --all --strict`; draft commit.

## 2. Apply

- [ ] 2.1 `ExtensionSession`: `ExtensionLoadResult` + `ExtensionLoadState`, per-descriptor timing at compose (D1).
- [ ] 2.2 `ExtensionsView` formatter (segments + JSON shape) per D2/D5.
- [ ] 2.3 `/extensions` interactive command (dispatch case + notice) per D2.
- [ ] 2.4 `lunate extensions [--json]` subcommand + `Cli.cs` routing per D3.
- [ ] 2.5 Tests: session E2E (`/extensions` with loaded + denied + empty), CLI text/JSON/empty/never-prompt, formatter unit tests; `Cli.cs` help text.
- [ ] 2.6 Touched suites green (Coding; Protocols/Extensibility if touched).

## 3. Gate

- [ ] 3.1 `scripts/verify.sh` green (both cultures).

## 4. Review

- [ ] 4.1 Adversarial review; fix findings; focused re-review.

## 5. Close

- [ ] 5.1 Archive (`openspec archive extensions-view --yes`); tick tasks.
- [ ] 5.2 PR + CI green + closing comment.

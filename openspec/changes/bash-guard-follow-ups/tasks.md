## 1. Container state-root mounts are high-risk

- [x] 1.1 Add host-path-based `HighRiskCallTests` for Docker `run -v` and Podman `run --mount type=bind,source=…`, including configured `LUNATE_HOME` expansion, a source below the state root, and a normal mount outside it; construct paths with `Path.Combine` so the tests are platform-neutral.
- [ ] 1.2 Extend the existing high-risk call classifier so the specified Docker/Podman state-root mounts always require approval, without changing the guard's claim about daemon containment or adding a public API.
- [ ] 1.3 Build `Lunate.Coding` and run the filtered `Lunate.Coding.Tests` cases for `HighRiskCallTests`; confirm the allowlist/remembered-approval protections still apply.

## 2. Warn about pre-existing external hardlinks

- [ ] 2.1 Add deterministic `Lunate.Coding.Tests` for an external hardlink (warning names an in-root path once), multiple links wholly inside the state root (no warning), a missing state root (no creation), and a partial scan failure (startup continues); use a temporary state root and host-native paths.
- [ ] 2.2 Implement the one-time startup scan of the existing state root, comparing filesystem link counts with links found inside it, without following directory symlinks or changing files; route the warning without writing to ACP stdout.
- [ ] 2.3 Build `Lunate.Coding` and run the filtered startup hardlink tests, including only platform-supported hardlink fixture cases.

## 3. Add the explicit guard opt-out

- [ ] 3.1 Extend `SettingsStoreTests` to pin `bashGuard` defaulting to `true`, accepting `false`, and rejecting a non-boolean value; verify the setting does not add a public .NET API member.
- [ ] 3.2 Wire `bashGuard: false` to skip wrapping and probing, keep default behavior guarded, and make the disabled condition explicit in the bash tool result and the welcome header.
- [ ] 3.3 Build `Lunate.Coding` and run filtered settings, BashTool, and welcome-header tests for the disabled/default paths; verify the welcome-header snapshot change does not alter `tests/fixtures/sessions/`.

## 4. Share the active mechanism in `/context` and the header

- [ ] 4.1 Add fixture-backed tests for each normalized mechanism (`sandbox-exec`, `bwrap`, `landlock`, `none`) and probe outcome (success, failure, not run), asserting `/context` and the welcome header carry the same status and opt-out reports `none`/`not run`; do not call live container or sandbox services.
- [ ] 4.2 Resolve the guard once before the welcome header, share the resulting status with `BashTool`, `/context`, and the header, and preserve the existing bounded probe and visible failure notice.
- [ ] 4.3 Build the touched `Lunate.Coding` and `Lunate.Tui` projects, then run only the filtered `Lunate.Coding.Tests` and `Lunate.Tui.Tests` classes that cover guard status, context text, and welcome-header rendering.
- [ ] 4.4 Run `openspec validate bash-guard-follow-ups --type change --strict`, the whole-tree CSharpier check, and the required narrow build/test gates before the implementation change is ready for review; leave the session-file fixtures unchanged.

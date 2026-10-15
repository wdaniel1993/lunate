# Tasks

## 1. Draft + sign-off

- [x] 1.1 Draft proposal/design/tasks/spec delta; `openspec validate --all --strict` green.
- [x] 1.2 Sign-off card on the lunate board (needs_input): wire `ModelChanged` at the session model switch (recommended) vs document as unwired; summary in chat. Apply only after approval.

## 2. Implementation

- [x] 2.1 `SwitchToModel`: after the state change, resolve the active runner (`_options.Hooks ?? _extensions?.HookRunner`) and await `RunModelChangedAsync(new ModelChangedPayload(string.Empty, model.Id), ct)`; thread `ValueTask` + `ct` through `SwitchModel` → `ConfirmPicker` → `HandlePickerKey` → the `HandleKeyAsync` call site and the `/model` command case.
- [x] 2.2 Tests (production path, `tests/Lunate.Coding.Tests`): `/model <id>` switch emits `ModelChanged` with the new id + empty run id to a registered handler; the picker-confirm path does the same (or a focused test through `HandleKeyAsync`); no emission for an unknown id; without a runner the switch still completes and records the session model-change entry.
- [x] 2.3 Confirm no other surface changes the active model (grep for `_modelId =` / `_model =` assignments outside `SwitchToModel`); if one exists, decide and record it.

## 3. Spec + docs

- [x] 3.1 MODIFIED "Hook wiring" delta: wired list += `InputReceived`/`ToolsChanged`/`ModelChanged`; retire the unwired sentence; six existing scenarios verbatim + one new `ModelChanged` scenario.
- [x] 3.2 Spot-check every catalogue hook has a producer (grep per hook) before asserting "every catalogue hook SHALL have a producer".
- [x] 3.3 `docs/spec/extensibility.md`: no edit expected — confirm the catalogue table stays accurate.

## 4. Gate

- [ ] 4.1 `scripts/verify.sh` green (both cultures); csharpier/style/analyzers/markdownlint clean.
- [ ] 4.2 Adversarial review; fix findings.
- [ ] 4.3 Archive; push; PR.

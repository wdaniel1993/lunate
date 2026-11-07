## Why

The rc.3 review found that only the credential value should be masked in the setup wizard, `/auth` and `/login` — but every typed value there went through the one masked prompt (`ISecretPrompt.ReadAsync`): provider id, endpoint URL, model id, profile name, region and routing descriptions all rendered as asterisks, so a user configuring a custom endpoint could not see what they typed. rc.3 finding 1.

## What Changes

- `ISecretPrompt` gains a visible read (`ReadTextAsync`, defaulting to `ReadAsync`) so scripted test prompts keep their one answer queue; the live TUI prompt implements it and renders what is typed.
- `TuiSecretPrompt` distinguishes a masked read from a visible read. The masked read behaves as before: one asterisk per character, pasted text stripped of all whitespace. The visible read echoes the typed characters, keeps spaces in pasted text, and strips only line breaks; Enter and Esc behave the same in both.
- `SecretPromptState` carries a `Masked` flag that drives rendering; the masked prompt still never puts the credential plaintext into the live state.
- The wizard's typed credential ("API key for …") reads through the masked prompt; every other wizard typed value (custom provider id, endpoint URL, API family, credential placement, region, models URL, model id, chat/fast/decision profile names, routing descriptions) reads through the visible prompt.
- `/auth`'s custom row reads id, base URL and model id visibly; the key stays masked. `/login` keeps the masked read and saved key prompts (`env:` reference branches) keep their masked or non-prompting behaviour.
- An audit test walks the wizard's custom path, the credential-reference branch, `/auth` custom and preset, and `/login` through a recording prompt and asserts that exactly the prompts whose label starts with "API key" were masked.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `setup-wizard`: the full walk masks only the credential value; every other typed value echoes visibly through a visible prompt.
- `tui`: the auth picker's custom row echoes id, base URL and model id and masks only the key; `/login` keeps the masked prompt for the credential.

## Impact

- Affected code: `src/Lunate.Coding/ISecretPrompt.cs`, `src/Lunate.Coding/TuiSecretPrompt.cs`, `src/Lunate.Coding/InteractiveSession.SetupWizard*.cs`, `src/Lunate.Coding/InteractiveSession.AuthPicker.cs`, `src/Lunate.Tui/LiveAreaState.cs` (`SecretPromptState.Masked`), plain-text fallback `ConsoleSecretPrompt` (inherits the visible default).
- Tests: new `SecretMaskingAuditTests` in `tests/Lunate.Coding.Tests`; updates to the secret-prompt and auth-containment suites.
- No new NuGet packages. No session-format change (no golden files). No public API change (`ISecretPrompt` and `SecretPromptState` are internal).

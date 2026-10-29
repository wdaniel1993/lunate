# Tasks — auth-onboarding

## 1. The store grows a CLI

- [x] 1.1 `lunate auth list` prints one line per selectable provider: preset id, whether the credential
      comes from the environment or the file, and no value. Exits 0 even when nothing is stored.
- [x] 1.2 `lunate auth login <provider>` reads the key from a masked prompt when stdin is a terminal;
      `--key-stdin` reads it as a single line from the pipe otherwise. Neither path echoes the value.
- [x] 1.3 `lunate auth login` without a provider lists the presets on a TTY, or exits 2 with the list on
      stderr when non-interactive — never hangs waiting for a key nobody can type.
- [x] 1.4 `lunate auth logout <provider>` removes the entry, states when the environment variable is still
      set (naming it, never its value), and exits non-zero when nothing was stored.
- [x] 1.5 `lunate auth status` reports the resolved default provider and whether a credential exists,
      exit 0/1, so a bootstrap script can branch on it.
- [x] 1.6 A key passed through a pipe appears in no log, error, notice or process argument list.

## 2. Custom OpenAI-compatible endpoint

- [x] 2.1 `lunate auth login --custom` (and the picker's last row) asks for id, base URL, model id and key.
- [x] 2.2 The entry is stored as a settings provider (base URL, model, catalog name) that points at an
      auth-store key name — one place a secret lives, no second store.
- [x] 2.3 Validation at save time: `https`, or `http` only for loopback hosts; a missing model id is
      refused with the reason. The refused input is never echoed.
- [x] 2.4 A stored custom provider resolves through the same `ChatClientFactory` path as a preset, with a
      test that builds the client from it (no network).

## 3. `/auth` in the TUI

- [x] 3.1 `/auth` opens a picker in the live area: the twelve presets in file order, then "Custom
      OpenAI-compatible endpoint…", Esc cancels, Enter selects, arrows/j-k move.
- [x] 3.2 Selecting a preset opens the existing masked prompt and saves through `AuthStore`; `/login
      <provider>` remains an alias that skips the picker.
- [x] 3.3 After a successful save the notice names the provider and points at `/model`; it never contains
      the key.
- [x] 3.4 An optional verification asks before it runs, performs one minimal request, and reports
      success or the failure without discarding the saved key.
- [x] 3.5 While the picker is open, ordinary input is inert and a running turn refuses the command, as
      `/login` does today.

## 4. First run

- [x] 4.1 Starting the TUI with no usable credential and a TTY opens the picker once; Esc dismisses it.
- [x] 4.2 Print mode, `--yolo`-style non-interactive runs and piped stdin keep the explicit error and never
      prompt.
- [x] 4.3 The empty-session banner names `/auth` as the next step when nothing is configured.

## 5. Documentation and proof

- [x] 5.1 `docs/` gains a "First run" page: `lunate auth login`, the picker, the custom endpoint, and the
      environment-variable override stated once.
- [x] 5.2 `README.md` install section links that page instead of the bare auth.json sentence.
- [ ] 5.3 Tests: CLI subcommands × (TTY, pipe, missing provider, unknown provider), picker navigation,
      first-run trigger and its non-interactive absence, custom-endpoint validation, and one test that the
      key never reaches a notice/history/session file on each path.
- [ ] 5.4 `scripts/verify.sh` unchanged; the suites carry the new tests.
- [ ] 5.5 A recording for the media set: fresh install → `/auth` → picker → key → `/model` → first prompt.
- [ ] 5.6 Archive the change into `openspec/specs/{agent-config,tui}/spec.md` in the same PR's last commit.

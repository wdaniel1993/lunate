# Auth onboarding

## Problem

The first five minutes are the weakest part of the product. A fresh install has no credential, and the
only ways in are a documented environment variable or a hand-edited `~/.lunate/auth.json`; `/login
<provider>` exists but you have to be inside the TUI and already know the preset id. The error a new
user actually sees — "Add a key named 'openai' to auth.json or set the matching environment variable"
— names a file they have never seen.

Three concrete gaps:

1. **Nothing works before the TUI.** A user who wants to set credentials up front, or script it, has no
   command. `lunate auth` does not exist; the error message is documentation in disguise.
2. **Discovery.** Twelve presets are embedded (`openai`, `anthropic`, `openrouter`, `deepseek`,
   `moonshotai`, `zai`, `opencode`, `opencode-go`, `groq`, `togetherai`, `xai`, `google`), but nothing
   lists them where a user starts. `/login` with no argument reports an error instead of offering the
   list.
3. **Local models.** A user running Ollama, vLLM or LM Studio has no preset and no way to add one
   without hand-writing config. For that user the tool is simply not usable out of the box.

## Change

One change, three surfaces over one store:

- **`lunate auth`** (`login`, `list`, `logout`, `status`) for the CLI, including `--key-stdin` so a
  script or a remote setup can feed a key without a TTY. `list` shows where each credential comes
  from (file or environment) and never a value. `status` is a machine-readable yes/no for scripts.
- **`/auth`** in the TUI: a picker over the presets plus "Custom OpenAI-compatible endpoint…", then
  the existing masked prompt, then an optional single-request verification. `/login <provider>` keeps
  working as an alias — the docs, the media and muscle memory all say `/login`, and renaming a command
  to make a screenshot prettier is not a reason to break it.
- **First run**: when the TUI starts with no usable credential and a TTY, the picker opens in the live
  area once; Esc dismisses it and leaves the normal path (a run reports the missing credential). Print
  mode and non-interactive runs never prompt — they keep today's explicit error.

The custom endpoint is the piece that makes the tool usable offline: id, base URL, model id and key,
saved as a settings provider entry that points at an auth-store key name, so there is still exactly one
place a secret lives.

## Decisions

- **Alias, never rename.** `/login` stays; `/auth` is the discoverable front door.
- **Verify is opt-in and never destructive.** A failed verification reports and keeps the stored key.
  The network is not a precondition for saving, because air-gapped and metered setups must work.
- **The picker is the only new TUI surface.** It uses the same key pump and the same masked prompt the
  login path already uses; no second input mechanism.
- **No new packages, no second secret store.** `AuthStore` (schema 1, `0600`) stays the only store.
- **Custom endpoints are validated at save time, not at load time only**: scheme must be `https`, or
  `http` for loopback hosts, and a missing model id is refused with the reason.

## Out of scope

- OAuth/browser flows (Anthropic, OpenAI and Google all offer them; each is its own change).
- Provider model catalogues and pricing display.
- Migrating existing `auth.json` files — schema 1 is unchanged by this work.

## Notes

Planned for 0.2, confirmed by the maintainer on 2026-10-27. The command name `/auth` and the alias
decision are the maintainer's call recorded here rather than discovered in review.

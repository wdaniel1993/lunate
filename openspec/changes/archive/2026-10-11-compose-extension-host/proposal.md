# Proposal: compose the extension host in the CLI

## Why

The extension host exists end to end — contract, loader, trust store, hook runner, renderers, widgets, MCP-from-extensions — but nothing in production composes it. `ExtensionLoader` is constructed only in `Lunate.Extensibility.Testing`, and the CLI passes null hook runners and registries into every session. Consequence: an installed extension has **no production effect**; the whole phase-6 sample story (T-45–T-52) is undemonstrable until this lands. Found by the T-40 review; pre-existing since T-36/T-39.

## What changes

- New `ExtensionSession` (Coding, internal): the composed host. Starts per session: discovers manifests, loads each extension with per-extension failure isolation, exposes the hook adapter, renderers, widgets, MCP servers and model-provider declarations, and stops cleanly.
- Session-start composition, never at CLI startup: interactive, print and ACP sessions compose the host as they start (the host's first use); `--version`, `--help`, `--discover` and flag parsing never read the extension store, so the startup budget is untouched.
- Trust prompts in the CLI: interactive sessions ask on the console (one-time per repository, as specced); print and ACP sessions refuse project extensions with a logged, actionable message (approve interactively first).
- Wiring: hook runner into the harness seam of all three modes (`AgentHookAdapter`); tool-result renderers and status widgets into the interactive session (the T-40 surfaces); extension MCP servers listed once at session start through the existing `ExtensionMcpServers` mapping.
- Failures never abort a session: discovery errors and load failures (including denied trust) are logged through a `lunate:`-prefixed host log and the session continues without that extension.

## Done when

An extension installed under `~/.lunate/extensions/` genuinely runs in a real session — its hook observes a tool call, its renderer draws a block in the TUI, its widget shows in the footer — proven by end-to-end tests with a fixture store, and `lunate --version` provably never touches the store (poisoned-store test + unchanged perf gate). Draft-first: architecture sign-off before apply.

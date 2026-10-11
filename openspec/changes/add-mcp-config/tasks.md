# Tasks: MCP config, prefixing, approvals and TUI integration

## 1. Config parsing (Protocols)

- [ ] 1.1 `McpConfig.Load(path)` — parse `mcpServers` (command/args/env/cwd/callTimeoutSeconds), collect per-entry errors (name + field) and file-level errors; unknown fields ignored (TDD: valid file, malformed entry, malformed file, missing file = zero servers + no error, unknown fields)
- [ ] 1.2 No startup reads: nothing in `Cli`/`Program` touches the path
- [ ] 1.3 Project scope: `.lunate/mcp.json` from the worktree, merged by server name (project wins); trust-gated — one-time per repository, re-prompted on content change, non-interactive refusal logged (tests: prompt-once-then-remember; changed file re-prompts; denial refuses and the global servers still work; project overrides a same-named global server)

## 2. Harmonized composition (Coding)

- [ ] 2.1 Generalize `ExtensionMcpServers` into a shared `McpToolSet` (options + log → hosts; list once; per-server status: not started / started + tool count / failed + reason; dispose); extension mapping delegates to it — existing extension-MCP tests stay green unchanged
- [ ] 2.2 Config servers composed at session start in interactive, print and ACP (path seam `McpConfigPath`; production default `~/.lunate/mcp.json`); failures → `Warn` notices through `IUserInteraction` (live area / stderr / host log)
- [ ] 2.3 Tools listed once before the first turn and added to the registry (all three modes); names `server__tool`, namespace = key, exposure Direct

## 3. TUI

- [ ] 3.1 `/mcp` command: one dim line per server with status; `no servers configured` when empty; registered in `Commands.BuiltIn` (alphabetical) + dispatch case + completion free
- [ ] 3.2 Tool calls render with their `server__tool` names (asserted in the e2e)

## 4. Approvals proof

- [ ] 4.1 Print e2e with the real policy: default `ask` refuses an MCP call with an approval error result; `--yolo` executes the same call

## 5. `--no-extensions`

- [ ] 5.1 Flag parsed in every mode; skips the extension host composition (hooks/renderers/widgets/extension MCP absent); configured `mcp.json` servers unaffected; `--version`/`--help` unaffected
- [ ] 5.2 Tests: flag skips the store read; configured server still works

## 6. End to end (the done-when)

- [ ] 6.1 Interactive e2e: temp `mcp.json` → real `TestMcpServer` → session lists its tools, a scripted call round-trips and renders, `/mcp` shows `started, N tools`
- [ ] 6.2 Poisoned-file test: a config whose server would write a marker; `--version`/`--help`/`--discover` never read it (marker absent), a session with the same config does
- [ ] 6.3 Broken server e2e: notice names it, session continues, other tools normal

## 7. Gate + record

- [ ] 7.1 `bash scripts/verify.sh` green; deviations recorded in design.md
- [ ] 7.2 Archive, PR, CI green

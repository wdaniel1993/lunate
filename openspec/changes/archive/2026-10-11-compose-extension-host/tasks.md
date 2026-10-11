# Tasks: compose the extension host in the CLI

## 1. The host (Coding)

- [x] 1.1 `ExtensionSeams` record (StorePath?, Prompt?, Log?) + `StderrExtensionLog` + `NonInteractiveTrustPrompt` + `ConsoleTrustPrompt` (TDD: log prefixing, prompt accept/deny line parsing)
- [x] 1.2 `ExtensionSession.StartAsync` — discover, load per extension with isolation, expose hooks/renderers/widgets/model providers/MCP, `DisposeAsync` stops cleanly (tests: empty store, one extension, one broken extension continues, denied trust logs and continues, dispose runs session-ending hooks)

## 2. Wiring

- [x] 2.1 Interactive: options seam; compose at session start; registries from the host; harness hooks; MCP tools listed once; dispose on end
- [x] 2.2 Print: options seam; harness hooks; MCP tools listed once; dispose on end
- [x] 2.3 ACP: seams parameter; host once at serve start; harness factory hooks; MCP list reused; dispose on end
- [x] 2.4 Identity: `GitCommonDir ?? WorktreeRoot`; working directory = worktree root (test: non-git dir gets a stable identity)

## 3. End to end (the done-when)

- [x] 3.1 Fixture: `HelloToolCallingHandler` in HelloExtension (logs the observed tool name)
- [x] 3.2 Print-mode e2e: temp store → `-p --json` run → hook observes the tool call (asserted in events/log)
- [x] 3.3 Interactive e2e: temp store → renderer block drawn for a tool result, widget segments in the footer
- [x] 3.4 Trust e2e: project extension — scripted deny refuses + logs; scripted allow loads and records trust; second run does not prompt
- [x] 3.5 Poisoned-store test: `--version` completes with a load-marker absent; no store read on `--help`/`--discover` paths

## 4. Existing tests + hygiene

- [x] 4.1 Session-level tests (Coding + Protocols ACP) get an empty temp store via their helpers
- [x] 4.2 `perf.sh` startup budget unchanged locally; discovery tripwire still green

## 5. Gate + record

- [x] 5.1 `bash scripts/verify.sh` green; deviations recorded in design.md
- [ ] 5.2 Archive, PR, CI green

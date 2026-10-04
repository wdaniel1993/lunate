# Lunate — rules for coding agents

Lunate is a native C# coding agent for the terminal. Background: `docs/guide.md`.
This repo is developed with OpenCode and OpenSpec (schema: `intent-driven`).

## Workflow (OpenSpec)
- Work happens through OpenSpec changes. Never hand-write files under `openspec/changes/` or `openspec/specs/`; use the opsx commands/skills and the `openspec` CLI (new → propose → continue → apply → verify → sync → archive).
- For propose/apply/verify/archive workflows, use the local `openspec-git-discipline` skill: proposals land on `main` before apply, implementation lands on `main` before archive.
- OpenSpec specs are the source of truth for behaviour. If spec and code disagree, stop and report — fix the spec first.
- One change per session. Do only what the change's `tasks.md` asks; anything else becomes a follow-up change.
- src work goes through the `senior-dev` subagent (test-first, `test-driven-development` skill); acceptance-test work through `senior-qa`.

## Build
- .NET 10 (pinned in `global.json`), C# 14, nullable, warnings as errors.
- `scripts/verify.sh` (Windows: `scripts/verify.ps1`) must pass before a change is done: build, tests, single-file publish, performance budgets, public API and format checks.

## Architecture
- `Lunate.Ai` <- `Lunate.Agent` <- `Lunate.Protocols` / `Lunate.Coding`. Never reference upward.
- `Lunate.Tui` references no other Lunate project.
- `Lunate.Agent` references only `Microsoft.Extensions.AI.Abstractions`, `Lunate.Ai` and the BCL.
- Model types are Microsoft.Extensions.AI types (`IChatClient`, `ChatMessage`, `AIContent`). Do not add parallel message types. Tools (`ITool`) and events (`AgentEvent`) are ours.
- Never use `FunctionInvokingChatClient` or `AIFunctionFactory`. The loop in `Lunate.Agent` runs tools, because approvals, events, steering, cancel, error messages and compaction all happen between tool calls. See ADR-0003.
- `Lunate.Roslyn`, MCP servers and extensions load on first use, never at startup.

## Rules
- Architecture decisions (loop, tool contracts, protocols, dependencies) require maintainer sign-off: propose and stop.
- No new NuGet packages. Ask first.
- Public API changes appear in `PublicAPI.Unshipped.txt`. Change public types only if the change says so; otherwise propose the change and stop.
- A failing golden session test means the session format changed. Stop and report; never update golden files to make a test pass.
- In ACP mode nothing writes to stdout except the protocol.
- Tool error messages are read by a model: say what failed and what to do next.
- Every behaviour change has a test. Tests use fixtures in `tests/fixtures/`, never live APIs.
- Keep files under about 300 lines. Prefer plain code over abstractions.
- No secrets in git. Commit small, conventional-commit messages.

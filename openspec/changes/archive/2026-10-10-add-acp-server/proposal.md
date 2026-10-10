# Proposal: ACP server (T-27)

## Why

The guide's frontends include **ACP mode** (`lunate --acp`): editors like Zed or JetBrains start Lunate as their agent over stdio. This change delivers the T-27 slice — SDK choice (ADR-0020), `initialize`, session, prompt with streaming updates, and cancel — so Lunate becomes drivable from a real editor. Permissions and the editor file system follow in T-28.

## What changes

- **ADR-0020** records the SDK choice: LibAcp behind the guide's `IAcpServer` interface (the ADR is the "ask first" for the new package; sign-off pending).
- **`IAcpServer` + LibAcp adapter** in `Lunate.Protocols`: initialize (protocol v1, honest capabilities), session/new (harness per session), session/prompt (one run; `AgentEvent`s mapped to `session/update` per the guide's table), session/cancel (Esc semantics — cancels the run).
- **`lunate --acp`** CLI mode: ACP over stdio; nothing else may write to stdout; logs to stderr; mutually exclusive with print mode and the TUI.
- **Tests**: an in-process client over a pipe pair driving the real server (handshake, streaming order, stop reasons, cancel), plus a stdout-purity check.

## Done when

The in-process client tests pass on all three OSes; `lunate --acp` speaks only protocol on stdout; `scripts/verify.sh` green. One real editor session (Zed) is the phase-5 closing check and is noted on the kanban card as a post-merge item.

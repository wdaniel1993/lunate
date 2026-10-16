# Agent Skills: discovery, exposure, invocation

## Why

Agents increasingly carry their know-how as portable skills — a `SKILL.md` file (name, description, instructions) following the agentskills.io convention that Claude Code and other harnesses already read. Lunate has no way to use them: its only project knowledge channel is `AGENTS.md`, which is unconditional prose — everything loads on every turn or nothing does. Skills are the on-demand layer: cheap to index, loaded in full only when relevant or explicitly invoked.

The repository itself already carries nine skills under `.agents/skills/` (companion skills for the OpenSpec harness). A Lunate session in this repo should be able to see and use them — and so should every user's project.

## What changes

- **Discovery**: project scope `.lunate/skills` > `.agents/skills` > `.claude/skills`, walking up from the worktree root to the repository root; user scope `~/.lunate/skills` > `~/.agents/skills`. Project wins over user; name collisions are warned once and listed in the new `/skills` view.
- **Exposure**: the index (name, description, path) is injected into the system prompt as its own budgeted section — the model loads a full `SKILL.md` with the existing read tool; no new tool. The index has its own cap, reported in `/context`; the "core prompt under 1,000 tokens" rule is amended to exclude the index explicitly.
- **Invocation**: `/skill:<name> [args]` through the existing command router; the skill body becomes a session-recorded prompt section and a run starts. `disable-model-invocation` is honoured (index-excluded, still user-invokable).
- **Security**: project skills load only in trusted repositories, listed in the trust question; `allowed-tools` is accepted and never a permission grant; a malformed `SKILL.md` is a warning, never a load.
- **Extensions**: extensions contribute skills through a new `ISkillSource` service — tagged, budgeted per extension, trusted via the extension's own trust (ADR-0017 catalogue gains the entry).
- **Sample story**: a Lunate session inside this repository discovers `.agents/skills`, uses one when asked, and a `/skill:<name>` run is scripted end-to-end.

## Scope

Design-first change (card: "Agent Skills … (design-first)"): this change lands the decision record, the spec deltas and the implementation task list. Implementation applies as its own follow-up after the design sign-off.

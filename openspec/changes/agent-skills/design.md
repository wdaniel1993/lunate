# Design — agent-skills

## Context (what already exists)

- **Prompt sections**: `AgentHarness` composes requests from an ordered list of `AgentPromptSection(Name, Text)`; a session file records every change as `SessionPromptSectionEntry` ("a system-prompt-section change for deterministic replay"). Appending beats rewriting the prefix (prompt caching, ADR-0017).
- **Base prompt**: `SystemPrompt.Compose` (Coding) fills the embedded `system-prompt.md` from runtime facts plus the `AGENTS.md` chain; the guide pins the core prompt under 1,000 tokens, asserted in `SystemPromptTests`.
- **Command router**: `Commands.BuiltIn` (fixed list) + `Commands.Parse`; `InteractiveSession.Commands.cs` dispatches; commands never reach the model; unknown commands get a dim notice; Tab completion reads `Commands.BuiltIn`.
- **Trust**: `TrustInventory` evaluates project extensions (directory hash), project `mcp.json` (definition hash) and per-server approvals against the repository-identity store; `/trust` lists and clears; the interactive prompt asks `n of m` questions; `lunate trust` records non-interactively.
- **Extension context**: the `ContextBuilding` hook adds tagged messages, merged after a per-extension budget (`HookRunnerOptions.ContextBudgetPerExtension`, default 2,000 estimated tokens, 1 token ≈ 4 chars); the ADR-0017 catalogue lives in the ADR + `docs/spec/extensibility.md`.
- **In-repo skills**: `.agents/skills/` carries nine `SKILL.md` skills (name + description frontmatter) — the sample story runs against them.

## Decisions

### D1 — Index injection (mechanics, cap, overflow)

- The index is a **new appended prompt section** named `skills`, produced at session start (interactive and print mode) after discovery — not a `system-prompt.md` token: it is per-workspace data, and the section machinery already records replay entries and counts toward compaction.
- Line format: `- <name>: <description> [<relative path>]`; a lead line names the tool to use: the model loads a full `SKILL.md` with the existing read tool (no new tool, no new permissions).
- **Cap: 1,000 estimated tokens (4,000 chars)** — same unit as the extension budget. Overflow: reject the oldest additions by discovery order and append one line `+N more skills (see /skills)`.
- **Core-prompt rule amendment**: the guide's under-1,000-token assertion keeps applying to the base composed prompt; the skills index is excluded from that count explicitly (it has its own cap; the test asserts both numbers).
- `/context` gains a `skills: N (index ~M tokens)` segment.
- Extension-contributed lines count under the contributing extension's `ContextBudgetPerExtension`; overflow there drops lines and logs (existing pattern).

### D2 — `/skills` view

One segment per skill in the `/mcp` / `/extensions` single-line discipline: `skills: a (project) — description…; b (user, command-only) — …; c (invalid) — <first sentence>`. States:
- scope: `project` or `user`, or `extension '<id>'`;
- `command-only` marker for `disable-model-invocation` skills;
- `shadows <scope> entry` on the winning entry when a collision was resolved (the shadowed one is not listed separately; it appears in the session's one-time warning);
- invalid skills listed as `<name> (invalid) — <first sentence>` (truncated like `/extensions`), not loaded;
- untrusted project skills listed as `<name> (project, untrusted)`;
- empty: `skills: none found`.

### D3 — Invocation semantics (`/skill:<name> [args]`)

- The router recognizes the `/skill:` prefix; the rest is the name; the argument is optional.
- Invocation **edits** the prompt-section list: a section named `skill:<name>` with the skill's full body, replacing any earlier section of that name (re-invocation keeps one live copy); the edit is recorded as a `SessionPromptSectionEntry` (deterministic replay — "recorded as session entries").
- A run then starts with a synthetic user turn: `[skill: <name>] <args>` (or `[skill: <name>] run the skill` with no args) so the invocation acts immediately — commands alone never reach the model.
- `$ARGUMENTS` in the body is substituted when present; otherwise the args are appended as a final `Arguments: <args>` line only when non-empty.
- Unknown name → dim notice `unknown skill: <name> (see /skills)`, no run, nothing recorded.
- Untrusted project skill → dim notice `skill '<name>' is not trusted in this repository`, no run.
- Command-only skills are invokable (the exclusion is from the index/warning surface for the model, not from the user).

### D4 — Discovery order and boundaries (per the card, 2026-10-11)

- From the worktree root, upward to the repository root (never above it): per directory `.lunate/skills` > `.agents/skills` > `.claude/skills`.
- Then user scope: `~/.lunate/skills` > `~/.agents/skills`.
- Nearest scope wins; within one level the earlier directory wins; project beats user.
- A collision warns once per session (name, winner, shadowed path) and is listed in `/skills` on the winner.
- Discovery is bounded: depth = worktree→repo root walk; a hard cap of 256 skills scanned per scope; unreadable directories are warnings, never errors.

### D5 — Trust gating (per the card)

- Project skills (all three directories) are a **new trust item in `TrustInventory`**: one question per repository identity, directory-content-hashed, re-prompted when the hash changes — the extension pattern exactly.
- The question lists the discovered project skill names (`skills: a, b, c`) — "listed in the trust prompt".
- Untrusted → project skills are excluded from the index and `/skills` shows them `(untrusted)`.
- `/trust` lists the skills record alongside the others; `/trust clear` clears it; `lunate trust` records it non-interactively.
- `allowed-tools` parses and is ignored for **all** scopes — never a permission grant; the approval system is the only grant path.

### D6 — Extension skill sources

- New abstraction `ISkillSource` (Abstractions, semver'd, PublicAPI-tracked): `Id` + enumerate descriptors (name, description, path or inline body). Registered through the extension API like other services.
- Contributed skills join the index tagged `extension '<id>'`, counted under the extension's context budget; the source's skills are trusted iff the extension is trusted; malformed contributions warn like core skills.
- The ADR-0017 catalogue and `docs/spec/extensibility.md` gain the resource-discovery entry; the record lands as `adr/0017-addendum-skills.md` (the model-registry addendum pattern — accepted ADRs are amended, never rewritten). **No packages.**

### D7 — Registers and edge cases

- Print mode composes the base prompt in `PrintMode.cs` — it gets the same discovery + index (same code path; a test pins parity).
- A skill whose frontmatter name differs from its directory name is invalid (deterministic `/skill:` addressing needs one canonical name); the warning names both.
- Symlinked skill directories are followed only within the repository boundary.
- Skill changes during a session are not hot-reloaded (session-start discovery; `/new` re-scans).

## Security notes

- Project skills are repository content: they are hidden until the repository is trusted for skills, and the trust is content-hashed so edits re-prompt.
- `allowed-tools` is deliberately inert; skills can never widen the permission surface.
- The index and `/skills` never print file contents, only name/description/path.
- Skills reach the model as prompt sections, not as tool output — the read tool remains the only content path, with its existing approval and size rules.

## Alternatives considered

- **Template token in `system-prompt.md`** — rejected: per-workspace data does not belong in the embedded template, and token substitution would rescan user text (the template's own rule).
- **A new `skill` tool** — rejected: the read tool already loads files; a new tool would add a permission surface for no new capability.
- **Index as a `ContextBuilding` addition** — rejected: skills are core, not extension-owned; the section path already provides replay entries and compaction accounting.

## Sign-off

Open decisions carried to the sign-off card: D1 cap/overflow numbers, D2 state vocabulary, D3 invocation mechanics (synthetic turn + replace semantics), D6 source shape + addendum. D4/D5 restate the 2026-10-11 decision and are marked as such.

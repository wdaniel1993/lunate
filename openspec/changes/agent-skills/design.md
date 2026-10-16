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
- **Built once per session**: composed at session start and never rebuilt mid-run (the system-prompt prefix stays cache-stable); a wholesale session reload (`/new`, resume-with-reload, an extension reload) re-scans. No `skills`-specific reload command exists or is added.
- Line format: `- <name>: <description> [<relative path>]`; descriptions are truncated (~200 chars, `…`) so one long description cannot eat the cap. A lead line names the tool to use: the model loads a full `SKILL.md` with the existing read tool (no new tool, no new permissions).
- **Index order (pinned)**: project, then user, then extension entries, each group by name (ordinal). Overflow cuts strictly from the end, so it is deterministic.
- **Cap: 1,000 estimated tokens (4,000 chars)** — same unit as the extension budget. Overflow appends one footer line the **model can act on**: `+N more skills (not indexed): <names>` — names only (no descriptions, no paths), comma-separated, truncated at 400 chars with a trailing `…` when even the names don't fit. Rationale (decision): names are the highest-value tokens; full paths in the model prompt would sprawl across scopes, while `/skills` (human) and the model's own `find`/`read` remain available for the overflowed set. No `(see /skills)` pointer — that is a human command.
- **Core-prompt rule amendment**: the guide's under-1,000-token assertion keeps applying to the base composed prompt; the skills index is excluded from that count explicitly (it has its own cap; the test asserts both numbers).
- `/context` gains a `skills: N (index ~M tokens)` segment.
- **Enforcement order for extension lines** (both caps can bite): (1) the contributing extension's `ContextBudgetPerExtension` (default 2,000 est. tokens) is applied at contribution time — over-budget lines are dropped and logged (existing pattern); (2) the global 1,000-token index cap applies to the merged, ordered list. Consequence: extension lines sort last and are the first cut by the global cap — deliberate: core discovery (project/user) outranks extension contributions.
- **Readability (ADR-0016 boundary)**: user skills (`~/.lunate/skills`, `~/.agents/skills`) and extension skill directories live outside the workspace root and would be refused by the read tool's boundary. The read tool gains a **per-run read-only allowance** for every discovered skill directory — including arbitrary bundled files under it (`references/`, `scripts/`) — computed from the run's own discovery, so child worktree runs resolve their own allowance the same way.

### D2 — `/skills` view

One segment per skill in the `/mcp` / `/extensions` single-line discipline: `skills: a (project) — description…; b (user, command-only) — …; c (invalid) — <first sentence>`. States:
- scope: `project` or `user`, or `extension '<id>'`;
- `command-only` marker for `disable-model-invocation` skills;
- `(not indexed)` marker for skills that fell into index overflow, so the user can see what the model does not know;
- `shadows <scope> entry` on the winning entry when a collision was resolved (the shadowed one is not listed separately; it appears in the session's one-time warning);
- invalid skills listed as `<name> (invalid) — <first sentence>` (truncated like `/extensions`), not loaded;
- untrusted project skills listed as `<name> (project, untrusted)`;
- empty: `skills: none found`.

### D3 — Invocation semantics (`/skill:<name> [args]`)

- The router recognizes the `/skill:` prefix; the rest is the name; the argument is optional.
- Invocation **submits a normal prompt**: the skill body becomes a wrapped **user turn** — `<skill name="<name>" dir="<absolute skill directory>">` + body + `</skill>` — sent through the same submission path as typed text. The `dir` attribute states the base directory so relative references in the body resolve.
- **Why a user turn, not a prompt section**: the system prompt stays byte-stable for prompt caching; several skills never stack in one prefix; the model sees exactly what it would see after reading `SKILL.md` itself; compaction and `/resume` handle it like any other turn. "Replace on re-invoke" is therefore unnecessary — a re-invocation is simply a newer turn.
- The turn is **recorded as a user session entry carrying skill metadata** (name, dir) so session files stay self-describing.
- `$ARGUMENTS` in the body is substituted when present; otherwise non-empty args are appended as a final `ARGUMENTS: <args>` line.
- While a run is active the invocation behaves **exactly like a normal prompt submission** — same queue/steer path; there is no separate invocation path.
- Unknown name → dim notice `unknown skill: <name> (see /skills)`, nothing submitted. Untrusted project skill → dim notice that names the fix: `skill '<name>' is not trusted in this repository — approve it in the trust prompt or run 'lunate trust'`.
- Command-only skills are invokable (the exclusion is from the index, not from the user).

### D4 — Discovery order and boundaries (per the card, 2026-10-11)

- From the worktree root, upward to the repository root (never above it): per directory `.lunate/skills` > `.agents/skills` > `.claude/skills`.
- Then user scope: `~/.lunate/skills` > `~/.agents/skills`.
- Nearest scope wins; within one level the earlier directory wins; project beats user.
- A collision warns once per session (name, winner, shadowed path) and is listed in `/skills` on the winner. **Shadowing order (pinned): project > user > extension**, with `.lunate > .agents > .claude` and nearest-first inside the project scope.
- Discovery is bounded: depth = worktree→repo root walk; a hard cap of 256 skills scanned per scope; unreadable directories are warnings, never errors.

### D5 — Trust gating (per the card)

- Project skills (all three directories) are a **new trust item in `TrustInventory`**: one question per repository identity, directory-content-hashed, re-prompted when the hash changes — the extension pattern exactly.
- The question lists the discovered project skill names (`skills: a, b, c`) — "listed in the trust prompt".
- Untrusted → project skills are excluded from the index and `/skills` shows them `(untrusted)`.
- `/trust` lists the skills record alongside the others; `/trust clear` clears it; `lunate trust` records it non-interactively.
- `allowed-tools` parses and is ignored for **all** scopes — never a permission grant; the approval system is the only grant path.

### D6 — Extension skill sources

- New abstraction `ISkillSource` (Abstractions, semver'd, PublicAPI-tracked): `Id` + an enumeration of **skill directories on disk inside the extension's own folder** — v1 is disk-only, no in-memory skills; the core scans and validates them exactly like any other skill, so the read tool's path handling is uniform across every source.
- Contributed skills join the index tagged `extension '<id>'`; they inherit the extension's trust (no separate question) **and its disable state** — `disable-model-invocation` is honoured for extension skills too.
- Extension lines count under the extension's context budget (enforcement order per D1: per-extension budget first, then the global index cap). A shadowed extension skill resolves like any collision: the winner (project/user) carries the `shadows` marker and the one-time warning names it; the extension copy is not listed separately.
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

**Answered 2026-10-16 (Daniel) — approved with adjustments, all folded:**

- **D1**: overflow footer must be model-actionable → names-only list (no descriptions, 400-char cap), no `(see /skills)` pointer; index order pinned project > user > extension, then by name; descriptions truncated ~200 chars; read-only allowance for discovered skill directories (bundled files included; per-run for child worktrees); built once per session, never mid-run; extension-line enforcement order stated (per-extension budget, then global cap).
- **D2**: added `(not indexed)` marker for overflowed skills.
- **D3 changed**: the body goes into a wrapped **user turn** (`<skill name=… dir=…>`), recorded as a user entry with skill metadata — not a prompt section; cache-stable prefix, no stacking, normal queue/steer path; untrusted notice names the fix.
- **D4**: shadowing order project > user > extension stated.
- **D6**: v1 limited to skill directories on disk inside the extension's own folder; inherits the extension's trust and disable state; shadowed copies follow the common collision rule.
- **Process**: the apply card carries a **production composition test** — through the real host, assert the index appears in the actual composed system prompt and `/skill:<name>` reaches the model ("spec'd but not wired" guard).

D4/D5 restate the 2026-10-11 decision and are marked as such.

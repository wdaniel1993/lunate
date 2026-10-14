# Design — sample-worktree-tasks

The change implements what the docs already pin (`docs/spec/extensibility.md` § Workspaces and repository identity; agent-loop and agent-sessions spec text). Open questions for sign-off are Q1–Q7 at the end; D1–D10 record the proposed answers.

## D1 — Child-run workspace surface (`Lunate.Agent`)

`SubagentRequest` gains one optional member:

```csharp
public sealed record SubagentRequest(
    string Prompt,
    int? MaxSteps = null,
    IReadOnlyList<string>? AllowedTools = null,
    string? WorkspaceRoot = null   // NEW: the child run's worktree root; null inherits the parent's
);
```

Semantics: the root is canonicalized (symlinks resolved for existing components, the same discipline as `Workspace`); a missing path or non-directory refuses the spawn with a teaching error result (`workspace_missing`, naming the path) — never an exception, matching every other runner refusal. Naming the parent's own root is allowed and behaves as the inherit path (idempotent). The field is Agent-level (Coding consumes it; extensions reach it through the same `SubagentRequest` they already use).

## D2 — The child tool set per workspace (the factory seam)

Agent cannot construct Coding tools (`read`/`write`/`edit`/`bash`, `cs_*`), so the child registry for another root must be built by the composer. New seam:

```csharp
// AgentHarnessOptions
public Func<string, ToolRegistry>? WorkspaceTools { get; init; }  // build the tool set for a root
```

- Request names no root **or** the parent's root → today's path: child registry = subset of the parent's live tool instances (T-47 behavior, bit-for-bit).
- Request names another root → child registry = `WorkspaceTools(root)` when present; when the seam is null the spawn is refused with a teaching error (`workspace_tools_unavailable`) — honest, never a silent parent-tools child in the wrong directory.
- The `AllowedTools` allow-list applies to the child's **actual** set in both paths (subset rule, parent order for the inherit path, factory order for the workspace path; unknown names refuse as today).
- Coding composes `WorkspaceTools: root => HarnessFactory.CreateTools(new Workspace(root))` plus the `cs_*` tools (D5). The parent's other live facts stay inherited: model client, hooks, mutation queue, file-change sink, approver, system prompt (spec: "composed from the parent's live facts" — working directory and tool set become the two workspace-dependent exceptions).

## D3 — Child session header + placement

Spec pins the child file *"beside its parent's (same sessions directory)"* — unchanged. When the request names another root, the child's header records the **child's own** `repo`/`worktree` identity (detected from the child root) instead of mirroring the parent's; without a workspace root the header mirrors the parent's exactly as today. Resume from the parent's link keeps working (the link carries the child session id; the child file is beside the parent's).

Identity detection currently lives inside Coding's `Workspace`; it moves to a small `Lunate.Agent` helper (`GitIdentity.Detect(root)` — `.git` directory or file + `commondir` parsing, never executing git) and Coding's `Workspace` delegates to it (single source; no behavior change — Coding tests stay green).

## D4 — Per-workspace services and limits (`WorkspaceServices`)

New internal registry in `Lunate.Coding` (shaped for the spec's "Roslyn backend, LSP background services"): keyed by canonical `WorktreeRoot`; values are lazily created, disposed on eviction or shutdown; thread-safe (one gate; creation outside the lock, double-checked). **Limit:** at most `roslyn.maxLoadedWorkspaces` (default **2**) **loaded** Roslyn backends across roots; a third root's load evicts the least-recently-used backend (disposing its loaded workspaces). LRU is touched on use. The setting lives in the `roslyn` settings section; `unlimited` is not offered (a value < 1 is refused at load with a teaching error naming the key). Eviction is observable for tests (an internal hook the tests read, or a `LoadedWorkspaces` count) — the cap proof asserts the LRU victim, not just the count.

## D5 — Roslyn tools composed into runs

`HarnessFactory` composes the five `cs_*` tools into every run's registry (interactive, print, JSON, ACP — one composition), each bound to `() => services.BackendFor(workspace.WorktreeRoot)`. Laziness is preserved end to end: the tool defers backend resolution to its first execution (`Lazy<ICSharpBackend>` already), the registry creates the backend on first request, and `RoslynBackend` keeps its own first-call load. No Roslyn/MSBuild assembly is touched before the first `cs_*` call (startup budget unchanged — pinned by the existing budget gate). Without a discoverable SDK the tools still exist and return their existing teaching errors (NoSdk path). Child runs in a worktree get `cs_*` bound to **their** root through the same composition (`WorkspaceTools`), so the sample's "Roslyn per workspace" proof is real.

## D6 — The sample (`worktree-tasks`)

Directory `samples/extensions/worktree-tasks/` (extension id `worktree`, so tool names are `worktree__*`), Abstractions-only, README with the capability table.

- **`worktree__start`** (`Execute`): args `name` (required), `task` (required), `base` (optional ref, default `HEAD`). Creates `<parentOfRoot>/<rootName>-<name>` via `git worktree add -b lunate/<name> <path> <base>` (paths absolute; runs through `core/process` from `ToolContext.WorkingDirectory`), then runs **one subagent** with `WorkspaceRoot = <path>` and a fixed allow-list (`read`, `write`, `edit`, `bash`, `cs_*`) and returns the child's summary + the worktree facts (path, branch).
- **`worktree__merge`** (`Write`): args `name`, optional `into` (default `HEAD`). Runs `git merge --no-ff lunate/<name>` from the main worktree; on conflict returns a teaching error naming the conflicted files and leaves the repo for a human decision (no auto-resolution, no abort).
- **`worktree__clean`** (`Destructive`): args `name`, optional `force`. `git worktree remove` (+ `git branch -D lunate/<name>`); refuses a dirty worktree without `force` with a teaching error naming the changes.
- **`worktree__list`** (`ReadOnly`): `git worktree list --porcelain`, rendered compactly.

All four are thin wrappers over `core/process` (no new primitives); worktree naming/paths are the sample's policy (the spec puts worktree management in extension territory).

## D7 — Sample tests (kit + real git)

Real temp repositories (created by the tests with `git init` + one commit through `core/process`; the suite already spawns processes and serializes):

1. registration: four tools, ids and annotations;
2. `worktree__start` against a real repo: worktree + branch exist; the scripted child runs **with the worktree as its working directory** (child session header records the worktree path and the same repo identity); a file the child writes lands in the worktree, not the parent;
3. `worktree__merge`: the child's file arrives on the parent branch as a merge commit; a conflicting second worktree returns the teaching error naming the file and leaves no half-merged state decision to the model;
4. `worktree__clean`: removes worktree + branch; dirty refusal without `force`;
5. `worktree__list`: reports both worktrees.

## D8 — Coding-level proofs

In `tests/Lunate.Coding.Tests` (the mode seams live there):

- **Two subagents, two worktrees**: two spawns with different `WorkspaceRoot`s, each child editing its own worktree (scripted clients); files stay isolated; both child sessions sit beside the parent's with their own `worktree` headers; the parent stream carries both children's events (existing forwarding rules).
- **Roslyn per workspace, cap holds**: `cs_diagnostics` in worktree A and B loads two workspaces (two `RoslynBackend`s keyed by root); a third worktree loads and the **LRU victim is A** (assert the evicted root, not only the count); touching B before C keeps B. Uses the fixture solution copied into each worktree.
- **Trust by repository identity**: at the trust-store level — trust recorded for a repository covers a new worktree of the same repository (no re-prompt); the content-hash check stays per worktree (an extension changed in one worktree re-prompts there only). Extensibility.Tests addition; no product change expected — if the store does not behave this way, the fix lands here.

## D9 — Spec deltas

- **agent-loop** (MODIFIED, "Subagent runs"): the request's `WorkspaceRoot`; the composition clause becomes "same model client, hooks, mutation queue, file-change sink and approver — with the working directory and tool set following the request's workspace"; the refusal vocabulary gains `workspace_missing` / `workspace_tools_unavailable`; new scenarios for a worktree child (isolated edits, header, allow-list against the child's own set).
- **agent-sessions** (MODIFIED, "Session store and resume"): the child header records the child's own `repo`/`worktree` when the child runs in another worktree; the file stays beside the parent's; a scenario pins the header + resume.
- No extensions-spec delta: the per-workspace registry is core-internal; extension services stay session-scoped.

## D10 — Errors and limits

Worktree root missing → `workspace_missing` (refusal). Tool seam absent → `workspace_tools_unavailable` (refusal). Dirty clean without force → teaching error. Merge conflict → teaching error naming files. Cap eviction is silent in behavior (documented; observable for tests). Depth stays 1 (a worktree child cannot spawn further children — the sample's own tool is what spawns). No new packages.

## Open Questions (sign-off)

- **Q1 — `SubagentRequest.WorkspaceRoot`** as the surface (name + null-inherits semantics + refusal vocabulary) — yes/adjust?
- **Q2 — The `WorkspaceTools` factory seam on `AgentHarnessOptions`** (Coding-supplied; null + non-parent root = refusal) — yes/adjust? Alternative: refuse workspace children entirely when the seam is null vs inherit parent tools (silent wrong-directory children) — propose refusal.
- **Q3 — Child session header records the child's own repo/worktree** (file stays beside the parent's); identity detection extracted to `Lunate.Agent` and reused by `Workspace` — yes/adjust?
- **Q4 — Compose the five `cs_*` tools into every run** (all modes, child runs included) through the per-workspace registry; cap `roslyn.maxLoadedWorkspaces` default **2**, LRU unload — confirm (this is the first product wiring of the Roslyn tools).
- **Q5 — Sample policy:** tools `worktree__start/merge/clean/list`; sibling path `<parentOfRoot>/<rootName>-<name>`; branch `lunate/<name>`; merge `--no-ff`, conflicts are teaching errors; clean refuses dirty without `force` — yes/adjust? (Alternatives: worktrees under `.lunate/worktrees/` inside the repo; fast-forward merges.)
- **Q6 — Proofs as listed** (sample kit tests with real git; Coding-level two-subagent + Roslyn cap + grouping proofs; trust proof at the trust-store level) — yes/adjust?
- **Q7 — ADR:** `adr/0017-addendum-run-workspaces.md` (child-run workspaces; per-workspace services and the Roslyn cap) — sufficient, or a full ADR?

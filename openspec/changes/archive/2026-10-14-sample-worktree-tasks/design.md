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

Semantics: the root is canonicalized (symlinks resolved for existing components, the same discipline as `Workspace`); a missing path or non-directory refuses the spawn with a teaching error result (`workspace_missing`, naming the path) — never an exception, matching every other runner refusal. **The root must be a worktree of the same repository** (same git-common-dir); any other directory refuses with `workspace_outside_repository` (sign-off Q1). Naming the parent's own root is allowed and behaves as the inherit path (idempotent). The field is Agent-level (Coding consumes it; extensions reach it through the same `SubagentRequest` they already use). **The asking run's workspace root is shown in the approval prompt**: `ApprovalRequest` (and the `ApprovalRequested` event) gains `WorkspaceRoot` — the asking run's root — so a subagent's ask is labelled with where it will act, not only by run id and depth.

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
- **Project MCP tools are not offered to a child with a different root** (sign-off Q2): their servers run in the parent's directory, so offering them would act in the wrong place. A server can opt in with an explicit `workspaceIndependent: true` in its `mcp.json` entry (default false); the workspace-tool factory excludes the rest. **Child runs use the parent session's loaded extensions as-is** — no per-worktree rediscovery, no mid-run trust prompts; extension tools are offered per the same allow-list rules.

## D3 — Child session header + placement

Spec pins the child file *"beside its parent's (same sessions directory)"* — unchanged. When the request names another root, the child's header records the **child's own** `repo`/`worktree` identity (detected from the child root) instead of mirroring the parent's; without a workspace root the header mirrors the parent's exactly as today. Resume from the parent's link keeps working (the link carries the child session id; the child file is beside the parent's).

Identity detection currently lives inside Coding's `Workspace`; it moves to a small `Lunate.Agent` helper (`GitIdentity.Detect(root)` — `.git` directory or file + `commondir` parsing, never executing git) and Coding's `Workspace` delegates to it (single source; no behavior change — Coding tests stay green). **The child keeps the parent's system prompt verbatim** (T-47 behavior) even when AGENTS.md differs in its worktree — no per-worktree prompt re-discovery (sign-off Q3). The header semantics for child runs (and any format change they need) are covered by the ADR (D9, Q7): the existing schema-2 `repo`/`worktree` fields carry the child's own identity; should the apply prove a disambiguation field is needed, the ADR records the bump and its migration.

## D4 — Per-workspace services and limits (`WorkspaceServices`)

New internal registry in `Lunate.Coding` (shaped for the spec's "Roslyn backend, LSP background services"): keyed by canonical `WorktreeRoot`; values are lazily created, disposed on eviction or shutdown; thread-safe (one gate; creation outside the lock, double-checked). **Limit:** at most `roslyn.maxLoadedWorkspaces` (default **2**) **loaded** Roslyn backends across roots; a third root's load evicts the least-recently-used backend (disposing its loaded workspaces). LRU is touched on use. **A backend with an in-flight call is pinned and never evicted** (sign-off Q4): the registry pins the entry for the call's duration; when every loaded entry is pinned, a new root's load **waits** (cancellation-aware) until a pin releases rather than failing or unloading under a live call. Eviction is observable for tests (an internal hook the tests read, or a `LoadedWorkspaces` count) — the cap proof asserts the LRU victim and the pin behavior, not just the count. The setting lives in the `roslyn` settings section; `unlimited` is not offered (a value < 1 is refused at load with a teaching error naming the key). **Memory is documented** (sign-off Q4): the cap doubles the worst-case Roslyn footprint — up to 2× the ADR-0009 reference-workspace budget — noted in the budget docs and included in the memory measurement notes.

## D5 — Roslyn tools composed into runs

`HarnessFactory` composes the five `cs_*` tools into every run's registry (interactive, print, JSON, ACP — one composition), each bound to `() => services.BackendFor(workspace.WorktreeRoot)`. **They are offered only in workspaces that look like C# workspaces** (sign-off Q4): a cheap file check for `.sln`/`.slnx`/`.csproj` at the root (bounded enumeration, no Roslyn, no MSBuild) gates the composition; elsewhere the tools are simply absent (the model never sees them). Laziness is preserved end to end: the tool defers backend resolution to its first execution (`Lazy<ICSharpBackend>` already), the registry creates the backend on first request, and `RoslynBackend` keeps its own first-call load. No Roslyn/MSBuild assembly is touched before the first `cs_*` call (startup budget unchanged — pinned by the existing budget gate). Without a discoverable SDK the tools still exist and return their existing teaching errors (NoSdk path); **a fresh worktree without restore output returns the ADR-0006 restore hint** (existing `RestoreRequired` path — pinned by a test, sign-off Q4). Child runs in a worktree get `cs_*` bound to **their** root through the same composition (`WorkspaceTools`), so the sample's "Roslyn per workspace" proof is real.

## D6 — The sample (`worktree-tasks`)

Directory `samples/extensions/worktree-tasks/` (extension id `worktree`, so tool names are `worktree__*`), Abstractions-only, README with the capability table.

- **`worktree__start`** (`Execute`): args `name` (required), `task` (required), `base` (optional ref, default `HEAD`). **Name handling (sign-off Q5):** `name` is sanitized — no `..`, no path separators, restricted to a safe character set — and the derived branch `lunate/<name>` is validated with `git check-ref-format --branch`; an existing branch refuses with a teaching error (never reuses or force-moves one). Creates `<parentOfRoot>/<rootName>-<name>` via `git worktree add -b lunate/<name> <path> <base>` (paths absolute; runs through `core/process` from `ToolContext.WorkingDirectory`), then runs **one subagent** with `WorkspaceRoot = <path>` and a fixed allow-list (`read`, `write`, `edit`, `bash`, `cs_*`) and returns the child's summary + the worktree facts (path, branch).
- **`worktree__merge`** (`Write`): args `name`, optional `into` (default `HEAD`). **Refuses a dirty target worktree** with a teaching error naming the changes; runs `git merge --no-ff lunate/<name>` from the main worktree; **on conflict runs `git merge --abort`** and returns a teaching error naming the conflicted files — never leaves a half-merged tree (sign-off Q5).
- **`worktree__clean`** (`Destructive`): args `name`, optional `force`. **Refuses while a run is active in that worktree** (sign-off Q5; the sample tracks its live spawns) and deletes the branch **only if it is merged** (or with explicit `force`); `git worktree remove` (+ branch delete) with the dirty refusal as before.
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

- **Two subagents, two worktrees**: two spawns with different `WorkspaceRoot`s, each child editing its own worktree (scripted clients); files stay isolated; **a cross-worktree write is refused** (a child in worktree A writing into worktree B is outside its boundary — sign-off Q6); both child sessions sit beside the parent's with their own `worktree` headers; the parent stream carries both children's events (existing forwarding rules).
- **Roslyn per workspace, cap holds**: `cs_diagnostics` in worktree A and B loads two workspaces (two `RoslynBackend`s keyed by root); a third worktree loads and the **LRU victim is A** (assert the evicted root, not only the count); touching B before C keeps B; **an in-flight call pins its workspace — the cap waits instead of unloading under it** (sign-off Q6); **a fresh worktree without restore output returns the restore hint**; a workspace without `.sln`/`.slnx`/`.csproj` offers no `cs_*` tools.
- **MCP tools in a child root**: project MCP tools are absent from a different-root child's set; a server with `workspaceIndependent: true` is offered (sign-off Q6).
- **Trust by repository identity**: at the trust-store level — trust recorded for a repository covers a new worktree of the same repository (no re-prompt); the content-hash check stays per worktree (an extension changed in one worktree re-prompts there only). Extensibility.Tests addition; no product change expected — if the store does not behave this way, the fix lands here.

## D9 — Spec deltas

- **agent-loop** (MODIFIED, "Subagent runs"): the request's `WorkspaceRoot` (same-repository rule + refusals `workspace_missing` / `workspace_outside_repository` / `workspace_tools_unavailable`); the composition clause becomes "same model client, hooks, mutation queue, file-change sink and approver — with the working directory and tool set following the request's workspace"; project MCP tools are not offered to a different-root child unless their server is marked `workspaceIndependent`; child runs use the parent session's loaded extensions as-is (no rediscovery, no mid-run trust prompts); the approval seam carries the asking run's workspace root; new scenarios for a worktree child (isolated edits, header, allow-list against the child's own set), the refusals, and the approval label.
- **agent-sessions** (MODIFIED, "Session store and resume"): the child header records the child's own `repo`/`worktree` when the child runs in another worktree; the file stays beside the parent's; a scenario pins the header + resume.
- No extensions-spec delta: the per-workspace registry is core-internal; extension services stay session-scoped.

## D10 — Errors and limits

Worktree root missing → `workspace_missing` (refusal). Root outside the repository → `workspace_outside_repository` (refusal). Tool seam absent → `workspace_tools_unavailable` (refusal). Dirty clean without force → teaching error. Merge conflict → `git merge --abort` + teaching error naming files. Cap eviction is silent in behavior (documented; observable for tests); an all-pinned cap makes a new load wait (cancellation-aware), never unload under a call. Depth stays 1 (a worktree child cannot spawn further children — the sample's own tool is what spawns). No new packages.

## Sign-off (2026-10-14)

Sign-off on Q1–Q7 **with adjustments** (recorded verbatim in the sign-off card `t_2ed85db5`; D1–D10 above fold them):

- **Q1:** yes, plus — `WorkspaceRoot` must be a worktree of the **same repository** (same git-common-dir), else `workspace_outside_repository`; **show the child root in the approval prompt**.
- **Q2:** yes, and the same principle extends: **project MCP tools are not offered in a different-root child** unless explicitly marked workspace-independent; **child runs use the parent session's loaded extensions** — no per-worktree rediscovery, no mid-run trust prompts.
- **Q3:** yes, plus — the child keeps the parent's system prompt even if AGENTS.md differs in its worktree; the header/format change is covered by the ADR.
- **Q4:** yes, with conditions — offer `cs_*` only in workspaces with `.sln`/`.slnx`/`.csproj` (cheap detection, no Roslyn load); **LRU never unloads an in-flight workspace (pin; all-pinned → clear refusal or wait)**; document the memory (cap 2 ≈ up to 2× the ADR-0009 Roslyn budget) and include it in the memory measurement; fresh worktree → ADR-0006 restore hint.
- **Q5:** yes, plus — sanitize names (no `..`, no slashes), validate branch names, refuse an existing branch; merge refuses a dirty target and on conflict runs `git merge --abort` (never a half-merged tree); clean refuses while a run is active in that worktree and deletes the branch only if merged.
- **Q6:** yes, plus tests — cross-worktree write refused; MCP tools absent in a child root; no unload during an in-flight call; restore hint in a fresh worktree.
- **Q7:** a **full short ADR** — "per-run workspaces and per-workspace services" — amending 0006, 0016 and 0017, rather than another 0017 addendum.

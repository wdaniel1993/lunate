## Why

Maintainer addendum to the extensibility architecture (ADR-0017): git worktree support. Every run needs a workspace object (not a bare cwd), project identity must be repository identity plus worktree, sessions group by repository and stay distinct per worktree, trust and services key correctly, and the fitness suite gains a ninth sample. The format/contract pieces integrate into the Part A series (`add-extension-formats`, `add-session-schema-v2`, a new `add-workspace-roots`); this change lands the docs.

## What Changes

- **ADR-0017** (now accepted) gains the "Workspaces and repository identity" decision block: workspace = `WorktreeRoot`/`RepoRoot`/`GitCommonDir` per run; tools and `bash` run against the run's workspace; project identity = repository identity + worktree path; sessions grouped by repository, distinct per worktree, header records both; trust keyed by repository identity (content-hash stays per worktree); per-workspace services keyed by `WorktreeRoot` (Roslyn cap: two loaded workspaces, LRU); `FileChanged` carries the workspace id; approval uses the worktree's own index; AGENTS.md/settings discovery starts at the worktree root; actionable restore hint for fresh worktrees; worktree management is extension territory.
- **`docs/spec/extensibility.md`**: new "Workspaces and repository identity" section; fitness suite gains `worktree-tasks` (sample 9); out-of-process card renumbered to T-52.
- **Guide**: series gains T-51 (worktree-tasks sample) and T-52 (out-of-process host); fitness list updated.

## Capabilities

### New Capabilities
- None (documentation change; `skip_specs`).

### Modified Capabilities
- None here — the behavioural pieces land in the Part A series: session header/grouping in `add-session-schema-v2`; the workspace object and run plumbing in `add-workspace-roots` (new); restore-hint and service-keying pins with T-25/T-39.

## Impact

- `adr/0017-extensibility-architecture.md` (amended; status flip to accepted landed separately on `main`), `docs/spec/extensibility.md`, `docs/guide.md`, and this change's artifacts. No `src/` changes.

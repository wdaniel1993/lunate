# The two queues

Work on Lunate moves through two queues. They are separate because the two halves have different
bottlenecks: producing a pull request parallelises almost perfectly, while merging does not — every
merge changes `main` and re-invalidates every other pull request's gate.

This document is the description of record. If a session, a script or a habit disagrees with it, the
document wins and the script changes.

## Summary

| | Queue 1 | Queue 2 |
|---|---|---|
| Question | How does an issue become a pull request? | How does a pull request become a merge? |
| Workers | **three**, in parallel | **one**, single slot |
| Input | an open GitHub issue | an open pull request |
| Produces | a **draft** pull request | a merged commit on `main` |
| Never does | undrafts, merges | edits the change's code |

## Queue 1 — issue to draft PR

Three workers run at once. Each takes one issue, claims it, works in its own worktree, and ends with a
draft pull request. A worker never undrafts, never merges, and never touches another worker's branch.

A worker run is:

1. **Claim the issue.** `lunate-claims.py claim <n> --run <name> --pid <driver pid>` writes a claim file
   under `~/.hermes/state/lunate-claims/` and adds the `agent/working` label. The claim records the pid
   of the long-lived driver, not of the short-lived CLI that wrote it — otherwise every claim reads
   `dead` the instant it is created and a second worker duplicates the issue.
2. **Cut a worktree.** `.worktrees/issue-<n>` on branch `wt/issue-<n>`, from `origin/main`.
3. **Drive one OpenCode session** over the server API. The session works the issue; the driver watches
   for stalls (15 minutes without a new message), aborts, and resumes once from the branch state. Two
   consecutive stalls block the issue with `agent/blocked` and the log excerpt.
4. **Commit and push** whatever the session produced. No commits means the issue is blocked, not done.
5. **Open a draft pull request.** The body's first line is `Refs #N` — or `Closes #N` when merging
   completes the issue — followed by one sentence on what the PR does for it. The driver converts a PR
   that a session opened as ready back to draft: **queue 1 owns drafts, queue 2 owns undrafts.**
6. **Comment on the issue** with the PR link and one line of status. The issue is the durable public
   identity of the work; the PR is how it lands.

Command:

```sh
python3 ~/.hermes/scripts/lunate-queue1.py --issue 187
python3 ~/.hermes/scripts/lunate-queue1.py --issue 187 --brief-file /tmp/slice.md   # one slice of a big issue
```

Start three of these on three different issues. Claims make that safe; the claim list must read `alive`
for every worker before you walk away.

## Queue 2 — draft PR to merged commit

Exactly one worker, because merging is the serial part. It never edits a change: if the gate fails, the
pull request goes back to queue 1's side as a draft with a flag on it.

A pass over a PR is:

1. **Rebase** onto the current `main` if it is behind, and push.
2. **Undraft** it. This is the only place a pull request stops being a draft.
3. **Gate** it: the full matrix on the PR's own head. A run dispatched with `workflow_dispatch` is not
   PR-owned and does not satisfy branch protection — never use it as a verdict.
4. **Merge** on green (squash), delete the branch, and comment the merge on the issue.
5. If it is not green: flag it, undraft it back, and leave it for queue 1.

State lives on the pull request, not in a second tracker:

- `queue2/hold` — a durable hold. Nothing merges while it is present. This is the only hold that
  survives a head change.
- `queue2/needs-work` — plus a marker file `nw-<pr>` holding the head SHA. The flag belongs to the head
  it judged: same head means skip, a new head clears it. A bare label would let a verdict outlive the
  commit it judged.
- `queue2/conflict` — the rebase could not be resolved automatically.

Two further rules the queue enforces and a human must not override:

- A run that has been `in_progress` for more than 45 minutes is treated as wedged and re-triggered once
  per head by closing and reopening the pull request (which produces a PR-owned suite). Never cancel a
  CI run by hand: duplicate runs share required context names, and cancelling one marks every required
  context failed.
- Commits whose subject starts with `wip(` never merge; a check fails any pull request that still
  carries one.

Command (the cron job runs it every five minutes; the lock is outside every worktree):

```sh
bash ~/.hermes/scripts/lunate-queue2.sh
```

## Shared rules

- **Drafts belong to queue 1, undrafts to queue 2.** Any other arrangement drifts: a ready PR is a claim
  that it has been rebased against today's `main`.
- **A flag must carry its head.** A verdict that does not name the commit it judged is not a verdict.
- **One gate at a time.** Never run two `verify.sh` sharing `HOME=/tmp/emptyhome`.
- **The issue is the public record.** Scope changes, findings and decisions go on the issue as well as
  in the PR.
- **Review is advice, not the gate.** Reviews run on a model from a different family than the
  implementers (`lunate-review <pr>`, default `opencode-go/gpt-6-luna`) and comment on the PR. The gate
  is CI plus a human decision.
- **Kanban is retired.** There is no card board. Issues hold the durable identity, claims hold execution
  state, and pull-request labels hold queue-2 state. Do not reintroduce a second tracker.

## Why this shape

Producing a PR is bounded work with an isolated workspace, so three workers cost little and hide each
other's latency. Merging is a global operation on a shared branch: a second merger would either queue
behind the first or race it. Hence three and one, not four, and not two and two.

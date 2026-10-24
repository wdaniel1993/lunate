# Branching sessions

## Why

A session is already a tree on disk — every content entry carries `parentId` — but everything about the product treats it as a line: the model sees every entry in file order, a compaction applies wherever it lies, and there is no way to go back to an earlier message without throwing the rest away. Three costs follow: a wrong turn poisons the context permanently, exploring two approaches means two sessions, and `/compact` has to be all-or-nothing because there is no path to scope it to.

## What changes

- **An explicit active leaf.** A new schema-3 entry kind, `activeLeaf`, is appended when the leaf moves backwards; until then the leaf is simply the last entry. One sentence governs everything: *the active leaf is the most recently appended entry, unless a later active-leaf entry points elsewhere.*
- **The model sees the path, not the file.** Context is the active leaf's `parentId` chain, root to leaf. A compaction applies only if the compaction entry itself is on that chain, so per-branch compaction falls out of the rule instead of needing machinery.
- **`/tree`, `/fork`, `/clone`.** `/tree` browses the tree in the existing transcript viewer and puts a picked message back in the editor; `/fork` writes a new file up to the picked entry; `/clone` copies the session. Both record `origin: {sessionId, entryId}` in the header.
- **A branch summary where it can be seen.** Leaving branch A for B with the option on writes a `branchSummary` entry as the **first entry after the leaf moved to** — on B's path, invisible from A. It is not a compaction: it replaces nothing.
- **Writes are refused while a run is active.** A run's pending tool calls belong to a path; forking or rewinding under it has no correct answer.

## The format exception, stated plainly

This change deliberately changes the session format, so it **updates the golden files in the same PR** and shows the golden diff in the PR description. That is the explicit exception to the AGENTS.md rule that goldens are frozen; every other change continues to treat them as read-only.

Schema 3 is written by every new session. The loader still accepts 1, 2 and 3, because reading our own older fixtures is regression coverage, not a compatibility programme — nobody runs Lunate yet, and the design does not pay for a user base it does not have.

An addendum to ADR-0018 records the tree model, the leaf rule, `origin`, and the summary's placement.

## Devil's advocate

- **A tree in an append-only file invites rewrites.** It does not here: a switch appends one small entry, and nothing is ever rewritten. The cost is that a file can contain entries on branches nothing points at — dead weight that is also the whole point (they are recoverable).
- **Two notions of "current" would be fatal** — a leaf pointer plus "last entry" is exactly that unless one rule subsumes the other. Hence the single sentence, and a loader test that a file without any `activeLeaf` behaves exactly as today.
- **The summary could quietly become a compaction.** It carries no `replaces` list and is a distinct kind, so a later compaction on B may list it among what it replaces — which is legal only because by then it *is* on that path.
- **`/clone` duplicates arbitrarily large files.** It is a copy, named as a copy, and the confirmation names the source and the new path.

## Impact

- `Lunate.Agent` / session layer: leaf tracking, path-scoped context, two new entry kinds, `origin` header fields.
- `Lunate.Tui`: `/tree`, `/fork`, `/clone`, the active-run refusal, one `keybindings`-free surface (no Alt keys).
- Specs: `agent-sessions` (format, path, summary, provenance) and `tui` (the three commands).
- Goldens: new `golden-branch-tree.jsonl`; the existing 1/2/3 fixtures stay byte-identical.
- No new package, no public extension-surface change.

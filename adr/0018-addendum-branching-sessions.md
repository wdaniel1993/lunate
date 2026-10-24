# ADR-0018 addendum: branching sessions (the session is a tree)

- Status: proposed — 2026-10-24
- Context: `branching-sessions` (the tree model, `activeLeaf`, `origin`, per-path context)
- Amends: ADR-0018 (session schema 2)

## Context

Every content entry has carried a `parentId` since schema 1, so a session file has always been a
tree; only the readers were linear. The model saw every entry in file order, a compaction applied
wherever it lay, `Session.HistoryItems` took the last compaction in the file, and there was no way
to leave a wrong turn without abandoning everything after it.

The format is frozen by golden tests, so the tree has to arrive as a schema event, not as a quiet
change of interpretation.

## Decision

- **One file per session; the tree lives inside it.** `/tree`, branch switches and in-place rewinds
  append to the session's own file. `/fork` and `/clone` write a new file with an `origin` header.
  A file per branch would turn `/tree` into a directory read and break "one session = one file" for
  copy, share and resume.
- **Schema 3, two new entry kinds.** `activeLeaf` (`leafId`) is written only when the leaf moves; the
  most recently appended one wins. `branchSummary` (`fromLeafId`, `summary`) is a distinct kind, not
  a compaction. The header gains optional `origin` (`{sessionId, entryId}`) and `branched: true`.
  Existing 1/2/3 fixtures keep their bytes; `golden-branch-tree.jsonl` covers the new kinds.
- **The leaf rule, one sentence and one function.** *The active leaf is the most recently appended
  entry, unless a later active-leaf entry points elsewhere.* `SessionTree.ActiveLeaf` is the only
  implementation; every reader asks it, and no reader asks "is this a tree session?". A file with no
  `activeLeaf` therefore resolves exactly as it did before, which is what the single-path digests pin.
- **A move entry is a marker, not a path entry.** Its `parentId` is the tip being left, so the append
  chain stays honest, but it never appears on any chain and never reaches a request. Appends hang off
  the *leaf* (`SessionTree.ActiveLeaf`), not off the file's last line.
- **Path-scoped context, per-path compaction.** The request is the leaf's chain, root to leaf. A
  compaction applies iff its own entry is on that chain; ids in its `replaces` list that are off the
  chain are ignored rather than errors. The compaction summary is still emitted first, so a session
  with a single path produces the same request (and the same digest) as before.
- **The summary goes where the model can see it.** With the option on, leaving A for B makes one call
  on the session's active model and writes `branchSummary` with `parentId` = the leaf moved to, so it
  is the first entry on B's path and invisible from A. It carries no `replaces` list; a later
  compaction on B may list it among what it replaces.
- **Writes are refused while a run is active.** Fork, clone and rewind refuse with a notice before any
  file work; `/tree` only reads and stays available. The fork point is therefore always a message
  boundary, by construction rather than by a special case.
- **Goldens.** This change updates the session fixtures deliberately (AGENTS.md's named exception):
  `golden-branch-tree.jsonl` is added and every existing fixture keeps its exact bytes.

## Consequences

- A session file can hold entries on branches nothing points at. That is dead weight *and* the point:
  they are recoverable, and path-scoped compaction is what keeps the request small.
- Every reader of the session must go through `SessionTree`; a reader that walks `Entries` in file
  order (instead of the active path) silently includes dead branches. The harness constructor, the
  request builder, `/tree`, `/fork` and the compaction lookup all use it.
- The node-level surface (`MoveLeaf`, `Fork`, `Clone`, `ActivePath`) is internal and reaches the
  interactive frontend through `InternalsVisibleTo`, so this change adds no public API.
- Fork and clone copy the session *file*; the image sidecar directory is not copied, so a fork of a
  session with images resumes with the documented missing-blob error (ADR-0018 addendum, images).
  Copying the sidecar is a follow-up.
- Requests for a session that never moved its leaf are byte-identical to the previous release.

## Alternatives considered

- **A branch per file.** Cleaner on disk, but `/tree` becomes a directory scan, copy/share/resume
  need a set not a file, and a dead branch stops being recoverable in one place. Rejected.
- **Store the leaf in the header (rewrite the first line).** Breaks append-only, needs a rewrite lock
  and a temp file, and turns a crash into a corrupt header. Rejected.
- **A `currentPath` entry listing the whole chain.** Duplicates state that is already derivable from
  `parentId`, and goes stale as soon as anything is appended. Rejected.
- **Make the branch summary a compaction.** Then leaving a branch would silently drop entries. A
  distinct kind with no `replaces` keeps it as ordinary context. Rejected.

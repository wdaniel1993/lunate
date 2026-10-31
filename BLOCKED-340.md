# Card #340 — blocked on the write-verifier seam (PR #342 / issue #336)

The card's premise is "TypeScript becomes the *second* consumer of the write-verifier seam
(`verified-edits-implementation`), without touching the seam — C# is the first consumer,
through `Lunate.Roslyn`." On `origin/main` (574557cc, the base of this branch) that premise
does not hold: the seam is not merged yet.

Measurements at the reported condition:

- `git log origin/main -S "IWriteVerifier" -- src` → no commits; the symbol does not exist
  anywhere under `src/` or `extensions/`.
- `src/Lunate.Extensibility.Abstractions/ExtensionApi.cs` declares `Current = "1.12.0"`.
  Task 1.1 of the unimplemented OpenSpec change `verified-edits-implementation` bumps it to
  `1.13.0` as part of the seam work.
- `src/Lunate.Extensibility.Abstractions/IExtensionContext.cs` exposes `Register`,
  `RegisterService`, `RegisterToolResultRenderer`, `RegisterStatusWidget`, `RegisterTool`,
  `RegisterWorkspaceService`, `RegisterMcpServer`, `RegisterModelProvider`,
  `RegisterModelMiddleware`, `RegisterVirtualModel`, `RegisterSkillSource`,
  `RegisterCommandSource` — and no `RegisterWriteVerifier`.
- The OpenSpec change `openspec/changes/verified-edits-implementation` has all tasks
  unchecked; its design (task 1–5) covers the exact contract, host pipeline, Roslyn
  verifier and settings that must exist before any extension can register a verifier.
- The whole seam implementation lives in open PR #342 (branch `wt/issue-336`, issue #336),
  status 2026-10-31: OPEN, not a draft, base `main`, merge state BLOCKED (awaiting
  review), 13 review comments.

So the symptom is not "the lsp-diagnostics extension does not verify TypeScript writes yet";
on main *nothing at all* is verified, because the host has no verifier seam yet. Building the
lsp-diagnostics verifier against a locally invented seam (rule 2a's regression test) would
guarantee churn: PR #342 already defines the contract and its shapes.

## Why this is a maintainer decision, and not a card shortcut

The binding rules require this card to be one pull request based on `origin/main`. Both ways
around the missing seam break a rule:

1. Stacking on `origin/wt/issue-336` makes this PR depend on (and carry) an unmerged sibling
   card, and fixes the merge order — that is the maintainer's call, and the card says to stop
   and ask rather than invent one.
2. Re-implementing `IWriteVerifier` / `RegisterWriteVerifier` on this branch is the body of
   PR #342: doing it would duplicate other in-flight work and "touch the seam" that the card
   forbids touching ("if it does, that is a seam bug and gets its own issue" — here the seam
   does not merely need a change; it does not exist).

## Decision requested

Pick one:

- **Wait** (default): implement this card immediately after PR #342 merges, in a fresh
  session that branches from the merged main. The lsp-diagnostics side fits the card without
  seam changes; the verifier will answer from the `ext/lsp-diagnostics/server` service it
  already registers, degraded to *unverified with reason* on slow/absent/crashed servers.
- **Stack now**: allow this branch to merge `origin/wt/issue-336` first and build on top; the
  merge order then becomes part of this PR's review.

## Status

No code has been changed on this branch: the empty commit carrying this file exists only so
the draft PR can record the blockage where the card work normally lives. Nothing was merged,
pushed to main, or invented in the seam's place. The same finding is posted on issue #340,
where the card requires issue-side visibility for scope-changing discoveries.

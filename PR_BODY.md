Refs #340

This card cannot be started from `origin/main`: the write-verifier seam it consumes
(`verified-edits-implementation`, issue #336) is **not merged yet**. This draft PR records
the blockage and the decision it needs; no code has been changed.

## What the investigation found

Per the card's "prove the premise at the reported condition" rule, measured at the branch
base (origin/main @ 574557cc):

- `git log origin/main -S "IWriteVerifier" -- src` → no commits; the symbol does not exist
  anywhere under `src/` or `extensions/`.
- `IExtensionContext` exposes `Register`, `RegisterService`, `RegisterToolResultRenderer`,
  `RegisterStatusWidget`, `RegisterTool`, `RegisterWorkspaceService`, `RegisterMcpServer`,
  `RegisterModelProvider`, `RegisterModelMiddleware`, `RegisterVirtualModel`,
  `RegisterSkillSource`, `RegisterCommandSource` — and no `RegisterWriteVerifier`.
- `ExtensionApi.Current` is `1.12.0`; the OpenSpec change `verified-edits-implementation`
  (all tasks unchecked, never applied) bumps it to `1.13.0` as part of the seam work.
- The entire seam implementation — contract, host pipeline, Roslyn C# verifier, settings —
  is open PR #342 (branch `wt/issue-336`, issue #336). (Merge state at the time of this
  line's writing was BLOCKED; see the re-check below for the current status.)

So the symptom is not "lsp-diagnostics does not verify TypeScript writes yet"; on main
*nothing* is verified, because the host has no verifier seam at all. The card's own line
"if the seam needs a change, that is a seam bug and gets its own issue" understates it —
the seam does not merely need a change, it does not exist on main.

## Re-check (2026-10-31 resumption run)

- `origin/main` is still `574557cc`: no `IWriteVerifier` / `RegisterWriteVerifier` anywhere in
  `src` or `extensions`, `ExtensionApi.Current` still `1.12.0`. The blockage stands.
- PR #342's GitHub merge state moved from BLOCKED to **CLEAN**, but it has 0 approved reviews
  and was returned to draft again by the queue for failing gates (`verify` on all three OSes,
  `verify-powershell`, `ci-ok`). A red, unreviewed seam does not weaken the case for stopping.
- The environment note below is retired: SDK 10.0.402 is installed here now, so whoever picks
  the card up can run the local gate.
- lsp-diagnostics is confirmed ready to consume the seam: its manifest declares
  `services: ["ext/lsp-diagnostics/server"]` and `LspDiagnosticsExtensionFactory` registers the
  lazily started `LspServerService` under `ext/lsp-diagnostics/server` — the verifier would
  answer from that running server, degrading to *unverified with reason* on slow/absent/crashed
  servers, exactly as the original plan describes.

## Why I stopped instead of working around it

Both workarounds would break a binding rule, so I did not invent either:

1. **Stacking** on `origin/wt/issue-336` would make this PR carry an unmerged sibling card's
   work and hard-code the merge order — a maintainer decision.
2. **Re-implementing** the seam here is the body of PR #342 and duplicates in-flight work
   (and would "touch the seam" the card forbids touching; PR #342 already fixes the exact
   contract shapes my code would otherwise have to guess).

## Decision requested

- **Wait** (default, my recommendation): implement this card in a fresh session immediately
  after PR #342 merges, based on that merged main. The lsp-diagnostics side fits the card
  without seam changes: the verifier answers from the `ext/lsp-diagnostics/server` service
  it already registers, degrading to *unverified with reason* on slow/absent/crashed servers;
  refused outcomes carry diagnostics as `StructuredContent`.
- **Stack now**: allow this branch to merge `origin/wt/issue-336` first and build on top;
  the merge order then becomes part of this PR's review.

The same finding is on issue #340. No build or tests were run because no code changed.

Details: `BLOCKED-340.md` on this branch.

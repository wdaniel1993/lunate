# Check 6 evidence — Microsoft Agent Framework .NET release churn

Source: `https://api.github.com/repos/microsoft/agent-framework/releases` fetched
2026-10-04. .NET tags only (`dotnet-*`), newest first. "Breaking" counts lines
in the release body that contain `[BREAKING]`.

| Tag | Published | Breaking lines |
| --- | --- | --- |
| dotnet-1.23.0 | 2026-10-01 | 5 |
| dotnet-1.22.0 | 2026-09-18 | 5 |
| dotnet-1.21.0 | 2026-09-11 | 4 |
| dotnet-1.20.0 | 2026-08-31 | 0 |
| dotnet-1.19.0 | 2026-08-22 | 1 |
| dotnet-1.18.0 | 2026-08-18 | 1 |
| dotnet-1.17.0 | 2026-08-04 | 0 |
| dotnet-1.16.0 | 2026-07-30 | 0 |
| dotnet-1.15.0 | 2026-07-22 | 1 |
| dotnet-1.14.0 | 2026-07-21 | 8 |
| dotnet-1.13.0 | 2026-07-03 | 2 |
| dotnet-1.12.0 | 2026-07-02 | 3 |
| dotnet-1.11.1 | 2026-06-25 | 3 |
| dotnet-1.11.0 | 2026-06-23 | 6 |
| dotnet-1.10.0 | 2026-06-10 | 3 |

15 .NET releases in 115 days (roughly weekly). Breaking entries that touch the
harness surface S-1 would depend on:

- 1.23.0 — "Better support tool changes between runs"; "Enforce approval
  response binding consistently"; "Bump ... MEAI to 10.10.1".
- 1.22.0 — "Improve replay support with Approval Binding"; promote
  `AgentSessionStore` into `Agents.AI.Abstractions`.
- 1.21.0 — "Add `file_access_read_lines` and move the line-numbering contract
  onto `AgentFileStore`".
- 1.14.0 — "Graduate message injection out of experimental"; "Graduate todo and
  agent mode providers out of experimental"; "Harness: Switch FileAccess to
  opt-in"; "Graduate ToolApprovalAgent and add
  `ToolAutoApprovalRuleContext`".
- 1.13.0 — "Add file editing tools and align FileAccess/FileMemory store API".
- 1.10.0/1.11.0/1.11.1 — auto-approval rules added, then file-access tools made
  approval-required by default.

The `HarnessAgentOptions` we used still marks `MaxContextWindowTokens`,
`LoopEvaluators`, `FileMemoryStore`, `FileAccessStore` and others
`[Experimental]` (`MAAI001`), which the spike had to suppress. Experimental
surface means further breaking changes without a major version bump.

Conclusion: the harness is actively developed with a weekly cadence and an
above-average share of breaking changes in exactly the areas Lunate cares about
(approvals, session replay, file access). Adopting it means pinning a version
and budgeting an upgrade spike per bump.

## Addendum — harness-scoped split (ADR-0003 revision, item 4, 2026-10-04)

Method: same GitHub releases fetch (2026-10-04); a line counts as
harness-scoped if it contains `Harness` (case-insensitive) and is a .NET entry.
Script: `churn-split.py` (this directory).

| Tag | Published | [BREAKING] lines | harness-scoped |
| --- | --- | --- | --- |
| dotnet-1.10.0 | 2026-06-10 | 3 | 0 |
| dotnet-1.11.0 | 2026-06-23 | 6 | 0 (2 lines are Python) |
| dotnet-1.11.1 | 2026-06-25 | 3 | 0 |
| dotnet-1.12.0 | 2026-07-02 | 3 | 0 |
| dotnet-1.13.0 | 2026-07-03 | 2 | 0 |
| dotnet-1.14.0 | 2026-07-21 | 8 | 2 |
| dotnet-1.15.0 | 2026-07-22 | 1 | 0 |
| dotnet-1.16.0 | 2026-07-30 | 0 | 0 |
| dotnet-1.17.0 | 2026-08-04 | 0 | 0 |
| dotnet-1.18.0 | 2026-08-18 | 1 | 0 |
| dotnet-1.19.0 | 2026-08-22 | 1 | 0 |
| dotnet-1.20.0 | 2026-08-31 | 0 | 0 |
| dotnet-1.21.0 | 2026-09-11 | 4 | 0 |
| dotnet-1.22.0 | 2026-09-18 | 4 | 0 |
| dotnet-1.23.0 | 2026-10-01 | 5 | 0 |

Window totals: 41 breaking lines across 15 releases; 12 releases with at least
one; 2 harness-scoped (both dotnet-1.14.0 — "Harness: Switch FileAccess to
opt-in", "Graduate HarnessAgent"). The curated list above also counts approval
and file-access contract changes the harness consumes (1.21.0–1.23.0); those
lines do not say "Harness" but change harness-visible behaviour. Full-history
context: 108 breaking lines across 55 `dotnet-*` releases since 2025-10.

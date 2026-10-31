# decision-layer — session notes

## Card #409 — one settings block for all four consumers (2026-10-31)

Implements the `decisions` settings block on this unarchived change; the `agent-config`
delta in this folder is kept as the source of truth for the block.

Decisions taken while implementing:

- `decisions.profile` is the single decision-profile selector. The `usages.decision`
  key (provider-store release text) is refused as an unknown usage key from now on —
  nothing released uses it (no tag since v0.1.0 carries these keys), so no alias or
  migration. The internal `decision` usage label (attribution, `/cost`,
  `ResolveUsageModel("decision")`) stays.
- The escalation threshold applies at the approval seam: a `deny` recommendation
  escalates only when its probability reaches `decisions.approvalsThreshold`
  (default 0.5). Shadow consumption records regardless of the threshold.
- `AgentHarnessOptions` keeps its existing consumer-flag names (`DecisionMode`,
  `RoutingMode`, `RoutingProfiles`) and gains `ApprovalsThreshold` (default 0.5) and
  `DecideTool` (default false). `UsageModelClient.DecisionThreshold` is removed with
  the profile threshold.
- `decisions` stays user-scoped: project settings may not carry it.
- The `questions` consumer (display-only) is issue #344, not yet implemented; this
  card adds only its setting value (`off | suggest`).
- Follow-up to note when this change archives: provider-store's released text
  ("usages SHALL include … `decision`") needs its own catch-up delta since the
  selector moves into the `decisions` block.

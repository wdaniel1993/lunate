# Proposal: UI abstraction across modes (T-40)

## Why

User-facing output beyond approvals is ad-hoc per mode today: the interactive session maps events to live-area notices and a status footer, print mode prints errors to stderr, JSON serializes events, ACP logs what it cannot send. Nothing states what each mode must show, and extensions have no way to contribute UI at all — yet the plan requires a ui-showcase sample (T-50) with a renderer and a status widget that "degrade cleanly in print/json/ACP".

## What changes

- **`IUserInteraction`** (internal, Lunate.Coding): the mode-facing seam for ambient interaction — `Notify` (transient notices: retry, compaction, step limit, run failures, command feedback), `UpdateStatus` (the footer's data), and `RenderExtension` (a tool-result renderer block contributed by an extension). One implementation per mode: TUI (live area + footer + styled blocks), print (stderr lines, stdout stays pure), JSON (same as print; the event stream is the machine interface), ACP (host log — the protocol has no notice frame).
- **Extension UI contributions** (contract, API 1.3.0 → 1.4.0): a JSON-safe text-segment model (`TextSegment` + style enum), `IToolResultRenderer` (renders a specific tool's result as segments) and `IStatusWidget` (extra footer segments), registered through `IExtensionContext`. Failures omit the contribution and report through the host log — the hook failure policy, applied to UI.
- **A stated degradation matrix** with one interaction-matrix test per mode, plus contract round-trip and failure-degradation tests.
- Approvals keep their existing per-mode chain (`IToolApprover`): reviewed in T-21/T-27/T-28, unified behaviour already tested. Unifying them under `IUserInteraction` is considered and rejected (see design.md).

## Done when

The matrix behaviours are covered by one test file per mode, the contract additions round-trip through System.Text.Json, a failing widget/renderer degrades to omission without breaking the UI, and `scripts/verify.sh` is green.

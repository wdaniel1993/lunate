## Context

Card T-08; guide Layer 2, "Tools". T-07 established the event set; T-04 to T-06 the model layer. The loop (T-09) runs tools between model calls, so this change fixes the tool surface it compiles against. ADR-0003 already decides that Microsoft.Extensions.AI never invokes our tools.

## Goals / Non-Goals

**Goals:** exact contract shapes per the guide; a declaration adapter with byte-stable schemas and throwing invocation; a fail-fast registry; deterministic, surrogate-safe truncation; tests that lock all of it.

**Non-Goals:** tool implementations (T-12 to T-15), the loop (T-09), the approval policy (T-21), MCP wrapping (T-29), streaming tool output.

## Decisions

- **Contract shapes exactly as the guide's sketch**: `ToolRisk { ReadOnly, Write, Execute }`; `ITool` with `Name`, `Description`, `ParametersSchema` (`JsonElement`, hand-written — no reflection), `Risk`, `ExecuteAsync(JsonElement, ToolContext, CancellationToken)`; `ToolResult(Output, IsError, Details)` with `Details` UI-only and never sent to the model; `ToolContext(WorkingDirectory, Events)`. All in `Lunate.Agent`.
- **`ToolDeclaration : AIFunction` (internal)**: exposes `Name`, `Description` and `JsonSchema` from the wrapped tool; the schema is **re-parsed from the tool's raw JSON text** into a declaration-owned clone, so the model sees exactly the bytes the author wrote (no re-serialization drift, no lifetime coupling to a caller's `JsonDocument`). Every invocation path through Microsoft.Extensions.AI throws `NotSupportedException` naming ADR-0003 — the loop invokes `ITool.ExecuteAsync` directly. The wrapped `ITool` stays reachable internally for T-09.
- **`ToolRegistry` (public)**: insertion-ordered; `Add` rejects duplicate names (fail fast — silent replacement hides wiring bugs); `Find(string)` returns null for unknown names; `Declarations` exposes `IReadOnlyList<AIFunction>` for `ChatOptions`; `Tools` exposes the registered tools. MCP prefixing (T-29) prevents collisions upstream; the registry stays strict.
- **Truncation (`ToolOutput.Truncate`, public static)**: `DefaultLimit = 30_000`. Output at or under the limit is returned unchanged (same instance). Longer output keeps the first half and the last half of the content budget with a marker in between: `"\n\n... [N characters truncated] ...\n\n"` (N = omitted character count, formatted culture-invariantly). Cut points never split a UTF-16 surrogate pair (adjusted inward). The result is the content budget plus the marker; a limit smaller than the marker plus two characters degrades gracefully (marker plus whatever fits) and never throws. The loop applies this before appending the tool message (T-09); what the UI sees through `Details` (for example a diff) is the loop's decision and is not truncated.
- **No new packages**: `System.Text.Json` is in-box; the adapter builds on `Microsoft.Extensions.AI.Abstractions` 10.10.1 (already a direct reference).

## Risks / Trade-offs

- [MEAI's `AIFunction` surface changes in a future 10.x] → the adapter is internal and covered by tests (name/description/schema/invocation-throws); pin bumps are deliberate.
- [Byte-stable schema check could be too strict if MEAI normalizes] → we own the `JsonSchema` value (a clone we construct); the test asserts raw-text equality between the tool's schema and the declaration.
- [Middle truncation hides the middle of large outputs] → by design: both head and tail are usually the informative parts, the marker states the omitted count, and the UI can still surface full details where the loop chooses to pass them.
- [Registry strictness vs MCP names] → MCP tools are prefixed (`server__tool`) before registration (T-29); duplicates stay a bug, not a feature.

## Migration Plan

Not applicable — additive.

## Open Questions

- None blocking.

# ADR-0026: Roslyn built-in composition boundary

- Status: accepted — 2026-10-30 (maintainer sign-off with five conditions; change `roslyn-built-in-migration`)
- Date: 2026-10-30
- Relates to: ADR-0002 (layering), ADR-0006 (Roslyn placement), ADR-0014 (C# backend interface), ADR-0016 (workspace boundary), ADR-0017 (extension architecture), ADR-0021 (per-run workspaces), ADR-0023 (first-party extension tier)

## Context

The extension contract now provides host-supplied built-ins, per-workspace services, manifest-driven tool activation and tool aliases. The five C# tools still bypass that contract: `Lunate.Coding` composes them directly and references `Lunate.Roslyn`, while `lunate mcp roslyn` constructs them on a separate path. Removing the direct project reference therefore requires one typed integration boundary that both paths can use without changing the tool contract or loading Roslyn at startup.

## Decision

**Adapter ownership.** Place the Roslyn built-in adapter in a small, internal `Lunate.Roslyn.BuiltIn` project. It owns the `roslyn` manifest, the `IExtensionFactory` and the typed references to `Lunate.Roslyn`. It is host-supplied, not a user-installed or separately trusted extension, and does not create a collectible load context.

**Host composition.** `Lunate.Coding` supplies the adapter's descriptor through the existing built-in seam. Its reference to `Lunate.Roslyn.BuiltIn` exists for exactly one purpose: the host's list of built-in factories. `Lunate.Coding` must not consume any other adapter type. Keeping the factory in a separate adapter rather than `Lunate.Roslyn` keeps Roslyn types out of `Lunate.Coding`'s compile and load closure. The adapter is the only integration point that names Roslyn implementation types; `Lunate.Coding` references the adapter, not `Lunate.Roslyn`.

**Contract and lifecycle.** The adapter declares the existing five tools, the `roslyn` workspace service, the current root activation patterns and the stable `cs_*` aliases through the extension contract. Each tool acquires a per-workspace service lease for its call. The extension host remains the sole owner of activation and workspace-service lifecycle; service creation and Roslyn/MSBuild loading remain lazy.

**Frontends.** Chat and `lunate mcp roslyn` use the same host-supplied built-in and `ToolActivationEvaluator`. MCP does not construct a second set of Roslyn tools or maintain a separate copy of the root rule.

This keeps feature ownership with the adapter while preserving compile-time validation of the factory and the existing lazy, per-workspace Roslyn lifecycle. Reflection or string-based factory lookup would hide the dependency rather than remove it; placing Roslyn ownership in the generic extension host would invert the dependency direction.

## Constraints accepted with the sign-off (2026-10-30)

1. **Dependency closure.** `Lunate.Roslyn` and `Lunate.Roslyn.BuiltIn` reference only `Lunate.Extensibility.Abstractions`, Roslyn/MSBuild packages and the BCL. Neither project references `Lunate.Agent`, `Lunate.Coding` or `Lunate.Extensibility`. The layering test enforces this closure for both projects.
2. **Exactly one Coding-to-adapter edge.** `Lunate.Coding` references `Lunate.Roslyn.BuiltIn` only to supply the host's list of built-in factories; a test asserts it consumes no other adapter type. A separate adapter, rather than a factory in `Lunate.Roslyn`, keeps Roslyn types out of `Lunate.Coding`'s compile and load closure.
3. **Proven lazy loading.** A startup test asserts that no `Microsoft.CodeAnalysis.*` or `Microsoft.Build.*` assembly is loaded before the first C# tool use. Existing startup budgets remain unchanged.
4. **Compatibility.** Preserve the five `cs_*` tool names, exact results, activation patterns, two-workspace cap and session format. Run the existing golden and end-to-end tests unchanged as proof; never rewrite a golden to make a diff pass.
5. **Positioning and publication order.** Restore the `docs/guide.md` statement that Lunate is “built only on the public extension API” only after constraints 1–4 are green. Only after that statement is restored may issue #237 publish `Lunate.Extensibility.Abstractions` as stable.

## Consequences

- The built-in contract is exercised by the flagship C# feature, and the direct Coding-to-Roslyn project edge can be removed.
- The adapter adds one internal project and dependency edge; its assembly and backend must remain lazy so startup and single-file behavior do not regress.
- The existing five `cs_*` names, tool results, activation patterns, workspace cap and session format remain compatibility requirements and must be verified during migration.
- Implementation follows this accepted boundary and the five constraints above.

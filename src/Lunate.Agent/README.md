# Lunate.Agent (preview)

The Lunate agent harness: the loop that runs tools (ADR-0003 — no `FunctionInvokingChatClient`),
the tool registry, `AgentEvent` streaming, steering and cancel, the approval seam, compaction and
the session store. This package exists so a host can embed the harness with its own model access
(`IChatClient` from Microsoft.Extensions.AI) and its own frontend.

**PREVIEW — no API stability promise before 1.0.** The API may change in any release. It is not a
supported surface: the supported extension contract is [`Lunate.Extensibility.Abstractions`](https://www.nuget.org/packages/Lunate.Extensibility.Abstractions)
and [`Lunate.Contracts`](https://www.nuget.org/packages/Lunate.Contracts), both stable; the
supported tool is `lunate`.

## Installation

```sh
dotnet add package Lunate.Agent --version 0.2.0-preview.1
```

## What the package carries

Self-contained in `lib/`: the `Lunate.Agent` assembly and `Lunate.Ai.dll`, the engine beneath the
harness that is not published as its own package. The declared dependencies are the stable contract
pair — `Lunate.Contracts` 1.15.0 and, transitively, `Microsoft.Extensions.AI.Abstractions`.

## Versioning

`0.2.0-preview.<n>`; the `<n>` increments per republish and does **not** track `ExtensionApi` (the
contract version), nor the tool's version. Pin an exact version at install time — the tool's
package installer already refuses ranges, and a floating reference here carries the same advice.

## Documentation

`docs/guide.md` and the ADRs at `adr/` describe the loop and the boundaries. The
[repository](https://github.com/wdaniel1993/lunate) is the source of truth for behaviour.

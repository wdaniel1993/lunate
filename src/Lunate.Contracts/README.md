# Lunate.Contracts

The message and tool contract every Lunate extension and host shares: the `ITool` surface
(`ToolContext`, `ToolResult`, declarations and annotations), the complete closed set of `AgentEvent`
records, and the decision request/result types. `Lunate.Agent` implements the contract;
`Lunate.Extensibility.Abstractions` and every extension build on it.

## Installation

```sh
dotnet add package Lunate.Contracts --version 1.15.0
```

It has one external dependency, `Microsoft.Extensions.AI.Abstractions` — the message model the
contract is built on (ADR-0002), so MCP tools and telemetry middleware integrate without a mapping
layer.

## On stability

This is one half of the **stable extension contract** — versioned together with
`Lunate.Extensibility.Abstractions` under `ExtensionApi.Current`, **1.15.0** at publication, in
lockstep with it and independently of the tool's own version (the `lunate` tool moves on its own
release schedule).

- Within a major version the contract only grows. A minor bump adds surface; extensions compiled
  against the previous minor keep working, and an extension's `apiVersion` stays compatible.
- Surface is removed or reshaped only at a **major** bump of `ExtensionApi`. The package version
  moves with it, the load-time gate refuses an extension whose floor the host no longer meets
  (naming the required version, before any extension code runs), and the changelog names what the
  next major drops.

## One identity across the load boundary

The host loads this assembly once and every extension binds to that copy. A copy an extension's
directory or a locked package carries is never loaded — its `ITool` and `AgentEvent` types would be
a second identity, so the loader ignores it and warns once. Keep your references compile-only, the
way [the template](https://github.com/wdaniel1993/lunate/tree/main/templates/extension) does:
`PrivateAssets="all"` and `ExcludeAssets="runtime"`.

## Documentation

`docs/extensions/` — the [package reference](https://github.com/wdaniel1993/lunate/blob/main/docs/extensions/packages.md),
the [manifest](https://github.com/wdaniel1993/lunate/blob/main/docs/extensions/manifest.md) and the
[contract catalogue](https://github.com/wdaniel1993/lunate/blob/main/docs/spec/extensibility.md).

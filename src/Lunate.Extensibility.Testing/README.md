# Lunate.Extensibility.Testing (preview)

The extension test kit of Lunate: `ExtensionTestHost` (discovers and loads an extension through the
real loader, real load context, real trust flow and real harness with a scripted model), the
scripted approver and trust prompt, event recording, session assertions, and the wire conformance
runner for remote extensions. No test of an extension needs a live API.

**PREVIEW — no API stability promise before 1.0.** Not a supported surface; the supported contract
is [`Lunate.Extensibility.Abstractions`](https://www.nuget.org/packages/Lunate.Extensibility.Abstractions)
and [`Lunate.Contracts`](https://www.nuget.org/packages/Lunate.Contracts), both stable.

## Installation

```sh
dotnet add package Lunate.Extensibility.Testing --version 0.2.0-preview.1
```

## What the package carries

The kit assembly, plus every Lunate assembly a test run needs that is not a published package
itself: `Lunate.Ai`, `Lunate.Extensibility`, `Lunate.Extensibility.Remote`, `Lunate.Protocols`,
`Lunate.Tui`, `Lunate.Extensibility.Compile`, `Lunate.Extensibility.Restore`, `Lunate.Roslyn`,
`Lunate.Roslyn.BuiltIn` and the tool assembly (`lunate.dll`, which hosts the production MCP-over-
extension mapping). The declared dependencies are [`Lunate.Agent`](https://www.nuget.org/packages/Lunate.Agent)
(preview, same version) and the stable contract pair — the contract assemblies are *not* inside the
kit, they ride the dependency chain, so an extension built against the kit binds to the same host
contract copies a real load does.

The Microsoft compiler packages behind `Lunate.Roslyn(.BuiltIn)` are **not** shipped: a test that
registers the roslyn built-in resolves them through its own package references the way the tool
does.

## Versioning

`0.2.0-preview.<n>`, incremented per republish, decoupled from `ExtensionApi` and from the tool.

## Documentation

[Testing an extension](https://github.com/wdaniel1993/lunate/blob/main/docs/extensions/testing.md) —
the kit, fixtures, and the missing-primitive rule; the
[template](https://github.com/wdaniel1993/lunate/tree/main/templates/extension) shows the test
project this kit assumes.

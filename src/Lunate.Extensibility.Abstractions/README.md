# Lunate.Extensibility.Abstractions

The extension contract of Lunate: `IExtension` and `IExtensionFactory` (the entry point), the hook
catalogue, workspace and settings surfaces, and `ExtensionApi` — the version number an extension's
`apiVersion` is judged against when the host loads it.

## Installation

```sh
dotnet add package Lunate.Extensibility.Abstractions --version 1.15.0
```

Depends on [`Lunate.Contracts`](https://www.nuget.org/packages/Lunate.Contracts) (same version) and
`Microsoft.Extensions.AI.Abstractions`; nothing else — `Lunate.Roslyn` and `Lunate.Roslyn.BuiltIn`
reference exactly this assembly plus the BCL, which keeps the contract installable from the feed
alone (ADR-0026): the packaged-extension template and a single-file extension install it from
NuGet instead of referencing the repository.

## On stability

This is the other half of the **stable extension contract**, versioned in lockstep with
`Lunate.Contracts` under `ExtensionApi.Current` — **1.15.0** at publication, independent of the
tool's version.

- Within a major version the contract only grows. A minor bump adds surface.
- Removals and reshapes happen at the next **major** `ExtensionApi` bump together. The `^` range an
  extension declares tracks the major, so the load gate refuses precisely when removed surface is
  reachable, with the required version named before any extension code runs. The changelog of the
  pre-major release names what the next major drops; there is no silent deprecation.
- The `Lunate.Agent`-located types that moved to `Lunate.Contracts` stay reachable through type
  forwards **until the next major bump**, so an extension compiled against the old location keeps
  loading until then (ADR-0002 addendum).

## One identity across the load boundary

The host loads this assembly once and every extension binds to that copy; a copy an extension ships
is ignored at load with one warning. Keep the reference compile-only (`PrivateAssets="all"`,
`ExcludeAssets="runtime"`), the way the
[template](https://github.com/wdaniel1993/lunate/tree/main/templates/extension) does.

## Documentation

`docs/extensions/` — the [extension reference](https://github.com/wdaniel1993/lunate/blob/main/docs/extensions/README.md),
the [hook catalogue](https://github.com/wdaniel1993/lunate/blob/main/docs/spec/extensibility.md) and
the [sample](https://github.com/wdaniel1993/lunate/tree/hello-extensions) extensions under
`samples/extensions/`.

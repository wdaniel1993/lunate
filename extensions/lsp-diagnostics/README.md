# lsp-diagnostics

A first-party Lunate extension that shows the model what a language server thinks about the file it
just wrote: after every successful `write` or `edit` of a C# file, the tool result carries the file's
errors and warnings from its language server.

The model no longer guesses whether the code it wrote compiles or contains obvious problems — the
diagnostics arrive in the same tool result the next turn reads, before the model reasons about its
own change.

## What it looks like

A labeled section is appended to a `write`/`edit` result's output:

```text
lsp-diagnostics (csharp-ls): 2 diagnostics for src/Foo.cs
error 5:1 FIXME left on line 5 (fixme-check)
warning 9:1 TODO left on line 9 (todo-check)
```

The same capped list is attached as structured content on the result, so tooling that reads
structured data sees the diagnostics typed (`path` plus one entry per diagnostic) instead of parsing
the text.

## Behaviour

- **Applies to** `write` and `edit` calls whose `path` argument ends in `.cs` and resolves inside the
  worktree. Everything else — other tools, non-C# files, a missing path, no workspace — passes
  through untouched and silent.
- **Path resolution** uses the call's arguments, never text scraped from the output, and refuses
  paths that leave the worktree.
- **One server process** per session, started lazily on the first eligible `write`/`edit`, stopped at
  session end, and restarted on the next call after a crash.
- **Errors and warnings only** (LSP severity 1–2), capped at `maxDiagnostics` with a
  `+n more diagnostics` line. Nothing to report → nothing is attached.
- **Failure stays quiet**: a server that is missing, crashes, hangs or answers garbage never blocks or
  breaks the tool call — the original result stands unchanged, the failure is logged, and the next
  eligible call gets a fresh process. The whole round trip is bounded by `timeoutMs`.

## Install

```sh
lunate install Lunate.Extensions.LspDiagnostics
```

The package carries the extension's build output, `extension.json` and its `lunate.package.json`;
content lands in `~/.lunate/extensions/lsp-diagnostics/` with an install record beside it, and the
extension stays pending until you answer its first-load question — installing is not trusting.

Project use goes the same way through a repository-local feed. Without a feed, copy the extension's
output directory (`LspDiagnosticsExtension.dll`, `extension.json`, its dependencies) under
`<worktree>/.lunate/extensions/lsp-diagnostics/` and trust it on first load. Nothing runs until a
`write`/`edit` of a C# file succeeds.

## Configuration

Settings live in `~/.lunate/extensions-settings/lsp-diagnostics.json`; all are optional:

| Setting | Type | Default | Meaning |
| --- | --- | --- | --- |
| `serverCommand` | string | `csharp-ls` | The language server executable to run. |
| `serverArgs` | array | `[]` | Arguments passed to the language server. |
| `timeoutMs` | number | `2000` | The whole round-trip budget per `write`/`edit`, in milliseconds. |
| `maxDiagnostics` | number | `5` | The most errors and warnings one tool result carries. |

See `extension.json`'s `settingsSchema` for the enforced shape.

## Support

Officially supported for bugs and API compatibility at the `apiVersion` the manifest declares — see
[`extensions/`](../README.md) for what the tier promises. Report issues on the repository; the
background-service surface it runs on is documented in
[`docs/extensions/contributions/background-services.md`](../docs/extensions/contributions/background-services.md).

# Single-file extensions

A single-file extension is one `.cs` file in an extensions directory. There is no project, no
`.csproj` and **no sidecar manifest**: the header at the top of the file is the only declaration
source, so the claim "this file provides these tools and restores these packages" sits inside the
bytes the user trusts.

This page is the authoring reference for that header. The packaged format is
[manifest.md](manifest.md); the normative behaviour is the `extensions` capability in
[`openspec/specs/extensions/`](../../openspec/specs/extensions/spec.md).

## Where a file is discovered

| Scope | Directory | Trust |
| --- | --- | --- |
| User | `~/.lunate/extensions/*.cs` | Per file, one decision per file path |
| Project | `<worktree>/.lunate/extensions/*.cs` | Per file, one decision per file path |

Only `*.cs` files **directly under** either directory are candidates. A file nested inside a
packaged extension's directory belongs to that extension and is not a candidate. The directory name
is not the identity: `#:extension` declares the id.

Discovery reads every candidate, parses its header before anything else happens, and reports it in
`/extensions` by its declared id. A file with no `#:extension` is listed as
`invalid (no #:extension)` and is never compiled; a file whose header is refused is listed with the
reason on the same line. Nothing is restored, compiled or trusted during discovery.

> **Current state.** This version discovers and validates single files; it does not compile them
> yet. `/extensions` therefore reports `not-built` for a usable file — no build exists — and the
> restore, lock and compile arrive with the next change.

## The header is the leading block

Directives are read only from the **leading block**: the directives, comments and blank lines before
the first C# token. Everything from the first other line on is code.

```csharp
// acme-tools — a single-file extension
#:extension acme.tools@1.0.0
#:tool fetch_issue
#:package Octokit@14.0.0

using Octokit;

public sealed class AcmeTools : IExtensionFactory { /* … */ }
```

The rules:

- A `//` line comment, a `/* … */` block comment and a blank line keep the block open.
- A `#:` line **after** the first C# token is an error naming its line number, never a silently
  ignored directive. Move it to the top of the file.
- A `#` line that is not `#:` (for example `#nullable enable`) ends the header too: the header must
  be the first thing in the file, so no preprocessor state can move a declaration outside the bytes
  the user approved.
- An unrecognised `#:` directive inside the leading block is a warning and is ignored, so a file
  written for a later version still loads.
- `#:sdk`, `#:property` and `#:project` are refused by name. Lunate owns the project and the
  restore; a file declares what it provides and which packages it needs, and nothing else.

## Directives

| Directive | Manifest field | Value |
| --- | --- | --- |
| `#:extension <id>@<version>` | `id`, `version` | Required, exactly once. One id and one exact version (`1.0.0`, not a range). |
| `#:tool <name>` | `tools` | One tool name, as `IExtensionContext.RegisterTool` uses it. Repeatable. |
| `#:command <name>` | `commands` | One command name. Repeatable. |
| `#:hook <name>` | `hooks` | One hook name (`tool-calling`, `session-started`, …). Repeatable. |
| `#:service <name>` | `services` | One service name. Repeatable. |
| `#:capability <name>` | `capabilities` | One free-form capability. Repeatable. |
| `#:provider <id>` | `modelProviders[].id` | One model-provider id, `[a-z0-9-]+`. Repeatable. |
| `#:package <id>@<version or range>` | — (restore input) | A package to restore. Repeatable, one declaration per package. |
| `#:generators on` | — (build-time opt-in) | Opts in to the declared packages' analyzers and source generators. Off unless present. |

The six declaration directives mirror the manifest field for field, so a single file can register
anything a package can. The mirror is enforced by a test that reflects over the manifest's
declaration lists: a manifest field added without a directive fails the build rather than shipping
as a silent gap.

`#:extension`'s id is lowercase letters, digits, dots and hyphens, starting and ending with a letter
or digit (for example `acme.tools`). A colon is refused because it would make a command id
(`ext:<id>:<name>`) ambiguous.

### `#:package`

The syntax is the documented .NET 10 file-based-app syntax, so a file written for `dotnet run app.cs`
reads the same here:

```csharp
#:package Newtonsoft.Json@13.0.3
#:package Octokit@[14.0.0,15.0.0)
#:package Humanizer@[2.14.1]
#:package Polly@[8.0.0,)
#:package Serilog@(3.0.0,4.0.0]
```

A bare version or a bracketed range is accepted: `[1.0.0]` is exact, `[1.0.0,2.0.0)` mixes an
inclusive lower bound with an exclusive upper bound, `(1.0.0,)` is a lower bound only. A floating
version (`1.*`) and a missing version are refused with the line number, because the version the user
approves must be the version that is restored.

### `#:generators on`

Compiling against a package's compile-time assemblies does not need its analyzers or source
generators. Those execute at build time, so they are off unless the header opts in. The opt-in is
part of the trust digest: toggling it re-asks, and the prompt only mentions build-time code when it
applies. A package that ships generators while the file did not opt in is named by `/extensions` as
`generators skipped: <package>` once compiling exists.

## Trust

A single-file extension is trusted before it is restored or compiled. The **first** trust covers:

```text
sha256("lunate-extension-v1" | id | each declared package as written | source bytes)
```

The tag names the format, so a later digest that also covers the lock file cannot be mistaken for
this one. Because the header is part of the source bytes, adding a `#:tool` line changes the digest
exactly like changing the code does — the declaration and the code are one approved unit.

The decision is stored in `~/.lunate/trust.json` under a `singleFile` section, keyed by the file's
full path:

```json
{
    "singleFile": {
        "/Users/you/.lunate/extensions/acme.cs": "608b4762…"
    }
}
```

The record is per file and is never inherited from another file, another path or a packaged
extension. When the computed digest differs from the recorded one the file is `stale`, and the stale
check stops before anything is built — a changed file makes no feed request.

## What `/extensions` shows

| Output | Meaning |
| --- | --- |
| `<id> <version> (<scope>, single file) not-built, tools …` | Usable; nothing has built it. |
| `<file name> (<scope>, single file) invalid (no #:extension)` | The header declares no id. Never compiled. |
| `<id> <version> (<scope>, single file) invalid (line N: …)` | The header refuses the file; the reason names the line. |
| `<id> <version> (<scope>, single file) stale — …` | Trusted before, changed since. It must be trusted again before it is built. |
| `<id> <version> (<scope>, single file) failed (trust) — …` | Trust could not be decided or verified; the reason says which. |
| `…, declared, not provided: <names>` | The header declares a name the code never registered. A warning, not a refusal. |

A registration the header does not declare refuses the load and names the registration and the
missing directive, because the header is what the user approved. The reverse — a declared name
nothing provided — is a warning, and the name is shown as `declared, not provided`.

Single-file entries are listed after the packaged ones, problems first, and `lunate extensions
--json` carries the same data with `singleFile` (the file name), `state`, `reason` and, on a failed
trust step, `failure: "trust"`.

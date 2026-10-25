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

> **Current state.** This version discovers, validates, restores and locks single files; it does not
> compile them yet. `/extensions` therefore reports `not-built` for a usable file — no build exists —
> and the compile arrives with the next change.

## The header is the leading block

Directives are read only from the **leading block**: the directives, comments and blank lines before
the first C# token. Everything from the first other line on is code.

```csharp
// acme-tools — a single-file extension
#:extension acme-tools@1.0.0
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

`#:extension`'s id follows the packaged manifest's rule exactly — lowercase letters, digits and
hyphens (`[a-z0-9-]+`, for example `acme-tools`) — so a single file stays promotable to a package
without renaming itself. A dot is refused because the id prefixes every tool name
(`<id>__<tool>`) and provider APIs accept only `[A-Za-z0-9_-]` there; a colon is refused because it
would make a command id (`ext:<id>:<name>`) ambiguous.

A `#:tool` name must match `[A-Za-z0-9_-]`, and the composed `<id>__<tool>` must fit the provider's
64-character limit. A pair that cannot fit is refused at discovery, naming the composed name and
both halves, so the author knows whether to shorten the tool or the id.

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
version is accepted too — `*` for the latest stable, `14.*`, `1.2.*`, or `1.2.3-*` for a prerelease
wildcard — and resolves to the highest published match **at lock time**. The lock then records the
exact versions that were resolved, and the trust digest covers that graph, so what you approve is
still a set of exact packages; the floating part only decides what "latest" meant on the day of the
restore. A missing version is refused with the line number.

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

Once a restore has written a lock, the digest also covers the lock's **graph** — the packages it
resolved, with their exact versions, hashes and assets — under its own tag:

```text
sha256("lunate-extension-v2" | id | each declared package as written | source bytes | the lock's graph)
```

The tag names the format, so the two layouts can never be mistaken for one another, and a file that
has never been restored keeps the decision it already has. Because the header is part of the source
bytes, adding a `#:tool` line changes the digest exactly like changing the code does — the
declaration and the code are one approved unit. Changing a resolved package (a hand-edited lock, a
re-resolved range the user approves) is part of the same decision.

What the digest deliberately does **not** cover is the lock's toolset half — your runtime, its
reference pack and their hashes. A .NET patch moves those on your machine without moving anything
you approved, so it rebuilds quietly instead of asking again.

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

## The lock file

A restore writes `extension.lock.json` into the extension's cache entry, for a project extension
exactly like a user one:

```text
~/.lunate/cache/extensions/<id>/extension.lock.json
```

It is never written next to your `.cs` file: a single-file extension stays one file.

The lock has two halves with two different jobs:

| Half | What it records | What a change does |
| --- | --- | --- |
| `graph` | the feeds used, and per package the exact version, the nupkg's SHA-512, the resolved compile/runtime/native assets and the dependency edges | it is part of the trust digest, so a change is a change to what you approved and asks before it is written |
| `toolset` | the target framework, the RID, the exact runtime, and the reference pack's version and nupkg hash (when the packages folder still holds it) | it is not part of the digest; it is integrity-checked and triggers a silent rebuild |

**A lock is produced by a restore, on the machine that runs it.** It is not a file you commit, and
nothing copies it from machine to machine: a teammate's first run restores and trusts on their own
machine, and their lock names their runtime and their reference pack. Two machines still get the
same packages, because the resolution algorithm is deterministic — it picks the lowest version that
satisfies every declared range, and that lowest applicable version does not move when newer versions
are published. The lock records that answer so a later run reads a file instead of asking a feed; it
is not what makes the answer stable.

A hand edit to either half is visible. Editing the graph changes the bytes the trust digest covers,
so the extension is `stale` and asks again. Editing the toolset block leaves its integrity hash
behind, so the lock is refused and the next restore rebuilds the block — no question, because the
resolution did not change.

## What `/extensions` shows

| Output | Meaning |
| --- | --- |
| `<id> <version> (<scope>, single file) not-built, tools …` | Usable; nothing has built it. |
| `… not-built, …, resolved: <id> <version>, …` | Restored: the graph the lock records, which is what a compile would use. |
| `… not-built, … — <reason>` | The lock on disk was refused (its toolset block no longer answers for itself) and will be rebuilt. |
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

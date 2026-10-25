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

> **Current state.** This version discovers, validates, trusts, restores, locks and compiles single
> files, and caches the build. `/extensions` reports `built` once an assembly exists for exactly these
> bytes, this lock, this contract version and this toolset; `not-built` means the file is usable but
> nothing has compiled it yet. Reload and unload are the next change.

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
generators. Those execute at build time — inside Lunate's own process — so they are off unless the
header opts in. With the directive the compiler loads the locked packages' `analyzers/` assemblies,
runs their generators and compiles the trees they emit, and runs their analyzers; an analyzer that
reports an error fails the compile with that error as the first diagnostic, exactly as it would in a
project build.

The opt-in is part of the trust digest — it is a header line, so the source bytes that are hashed
contain it. Toggling it makes a trusted file `stale` and re-asks; the prompt only mentions build-time
code when the directive is there. A package that ships generators while the file did not opt in is
named by `/extensions` as `generators skipped: <package>`, by `lunate extensions build` on stdout
after the built line, and as `generatorsSkipped` in either `--json` — so the omission is visible
rather than silent. Before a restore there is no resolved graph, so nothing is named yet.

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

**What the lock is for.** It is the compile's whole input: every reference the compiler uses comes
from the packages and reference pack the lock names, so building never re-resolves a graph and never
asks a feed. It is also what a later run reads instead of querying a server for versions, and what
`/extensions` shows as `resolved: …` — the exact set of packages you are running.

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

## What gets restored, and where

Two kinds of package are restored, and both go to the same place: NuGet's **global packages folder**.

| What | Where |
| --- | --- |
| the packages your header declares, and every dependency the resolution pulled in | `NUGET_PACKAGES` when that variable is set, otherwise `~/.nuget/packages` |
| `Microsoft.NETCore.App.Ref` — the reference assemblies the compile needs for your runtime | the same folder, restored on the first compile and reused afterwards |

That folder is NuGet's, not Lunate's: it is content-addressed, immutable, and the one your `dotnet`
commands already populate, so a package you have restored before costs nothing here. **Nothing is
copied into `~/.lunate`.** What Lunate owns is the compiled assembly, the lock and the pointer, and
those live in the extension's cache entry (see [The build cache](#the-build-cache)).

`~/.lunate` is the state root. Set `LUNATE_HOME` to an absolute path to move the whole root — a
sandbox, a portable install, or a second profile. A relative path is refused when it is read rather
than quietly ignored, because silently falling back to the real profile is how a sandboxed run ends
up writing to your own state.

Lunate reads `LUNATE_HOME` from the process environment and nowhere else: no `.env` file, no
`settings.json`, no `AGENTS.md` and no project file can supply it. A repository that could choose the
store could choose the directory its own trust records live in.

### What follows the root, and what does not

| State | Where it lives | Why |
| --- | --- | --- |
| `<root>/trust.json` and the per-extension cache entry (`cache/extensions/<id>/` — lock, builds, pointer) | the root | it is the state the guard below is about |
| `<root>/extensions/` (user extensions) | the root | yours, not the project's |
| `<root>/sessions/`, `auth.json`, `settings.json`, `history`, `exports/`, `models.json`, `models.catalog.json`, `recordings/` | the root | all resolve through one rule |
| `<root>/skills/` (Lunate's own user skill scope) | the root | it was `<profile>/.lunate/skills` and did not follow `LUNATE_HOME`; fixed in this phase |
| `~/.agents/skills` and `~/.agents/mcp.json` | the real home | a convention shared with other agents; not Lunate state |
| restored packages | `NUGET_PACKAGES` when set, else `~/.nuget/packages` | content-addressed package storage the SDK shares, not Lunate state |

There are no log files today: extension logging goes to the terminal through `IExtensionLog`, and a
session's transcript is the session file under the root. Nothing writes a log of its own.

If `LUNATE_HOME` resolves inside the workspace a session starts in — or the repository that worktree
belongs to — Lunate refuses it: `LUNATE_HOME points inside the workspace; trust records there could
be written by the repository`. The refusal runs wherever a workspace exists: starting an interactive
session, print mode, ACP, `lunate trust`, `lunate extensions build` and `lunate cache prune`. A store
equal to the root counts as inside; a sibling directory does not. Containment is compared on real
paths (symlinks resolved), so `/var/…` and `/private/var/…` cannot disagree on macOS.

When `LUNATE_HOME` is set, both `lunate --version` and the interactive welcome header print the active
home, in one spelling: `home /tmp/state (LUNATE_HOME)`. A redirected or sandboxed run is never
invisible.

Consequences worth knowing:

- A machine with a warm cache needs no feed at all — the lock names the exact versions, so a compile
  reads files instead of asking a server.
- A cold cache needs a reachable feed: the `nuget.config` chain above the extension (machine, user,
  project), and `NUGET_PACKAGES` if you keep your packages somewhere other than the default.
- The reference pack is the largest thing a first build downloads: about 49 MiB for one .NET 10
  version, restored once per machine and shared with the SDK. That is why Lunate does not bundle it —
  the published binary would carry every byte of it.
- Lunate never edits your `.cs` file. Restoring, locking, compiling and pruning all write under
  `~/.lunate` only.

## Building

`lunate extensions build <id|path> [--json]` compiles one file now. It is the same service
`/extensions reload` uses, so there is one order everywhere: trust, then a lock that answers for
those bytes, then the cache, then the compiler. It never asks a trust question — trust is your
decision, so a file whose bytes you have not approved reports `failed (trust)` and tells you to
approve it in an interactive session first.

```console
$ lunate extensions build acme-tools
acme-tools 1.0.0 built 7c2b0a9f3d51e8c4 — /Users/you/.lunate/cache/extensions/acme-tools/builds/7c2b0a9f3d51e8c4/acme-tools.dll
```

A compile failure names the first diagnostic in Roslyn's own form and exits non-zero, and it leaves
the cache as it was — no half-built directory, and the previous build (if any) is still the one the
entry points at:

```console
$ lunate extensions build acme-tools
lunate: acme-tools failed (compile) — /Users/you/.lunate/extensions/acme.cs(12,9): error CS0103: The name 'Issue' does not exist in the current context
```

### Reading a compile failure

Three things are worth knowing about that line:

- **It is the first error by position**, not the first error the compiler happened to produce, so the
  file, line and column are the place to look first. The shape is Roslyn's own — `path(line,column):
  error CSxxxx: message` — which is the same text a compiler or an IDE would print, so a search for
  the id and the message finds the language documentation.
- **It is also what `/extensions` shows** on the row (`failed (compile) — …`), and with `--json` the
  same failure carries a `diagnostic` object (`{file, line, column, id, message}`) beside the reason,
  so a script reads fields instead of parsing the sentence.
- **Nothing was written.** A failed compile leaves the cache exactly as it was: no build directory, no
  pointer, no trimmed history. If the file had a working build before the edit, that build is still
  the one the entry points at — editing a file into a compile error never costs you the last good
  assembly.

Two other failures look similar and are not compile failures: `failed (trust)` means the bytes (or
the lock's graph) are not the ones you approved — nothing is restored or compiled, and the CLI tells
you to approve the file in an interactive session because trust is your decision, never the CLI's;
`failed (restore)` means a package could not be resolved, with the package and the feed in the reason.

### The build cache

```text
~/.lunate/cache/extensions/<id>/
  extension.lock.json
  builds.json                  the current build and the ones kept, newest first
  builds/<key>/
    <id>.dll                   the compiled assembly
    build.json                 what it was built from and what a load needs
```

The key is `sha256(sourceHash | lockHash | apiVersion | TFM | RID)`, lowercase hex truncated to its
first 16 characters. `lockHash` covers the whole lock file, both halves, so a resolution change and
a toolset change each produce a new key — and because the API version is in it too, a Lunate update
rebuilds extensions on first use instead of loading an assembly compiled against another contract.
The same source, lock and toolset always compile to byte-identical bytes, so a rebuild is a cache
hit rather than a second artifact.

A successful compile keeps the **last two** builds and deletes older ones: editing, failing and
fixing never grows the cache past two assemblies. `builds.json` says which two and which one is
current, so "the last two" is a written order, not a guess from timestamps.

### After a Lunate update

The first `/extensions` (or `extensions build`) after you update Lunate **rebuilds every single-file
extension on first use**. That is deliberate, not a bug: the build key includes the extension API
version, the target framework and the RID, so an assembly compiled against another contract can never
be reused by a newer host. The rebuild is a local compile against the lock you already approved — no
trust question, no new resolution, and a warm packages folder means no feed either — so it costs the
compile, not the restore.

`lunate cache prune` removes the entries nothing points at — run it after deleting an extension's
`.cs` file; that is how removing an extension deletes its builds. It only touches
`~/.lunate/cache/extensions/` and never the shared NuGet packages folder, which is content-addressed
and your SDK's too. It sees the extensions this machine can discover from the current directory: your
user extensions and this project's.

## What `/extensions` shows

| Output | Meaning |
| --- | --- |
| `<id> <version> (<scope>, single file) not-built, tools …` | Usable; nothing has built it. |
| `… built, tools …` | An assembly exists for exactly these bytes, this lock and this toolset. |
| `… not-built, …, resolved: <id> <version>, …` | Restored: the graph the lock records, which is what a compile would use. |
| `…, generators skipped: <package>` | A locked package ships analyzers or generators and the header did not opt in (`#:generators on`), so the compile leaves them out. |
| `… not-built, … — <reason>` | The lock on disk was refused (its toolset block no longer answers for itself) and will be rebuilt. |
| `<file name> (<scope>, single file) invalid (no #:extension)` | The header declares no id. Never compiled. |
| `<id> <version> (<scope>, single file) invalid (line N: …)` | The header refuses the file; the reason names the line. |
| `<id> <version> (<scope>, single file) stale — …` | Trusted before, changed since. It must be trusted again before it is built. |
| `<id> <version> (<scope>, single file) failed (trust) — …` | Trust could not be decided or verified; the reason says which. |
| `<id> <version> (<scope>, single file) failed (compile) — <file>(line,col): error CSxxxx: …` | The compile failed at that position; nothing was written. |
| `…, declared, not provided: <names>` | The header declares a name the code never registered. A warning, not a refusal. |

A registration the header does not declare refuses the load and names the registration and the
missing directive, because the header is what the user approved. The reverse — a declared name
nothing provided — is a warning, and the name is shown as `declared, not provided`.

Single-file entries are listed after the packaged ones, problems first and built entries last, and
`lunate extensions --json` carries the same data with `singleFile` (the file name), `state`,
`reason`, `build` (the key, once one exists), `resolved` (the locked graph), `generatorsSkipped`
(the packages whose build-time code the compile leaves out) and `failure` (`trust`, `restore` or
`compile`) on a failed step.

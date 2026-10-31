# Lifecycle and trust

An extension is discovered from a directory and lives for the session. An in-process extension
loads into its own collectible `AssemblyLoadContext`; a remote extension's child process starts on
first use. Project-scoped extensions run repository code, so they need an explicit trust decision
before they load.

## Discovery

Two directories are scanned at session start:

| Scope | Directory |
| --- | --- |
| Global | `~/.lunate/extensions/` |
| Project | `<worktree>/.lunate/extensions/` |

Each immediate subdirectory is one extension. The directory name is not the identity — the `id` in
its `extension.json` is. A directory without a manifest is a discovery error; so is a manifest that
does not parse or an `apiVersion` that is incompatible with the running host.

Directories are scanned in ordinal order, global before project. Hidden directories (a name
starting with `.`) are skipped. If two directories declare the same id, the first one wins (global
scope first) and the second is a discovery error naming both paths.

Discovery reads manifests only. No extension code runs during discovery.

## Loading

The host loads every discovered extension when the session composes. In the interactive session
that happens after the welcome header and before the first prompt; in every mode it is before the
first run. This is the first use of the extension host, not process startup and not the first tool
call. For an in-process descriptor the host:

1. evaluates project trust, for project scope only;
2. loads the entry assembly into a collectible `AssemblyLoadContext`;
3. finds the one public `IExtensionFactory` and runs `Create`;
4. cross-checks the manifest's `tools` list against the registrations;
5. registers manifest-declared model providers;
6. starts the extension's background services.

A remote descriptor takes the same trust decision, builds host-side proxies instead of loading an
assembly, and starts its child lazily.

After the load loop, the host fires `SessionStarted` once, so handlers observe the full set of
extensions that loaded. An extension loaded later would not observe it; in this version there is
no later in-process load.

`AssemblyLoadContext` isolates dependencies, not permissions. `Lunate.Contracts`,
`Lunate.Extensibility.Abstractions`, `Lunate.Agent`, `Microsoft.Extensions.AI.Abstractions` and
`System.Text.Json` are shared with the default context; every other dependency loads from the
extension's own directory or, for a single-file extension, from the exact package asset its build
recorded. A copy of a shared assembly shipped beside the entry assembly — or inside a package a
single-file extension locked — is ignored, and the load reports it once, naming the package, so the
copy is never a second identity. In-process extensions are fully trusted.

Long-lived resources start later: a registered background service starts as the extension loads,
a remote extension's process starts on first use (or in the background after the interactive UI is
ready), and a provider's client is created at its first use. The factory itself must only register.

### Lazy loading

Startup reads manifests only. Assembly loading is deferred from process start to session
composition, so the start-up path never touches extension code. A remote extension's child process
and a provider's client are lazier still: they start on first use (a remote extension may instead
start in the background after the interactive UI is ready).

There is no per-extension lazy assembly load in this version: every discovered in-process
extension loads when the session composes, whether or not a run uses it. There is also no file
watcher that reloads an in-process extension: a packaged extension's changed files take effect in a
new session, and a single-file extension is reloaded explicitly with `/extensions reload <id>`
(which trusts, builds and swaps, in that order — see [single-file.md](single-file.md)).

## Project trust

A global extension is trusted by installation. A project extension runs code from the repository,
so the host asks once per repository before loading it.

- **Identity.** Trust is keyed by repository identity (the git common directory, or the worktree
  root outside a repository) and by worktree path, against a content hash of the extension
  directory.
- **Content hash.** Every file under the extension directory, recursively, contributes its relative
  path and bytes in sorted order. Editing any file changes the hash. The bytes are normalized to LF
  before hashing, so the same source checked out with CRLF and with LF has one hash — a pure
  line-ending change does not re-prompt.
- **First decision.** The interactive prompt names the extension id, version and directory (and
  says that it runs with your user privileges for a remote extension). Answering yes records the
  decision in `~/.lunate/trust.json`.
- **New worktree.** A worktree of an already-trusted repository does not prompt again; the decision
  is recorded for the new worktree automatically.
- **Changed files.** If the recorded hash no longer matches, the prompt appears again and says the
  files changed since the repository trusted the extension. A changed extension stays untrusted
  until approved.
- **Non-interactive modes.** Print and ACP sessions never auto-approve repository code. They refuse
  the load and log: `project extension '<id>' was not trusted; run an interactive session or
  'lunate trust' to approve it.` The extension appears as `trust-denied` in `/extensions`.
- **Denial.** A denied extension is not loaded and not recorded. `/extensions` reads
  `trust-denied`; the session continues without it.
- **The record is not a boundary.** `trust.json` records what was asked and answered; it does not
  stop a process that can write the file. The approval panel is the boundary in default mode, and
  `--yolo` removes it. Nothing here is an OS sandbox: 0.1.0 runs commands with your user's rights on
  every platform, Windows included; the guard profile is scheduled for 0.2.

### The `ProjectTrust` hook

A global extension can register an `IProjectTrustHandler` to **veto** a project extension before the prompt. Only global extensions that are already loaded participate. The first `Deny` wins; a handler exception or timeout denies fail-safe. `Allow` approves nothing: a handler cannot answer the question on the user's behalf, so the prompt still runs and still records the decision — the same rule that keeps an installed package pending (see [packages.md](packages.md)). A policy extension can therefore block what it does not recognise, never grant what the person was not asked.

### `lunate trust`

The command inspects and records this repository's pending decisions without loading code,
dispatching hooks or starting servers:

```bash
lunate trust                       # current records and pending decisions; exit 3 when something is pending
lunate trust --json                # the same as one JSON document
lunate trust --yes                 # record every pending decision (needs a terminal)
lunate trust --yes --digest <sha>  # record only when the pending digest matches (the CI path)
lunate trust --clear               # remove this repository's records
```

Exit codes: `0` success, `1` unexpected failure, `2` usage, `3` pending decisions found, `4` the
pinned digest does not match. `--yes` records exactly what it prints: use it for checkouts you
control, never for an untrusted pull request. A bare `--yes` needs a terminal at stdin; the
non-interactive path is `--yes --digest <sha>`, which names the exact decision being approved. Both
still refuse inside a Lunate session (`LUNATE_SESSION`), and that marker is a visibility guard rather
than a lock: a command running with your shell can remove it, and under `--yolo` no panel is shown.
In an interactive session, `/trust` lists the records.

A project extension's trust record is independent of project skill trust and of `mcp.json` trust;
those have their own sections in the same store.

## Unload

Unloading an extension:

- removes its tools and cancels in-flight calls into them (a cancelled call settles as an error
  result saying the extension was unloaded);
- removes its hook handlers, stops and forgets its background services, drops its named services;
- removes its model providers, middleware, virtual models and declared providers;
- removes its MCP server registrations, renderers, widgets, skill sources and file-change
  subscriptions;
- unloads the collectible `AssemblyLoadContext` (an in-process extension) or shuts the child down
  (a remote extension). State does not survive: a later load creates a new factory instance in a
  new context.

If tools were registered, unload fires `ToolsChanged` with the remaining set. The register-only
`lunate extensions` inspection unloads every extension it loaded; a session ends by stopping its
services and remote children, and the process exit releases the load contexts.
`/extensions restart <id>` exists only for remote extensions; an in-process extension is not
restarted in place.

## Incompatible `apiVersion`

`ExtensionApi.Current` is `1.12.0`. A manifest whose range is syntactically valid but does not
match is refused at discovery:

```text
extension 'future' requires Lunate ≥ 2.0.0 but this is Lunate 1.12.0; it was not loaded.
```

The extension is not listed by `/extensions` and nothing loads. A malformed range is a parse error
instead. See [manifest.md](manifest.md#api-version).

## Disabling extensions

`lunate --no-extensions` starts any mode without the extension host: no extension tools, hooks,
services, renderers, widgets, model providers or extension MCP servers. Configured `mcp.json`
servers still run. `/extensions` reads `extensions disabled (--no-extensions)`.

## See also

- The normative loading and trust contract: [`docs/spec/extensibility.md`](../spec/extensibility.md)
- The full manifest field reference: [manifest.md](manifest.md)
- Run and inspect failures: [troubleshooting.md](troubleshooting.md)
- Remote lifecycle: [remote extensions](../extensions-remote.md)

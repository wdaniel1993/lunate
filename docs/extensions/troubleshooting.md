# Troubleshooting

Start with `/extensions` in an interactive session, or `lunate extensions --json` outside one. Both
read the same discovery and load results. The session log has the full message; `/extensions`
shows a summary.

## Read the state

| Output | Meaning |
| --- | --- |
| `extensions: none configured` | Nothing was discovered. The directories may be missing, empty, or every candidate failed discovery (which is logged, not listed). |
| `extensions disabled (--no-extensions)` | The extension host was not composed for this run. |
| `<id> <version> (<scope>) loaded in <ms> ms, tools …` | Loaded. A field differing from the manifest reads `(N declared)`. |
| `… (slow)` | The load took longer than 500 ms. Investigate the factory. |
| `…, hook <Hook> max <ms> ms, <slow>/<calls> slow` | A hook crossed the 500 ms slow threshold. The full timing is in `--json` as `hookTimings`. |
| `…, start <ms> ms` | A remote extension's most recent start and handshake. |
| `<id> <version> (<scope>) trust-denied, tools …` | A project extension was not trusted for this repository. |
| `<id> <version> (<scope>) failed: <first sentence> — see the log, tools …` | A load failed. The log has the rest. The `— see the log` pointer appears when the message was truncated. |
| `<id> — remote failed: <reason>` | A remote process crashed or exited. |
| `<id> — remote refused: <reason>` | The remote handshake refused the extension (version mismatch or an undeclared surface). |
| `<id> <version> (<scope>, single file) not-built, tools …` | A single `.cs` file with a usable header; nothing has built it. |
| `<file name> (<scope>, single file) invalid (no #:extension)` | A `.cs` file with no header; it is never compiled. |
| `<id> <version> (<scope>, single file) stale — …` | The file changed since it was trusted; see [single-file.md](single-file.md#trust). |
| `<id> <version> (<scope>, single file) failed (trust) — …` | The trust decision could not be read or verified. |

The view orders failed entries first, then trust-denied, then loaded. `lunate extensions` exits `1`
when any extension failed to load, `0` otherwise; a denied extension is expected without trust and
still exits `0`. Usage errors exit `2`.

A discovery error never appears as a descriptor: a missing manifest, invalid JSON, a bad field and
an incompatible `apiVersion` leave the extension out of the list. A duplicate id leaves out the
later directory; the first one is listed. The log line is the only place to see why.

## It does not appear at all

Check, in this order:

1. The directory is an immediate child of `~/.lunate/extensions/` or `.lunate/extensions/` and
   contains `extension.json`. Nested directories are not scanned; a hidden directory is skipped.
2. The manifest parses. A missing required field, a wrong type and a malformed `apiVersion` range
   are parse errors.
3. The `apiVersion` is compatible with `ExtensionApi.Current` (`1.10.0`).
4. No other directory declares the same id. Global is scanned before project, so a project
   duplicate loses.

The messages:

```text
extension directory '/…/extensions/hello' has no extension.json manifest.
extension 'hello' is declared more than once: '/…/.lunate/extensions/hello' and '/repo/.lunate/extensions/hello'.
extension 'future' requires apiVersion '2.0.0' but the current API version is 1.10.0.
extension.json: field 'id' must match [a-z0-9-]+ but is 'Hello'.
```

## `trust-denied`

The extension is project-scoped and this repository (or worktree) has not trusted its current
content. Approve it interactively, or record it ahead of time:

```bash
lunate trust
lunate trust --yes
```

Print and ACP modes never auto-approve. Their log says:

```text
project extension 'hello' was not trusted; run an interactive session or 'lunate trust' to approve it.
```

The decision is keyed by repository identity and a content hash of the extension directory, so
editing any file asks again. A new worktree of an already-trusted repository does not ask. See
[lifecycle-and-trust.md](lifecycle-and-trust.md#project-trust).

## `failed`

The common load failures, with the fix:

| Message | Fix |
| --- | --- |
| `entry assembly '<name>' was not found in '<directory>'` | Build and copy the assembly, or correct `entryAssembly`. |
| `missing dependency '<name>'. Place the dependency in '<directory>'` | Copy the dependency next to the assembly. Shipping the contract assemblies is unnecessary; they are shared from the host. |
| `has N public IExtensionFactory implementations; declare exactly one` | Keep one public, non-abstract factory in the entry assembly. |
| `IExtensionFactory '<type>' needs a public parameterless constructor` | Add one, or remove the non-default constructors. |
| `tool '<name>' is registered by code but not declared in the manifest 'tools' list` | Add the local name to `tools`, or remove the registration. |
| `setting '<name>' is required` / `setting '<name>' must be <type>` | Fix `~/.lunate/extensions-settings/<id>.json`; see [settings and secrets](contributions/settings-and-secrets.md). |
| `model provider id '<id>' is already registered by extension '<other>'` | Rename the provider or remove the duplicate. |
| `model id '<id>' is a catalog model id; extension '<id>' cannot shadow it` | Rename the served model id. |

`/extensions` shows the first sentence; the log has the entire message, including the path.

## The tool is missing

If the extension loaded but the tool is not offered, the manifest and the code disagree:

- **Registered but undeclared** refuses the load, so the extension would be `failed`, not loaded.
- **Declared but unregistered** logs `manifest declares tool '<name>' but no registration matched;
  the entry is dropped from the offered set.` `/extensions` reads `tools 0 (1 declared)`.
- **A duplicate model-facing name** refused the load, naming the extension and the tool. This
  includes the same extension registering the same local name twice; two extensions cannot share
  an id because discovery refuses that first.
- **Exposure** may keep the tool from the model or from nested calls: check `Exposure`.
  `ModelOnly` is declared but refuses nested calls; `Programmatic`, `Deferred` and `Hidden` are not
  declared to the model.

A tool call that fails at runtime returns a teaching error, not a crash:

```text
Tool 'hello__greet' failed: <message>. Fix the call or try a different approach.
Denied: 'hello__greet' was blocked: <reason>. Explain why the call is needed and ask before retrying.
```

## Duplicate ids and names

| Duplicate | Result |
| --- | --- |
| Extension id | Discovery error naming both directories; the first in scan order wins. |
| Tool model-facing name | Load refused, naming both extensions. |
| Named service | Load refused, naming both extensions. |
| Model provider id or served model id | Load refused, naming both extensions. |
| Virtual model id | Load refused, naming both extensions. |
| MCP server name | First registration wins; the later one is refused and logged. |
| Skill source id | Last registration wins, with a warning. |
| Tool-result renderer target | Last registration wins, with a warning. |

## Settings-schema errors

A settings violation refuses the load: `/extensions` reads `failed`, and the log has
`extension '<id>': setting '<name>' …`. Check that:

- the file is `~/.lunate/extensions-settings/<id>.json`, not a project file;
- it is a JSON object (not an array);
- every `required` name from `settingsSchema` is present;
- each declared top-level property has the right JSON kind.

The host enforces only `required` and the top-level `properties.<name>.type`. `default`, `enum`,
`minimum`, `maximum` and nested schemas are documentation; the extension code applies its own
defaults. An unreadable or invalid JSON file fails with
`extension '<id>': settings file '<path>' …`. Secrets use the same shape at
`~/.lunate/extensions-secrets/<id>.json`.

## Hook timeouts

Every hook handler has 5 seconds; `ProviderStreamEvent` has 100 ms. A timeout or exception is logged
as:

```text
hook ToolCalling: handler PermissionGateToolCallingHandler from extension 'permission-gate' failed: timed out after 5000 ms; the hook's failure policy applies.
```

The policy decides the consequence:

- `ToolCalling` blocks the call fail-safe; the model sees
  `Denied: '<tool>' was blocked: the ToolCalling handler from extension '<id>' failed; the tool call was blocked. …`
- `ProjectTrust` denies the project extension.
- `Compacting` falls back to the default compaction.
- Every other hook keeps the original value or skips that handler and continues.

A hook that takes more than 500 ms is slow, not failed: the first crossing logs one warning and
`/extensions` shows the slowest hook above the threshold. Move the work to a background service or
return a quick answer and let the next request pick up the result; do not block the run on network
or process work.

## A remote extension fails or refuses

`/extensions` appends `remote failed: …` or `remote refused: …`. A crash gets at most one automatic
restart per session; after that, use:

```text
/extensions restart <id>
```

A refusal at `initialize` means the protocol version is incompatible or the child implements a hook
or service the manifest does not declare. See [remote extensions](../extensions-remote.md).

## See also

- The manifest rules: [manifest.md](manifest.md)
- Trust and unload: [lifecycle-and-trust.md](lifecycle-and-trust.md)
- Testing a fix through the real host: [testing.md](testing.md)

# Packages: install, upgrade and uninstall

A package is a NuGet package that carries an extension, skills, or both, and is installed by id. It is the format for extensions that are more than one file: they publish output, they may depend on libraries, and they are removed the same way they arrived.

This page is the reference for installing one. The bundle format is owned by the same contract a hand-placed extension uses — [`manifest.md`](manifest.md) — and the trust model by [`lifecycle-and-trust.md`](lifecycle-and-trust.md). What is specific to packages is here: the bundle, resolution, the record, both scopes, and what each command prints.

## The bundle

```text
lunate.package.json
extension/            extension.json + the published files
skills/<name>/        one SKILL.md per skill, plus its files
```

```json
{ "schema": 1, "extension": "extension", "skills": "skills" }
```

At least one of `extension` and `skills` must be present. `extension.json` is exactly the manifest a hand-placed extension has — same fields, same parser, same `apiVersion` check — so a package's extension is indistinguishable from a directory someone copied in, apart from the record that says where it came from. Unknown fields in `lunate.package.json` are ignored, so a bundle written for a later version still parses.

A skills-only bundle has no `extension/` folder and no entry assembly; it installs skills and nothing else, and `lunate packages` says so.

## Install

```bash
lunate install <package-id>                 # latest stable, resolved once and recorded exactly
lunate install <package-id>@<version>       # pinned
lunate install <package-id> --dry-run       # print the full identity and write nothing
lunate install <package-id> --project       # install into this repository
lunate install <package-id> --offline       # resolve only from the global packages folder
lunate install <package-id> --force         # repair a missing or changed recorded file
lunate install <package-id> --yes           # inside a Lunate session: confirm the install
lunate install <package-id> --json          # machine-readable result
```

A range, a wildcard, a floating version, a path or a git URL is a usage error. "Which version am I running" has exactly one answer, and a range belongs to a dependency someone declared, not to a user's install.

Before anything is written, the command prints what it is about to install: canonical id, exact version, source, the nupkg's SHA-512, size, the contents, and the dependency closure with each resolved version. The SHA-512 is transport integrity against the feed — it says the bytes arrived intact, not who wrote them.

### Inside a session

`lunate install` is a CLI command; no tool, slash command or protocol path wraps it. But the bash tool can run it, so a session can start one — including one a prompt injection asked for. When `lunate install` detects it is running inside a Lunate session (the `LUNATE_SESSION` marker the bash tool sets for its children), it refuses without `--yes`. The approval prompt that lets the command run shows the full command, `--yes` included, so an install started from a session is never silent and the command the user approved is the command that runs.

## Where the content goes

| What | User scope (default) | `--project` |
| --- | --- | --- |
| record | `<root>/packages/<package-id>/install.json` | `<root>/packages/<package-id>/install.json` |
| extension | `<root>/extensions/<extension-id>/` | `<worktree>/.lunate/extensions/<extension-id>/` |
| skills | `<root>/skills/<name>/` | `<worktree>/.agents/skills/<name>/` |

`<root>` is `LUNATE_HOME` when set, otherwise `~/.lunate`.

A project install puts the content in the repository — `.lunate/extensions/` and `.agents/skills/` are committed, so a teammate gets the same bytes through git — and keeps the record in the state root, because the record is machine state: this machine's feeds, paths and install time. A project record names its worktree, so `lunate packages` in another worktree does not list it.

**A project install is a vendored install.** Dependencies are materialised beside the entry assembly and therefore committed too. Install prints the total content size; above 10 MiB it warns and names the largest entries, because that size is now in the repository's history.

## Dependencies

A package's declared dependencies are resolved by the same walk a single-file extension's `#:package` directives use: same resolver, same conformance rules, same global packages folder, same host-owned exclusion. There is no second resolver and no compile step — a package is published output.

The resolved graph is written to `<root>/cache/extensions/<extension-id>/extension.lock.json`, the file a single-file extension already writes: same format, same canonical bytes for hashing, same graph and toolset halves. The record names the lock; it does not copy it. `lunate cache prune` keeps an installed package's entry, because unlike a build it cannot be regenerated from a source file.

A dependency no feed serves, a graph that will not settle, or assets that fit no target is refused **before anything is written**, naming the dependency and the feeds tried. Assemblies the host owns are never materialised; a bundle that ships its own copy of the contract still loads the host's.

### Offline and private feeds

`--offline` answers every question from the global packages folder and makes no feed request at all, not even a version lookup. A cold folder fails naming the package, the missing dependency and the folder it searched, and writes nothing.

Feeds and credentials come from NuGet's configuration chain rooted at the working directory: a repository's `nuget.config` applies to a `--project` install, the user's to a user install. There is no interactive credential prompt; a 401 names the source and the file to fix.

## The record

`install.json` has two halves. `package` carries the sources, canonical id, exact version, the nupkg's SHA-512, the archive's file list and the extension id whose cache entry holds the lock. `content` carries the extension id and version, the skill names, and every written path with its size and SHA-256. Around them: the scope, the worktree when project, the install time, and an integrity hash over the record's own canonical bytes.

The record is written last. A failure before that leaves the previous install byte-identical — content and record — and there is no moment at which a valid record describes content that is not there.

```bash
lunate packages                # what this directory can see, by scope and package id
lunate packages --verify       # recompute every recorded hash
lunate packages --json         # the same, machine-readable
```

`--verify` exits 3 when any record is broken, a recorded file changed, or recorded content is gone. A store that cannot be read exits 1 with the reason; a usage error exits 2.

A record that does not answer for itself reads `broken` and no ordinary command acts on it. It is recoverable: `lunate install <id>` over it replaces it (the reinstall is the repair), and `lunate uninstall <id> --force` removes it together with the content at the paths that need no record — its own directory and `<root>/extensions/<package-id>/` — printing what it removed and naming what only a readable record could have known.

## Upgrade, downgrade, repair

`lunate install <id>@<other-version>` on an installed package is the upgrade, and the downgrade is the same command. Content is staged, the old record's now-stale paths are deleted, the new content is committed, the lock is written, and the record goes last.

An upgrade changes the bytes and the graph, so the trust digest changes and the extension is pending until it is approved again. An identical reinstall keeps the existing decision.

A file that cannot be deleted — on Windows, an assembly a running session still has mapped — is reported as **left behind (in use)** and appears in `--json` as `leftBehind`. It is not counted as removed, and the message says which file and what to do: stop the session and run the install again.

`--force` on `install` repairs an install whose recorded files are missing or changed: each recorded path is rewritten from the recorded version's own archive, and the record is left byte-identical. A record that cannot be read at all needs no flag — a plain reinstall replaces it and names the bytes it could not account for.

## Uninstall

```bash
lunate uninstall <package-id>            # or an extension id only one record provides
lunate uninstall <package-id> --force    # removes a changed file, or a record that cannot be read
lunate uninstall <package-id> --json
```

Uninstall deletes exactly the paths the record lists, then the directories the install created that are now empty, then the record. It keeps the nupkgs and the lock. A file inside a package's directory that the package did not write is left in place and **named**. A file whose hash no longer matches refuses the uninstall and names the files; `--force` removes it anyway.

An ambiguous extension id is refused with the candidates. There is no `--project`: the record's scope decides.

## Trust

Installing is not trusting. An installed extension is pending in **both** scopes — user and project — until the existing question is answered at first load, and that question names where the code came from: `from package acme-tools@1.2.0 (nuget.org)`. Hand-placed extensions keep their current rule: a hand-placed user-scope extension is trusted by placement, a hand-placed project one is pending.

The decision binds the package identity, the canonical lock graph and the directory's content hash, so an upgrade re-prompts and an edited file re-prompts. Install writes no trust record; it changes the pending set `lunate trust` prints.

## What a package cannot do

There is no marketplace, search or discovery, no `update` command, and no self-update of Lunate. There is no signature or provenance verification: the SHA-512 is transport integrity, not authorship. Signing is the fix for installing code you have not read, and it is deferred by decision, not by oversight.

## Exit codes

| Code | `install` | `uninstall` | `packages` |
| --- | --- | --- | --- |
| 0 | installed, upgraded, repaired, or nothing to do | removed | listed (with `--verify`: everything matches) |
| 1 | the install failed (feed, resolution, filesystem) | the uninstall refused or failed | the store could not be read |
| 2 | usage error (including the session gate without `--yes`) | usage error | usage error |
| 3 | — | — | `--verify` found a broken record, a changed file or missing content |

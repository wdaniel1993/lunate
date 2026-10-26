# 0017 addendum — Packages: install, the record, and what trust binds

- Status: **Accepted — 2026-10-26** (maintainer sign-off in chat; immutable once accepted)
- Date: 2026-10-26
- Relates to: ADR 0017 (extensibility architecture); 0017 addendum — Extensions: formats, restore and trust; ADR 0005 (startup budgets)
- Evidence: `openspec/changes/package-install` (`design.md`, `specs/packages`, `specs/extensions`); the phase PRs #177, #178, #193, #194

## Context

ADR 0017 names the second extension format in one sentence: *"a packaged extension is a `.nupkg` with `extension.json`, an entry assembly and its dependencies; `lunate install <id>` installs it"*. The single-file work shipped the machine that sentence needs — an in-process restore through the NuGet client libraries, a dependency walker with its conformance corpus, a lock whose graph is bound into a trust record, and a cache keyed by everything a build consumed.

What the sentence does not say is what a package *is* as a distribution unit: its manifest, what a version means, where its content lands, what proves it later, and how it goes away. Those are the decisions this addendum records. The constraint throughout is one restore, one lock, one trust mechanism: a package reuses the walk, the lock writer, the trust digest and the loader rather than growing second copies of them.

## Decision

**P1 — The bundle format is `lunate.package.json` and the folders it names.**

```json
{ "schema": 1, "extension": "extension", "skills": "skills" }
```

```text
lunate.package.json
extension/            extension.json + the published files
skills/<name>/        one SKILL.md per skill, plus its files
```

At least one kind must be present, and only kinds this version installs. `extension.json` stays exactly what it is — it requires exactly one of `entryAssembly` and `runtime`, so a skills-only bundle could not be expressed through it, and a package manifest that doubled as the extension's would tie two lifecycles together for no gain. Unknown fields are ignored, so a package written for a later version still parses.

**P2 — The unit is a NuGet package id at an exact version; an upgrade is a reinstall at a different version.**

`lunate install <id>` resolves the latest stable once and records exactly what it resolved; `install <id>@<version>` pins. A range, a wildcard or a floating version is a usage error — a range belongs to a dependency someone declared, not to a user's install, and "which version am I running" must have one answer. A path or a git URL is refused by name: a directory is not a distribution unit, and that is what copying into `.lunate/extensions/` is for.

There is no `update` command. `install <id>@<newer>` on an installed package replaces it in place, so a future convenience command can be added without changing the meaning of anything here.

**P3 — Dependency resolution is the existing walk, not a second one.**

The package's declared dependency set goes through the same resolver, conformance corpus, global packages folder, asset selection and host-owned exclusion a single-file restore uses. Resolved assemblies are materialised beside the entry assembly, which is the layout the packaged loader already binds — the loader does not change. A package is published output, so install compiles nothing and touches no build cache.

A dependency the feeds do not serve, a graph that will not settle, or assets that fit no target is refused **before anything is written**, naming the dependency and the feeds tried. Ignoring dependencies would make "installed" a lie; a half-materialised closure would make it a trap.

**P4 — The lock is the existing lock, in the existing place.**

`<root>/cache/extensions/<extension-id>/extension.lock.json`, same format, same graph and toolset halves, same canonical bytes for hashing. It is named by the record, never copied into it. `cache prune` leaves an installed package's entry alone because, unlike a build, it cannot be regenerated from a source file; a reinstall is the repair.

**P5 — One record, `install.json`, with the lock referenced rather than duplicated.**

Its `package` half carries the sources, canonical id, exact version, the nupkg's SHA-512, the archive's file list and the extension id whose cache entry holds the lock. Its `content` half carries the extension id and version, the skill names, and every written path with its size and SHA-256. Around them: the scope, the worktree when project, the install time, and an integrity hash over the record's own canonical bytes.

Writes are ordered so that the record is last. A failure before it leaves the previous install **byte-identical** — content and record — and there is no window in which a successful record describes content that is not there.

A record that does not answer for itself reads `broken`, and no ordinary command acts on it. Being stuck is not an acceptable end state, so recovery is defined:

- **`install <id>` over a broken record replaces it.** The reinstall is the repair P4 describes: content is staged and written, the record is written last and replaces the broken one. Bytes the broken record would have named but no longer can are named at the end of the run rather than claimed as removed.
- **`uninstall <id> --force` removes it**, together with the content at the conventional paths that need no record — the record's own directory and `<root>/extensions/<package-id>/` when it exists — printing every path it removed and naming what only a readable record could have known (the skill directories, an extension id that differs from the package id).

**P6 — Content lands in the scopes that already exist; a project install writes content into the repository and keeps its record local.**

| What | User scope | Project scope |
| --- | --- | --- |
| record | `<root>/packages/<package-id>/install.json` | `<root>/packages/<package-id>/install.json` |
| extension | `<root>/extensions/<extension-id>/` | `<worktree>/.lunate/extensions/<extension-id>/` |
| skills | `<root>/skills/<name>/` | `<worktree>/.agents/skills/<name>/` |

`<root>` is `LUNATE_HOME` when set, otherwise `~/.lunate`. The project record stays in the state root because it is machine state — this machine's feeds, paths and install time — while the content is the team's: committed, carried by git, and trusted by each teammate through the existing question. A project record names its worktree, so `lunate packages` elsewhere does not list it.

User-scope skills land in `<root>/skills`, which is the user-scope skills root discovery already scans. They do not land in `~/.agents/skills`: that folder is the cross-agent hub other tools read and write, and an install through Lunate belongs in Lunate's own store, where the record and `lunate uninstall` can account for it.

**A project install is a vendored install.** Dependencies are materialised beside the entry assembly (P3), so a project install commits the entry assembly *and every resolved dependency assembly* into the repository. That is the point — a teammate gets identical bytes from git, no feed, no restore — but it is a size the team carries. Install prints the total content size and, for `--project`, warns above 10 MB, naming the largest entries, so the decision to commit is made with the number in hand.

**P7 — Trust does not change shape; an installed extension is pending in both scopes until the existing question is answered.**

An installed extension is a directory with a manifest, so the existing extension question decides it. What this addendum changes is *when* it is trusted: **installation does not trust.** An installed extension — user scope or project scope — is pending until the user answers the existing question at first load, and that question names where the code came from: `from package acme-tools@1.2.0 (nuget.org)`. Hand-placed extensions keep their current rule: a hand-placed user-scope extension is trusted by placement, a hand-placed project extension is pending. The install record is what separates the cases — a directory a record owns is pending in either scope, because a package is code the user neither wrote nor placed.

The decision binds:

- the package identity the user chose — package id@version and the nupkg's SHA-512;
- the canonical lock graph, so a re-resolution that changes dependencies re-prompts;
- the existing directory content hash, because installed files are writable and identity alone would not notice an edit.

Install writes no trust record; it changes the pending set `lunate trust` prints, and the first load asks.

Install is a CLI command and nothing else: no tool, slash command or protocol path wraps it. It does **not** follow that a model cannot install anything — the bash tool can run `lunate install`, and a prompt injection can put that command in front of it. What is guaranteed is narrower and checkable: when `lunate install` runs inside a Lunate session — detected by the marker the bash tool sets in its children's environment — it **refuses unless `--yes` is given**, and the approval prompt that lets any such command run displays the full command, `--yes` included. An install started from inside a session is therefore never silent, and the command the user approved is the command that runs. Outside a session there is nothing to gate: the user typed it.

**P8 — Supply chain: what is in, and what is not.**

In: an exact id match with no search or suggestions, the full identity printed before anything is written (id, exact version, source, SHA-512, size, contents, dependencies), `--dry-run`, path confinement for every manifest path and archive entry, entry-count and expanded-size bounds checked before extraction, per-file hashes in the record, and `--verify` to prove the content later.

Out: author and repository signature verification, provenance attestations, vulnerability scanning, an SBOM. The SHA-512 is transport integrity against the feed, not a statement about who wrote the package; the command's output and the docs say so.

**P9 — Scope is explicit and collisions are refusals.**

User scope is the default; `--project` outside a worktree is a usage error. The extension id is the collision key across both scopes and against hand-placed and single-file extensions alike, and an install that would create a duplicate is refused naming both providers. An existing skill directory is never written into; a same-named skill in the other scope is the existing shadowing rule, warned once.

**P10 — Offline means no request at all.**

`--offline` resolves the package and its closure only from the global packages folder and makes no feed request, not even a version lookup — asserted by an observer on the feed seam, not inferred. A cold folder fails naming the package, the missing dependency and the folder, and writes nothing.

## Deferred

Named here so they are not silently implied by "packages exist":

- a marketplace, search or package discovery;
- signature verification and provenance attestations (**the real fix** for installing code the user has not read);
- vulnerability scanning and an SBOM;
- git sources and self-update of Lunate;
- an `update` command (`--all`, latest-stable convenience);
- the template and theme kinds — the product has no consumer for them yet;
- a per-package dependency allowlist or denylist beyond the host-owned exclusion.

## Consequences

- **One mechanism, one place to fix it.** A change to restore, resolution, the lock or the trust digest reaches single files and packages together, because there is only one of each. The cost is that a package cannot express what a single file cannot: there is no compile step and no MSBuild path.
- **Installing is not trusting.** An installed extension asks before it runs, in both scopes, and the question carries the package it came from; a hand-placed user-scope extension keeps the trust-on-placement rule it already had. An upgrade produces new bytes and a new graph, so it asks again; an identical reinstall keeps the existing decision.
- **Uninstall is exact, and trust survives it.** Removing bytes does not revoke a decision about a path.
- **An install started inside a session is gated, not trusted.** It needs `--yes`, and the approval prompt shows that command in full; outside a session the user typed the command themselves.
- **A project install is a committable artifact and a local fact at once** — content in the repository, record in the state root — and it is *vendored*: the team carries the dependency assemblies in git, the price of identical bytes and an install that needs no feed. Install states the size before that price is paid.
- **Deferred signatures remain the honest gap**: the SHA-512 says the bytes arrived intact, not who wrote them.

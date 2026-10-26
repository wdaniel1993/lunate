## Purpose

Installing, upgrading and removing distributed packages: what a package has to contain, how its dependencies are resolved, where its content lands for each scope, how an install is recorded and verified, and what the three `lunate` commands print. A package carries extensions and skills; it is never a second trust decision.

## ADDED Requirements

### Requirement: The installable unit

A package SHALL be identified by a NuGet package id and, when given, one exact version — `lunate install <id>` or `lunate install <id>@<version>`. The version argument SHALL be an exact version; a range, a floating version or a wildcard SHALL be a usage error that names the exact form and exits 2. An argument that is a URL (a scheme, or scp-style `user@host:path`) or that names a filesystem path (a directory separator, a leading `.` or `~`, or a `.nupkg` suffix) SHALL be refused as that input before any feed is asked, naming the supported form and the reason: a path is not a distribution unit, and git is not supported in this version. A package SHALL contain a `lunate.package.json` at its root; a nupkg without it SHALL be refused, naming the package and saying what the file is for. The manifest SHALL declare at least one content kind and only the kinds this version installs: `extension` and `skills`. A declared `templates` or `themes` kind SHALL be refused by name, saying that this version does not install that kind, so a user never believes content landed that did not.

#### Scenario: A bundle with an extension and skills installs

- **GIVEN** a package `Acme.Toolkit@1.4.2` whose manifest names an `extension` folder and a `skills` folder
- **WHEN** `lunate install Acme.Toolkit` runs
- **THEN** the extension and every skill are installed and the output names the package, the exact version, the source, the SHA-512 and both content kinds

#### Scenario: A nupkg that is not a Lunate package is refused

- **GIVEN** a package that carries assemblies but no `lunate.package.json`
- **WHEN** `lunate install` runs for it
- **THEN** it is refused, naming the package and the missing file, and nothing is written

#### Scenario: A range at the command line is a usage error

- **GIVEN** the argument `Acme.Toolkit@[1.0.0,2.0.0)`
- **WHEN** `lunate install` runs
- **THEN** usage is printed on stderr, the exit code is 2, and no feed is asked

#### Scenario: A git URL is refused by name

- **GIVEN** the argument `https://github.com/acme/toolkit.git`
- **WHEN** `lunate install` runs
- **THEN** it is refused, naming the supported input (`<package-id>[@<version>]`) and saying that git is not supported in this version

### Requirement: A package's layout

The package manifest SHALL accept `schema` (the integer `1`), `extension` (one package-relative directory) and `skills` (one package-relative directory), SHALL ignore unknown fields, and SHALL refuse a known field of the wrong type naming the field. Every path a manifest names, and every entry the archive yields, SHALL resolve inside the package root: a rooted path, a `..` segment or an entry whose resolved path leaves the root SHALL be refused, naming the entry. The `extension` directory SHALL contain an `extension.json` that parses under the existing manifest rules, whose `apiVersion` range the running host meets, and whose `entryAssembly` exists in that directory; a violation SHALL be refused with the existing wording (including `requires Lunate ≥ x.y` for a version above the host). Each immediate subdirectory of `skills` SHALL be one skill holding a `SKILL.md` whose frontmatter parses and whose name equals the directory name; a malformed one SHALL be refused, naming the directory and the reason, and two skills with one name SHALL be refused. Folders outside the declared kinds — `lib/`, `build/`, `buildTransitive/` — SHALL NOT be installed and SHALL be named once in the output. A package SHALL be refused, before extraction, when it holds more than 20,000 entries or expands beyond 512 MiB, naming both the limit and the observed value.

#### Scenario: An entry that escapes the package is refused

- **GIVEN** a package whose manifest names `../../etc`
- **WHEN** it is installed
- **THEN** it is refused, naming the entry, and nothing is written outside the scope

#### Scenario: An extension this host cannot run is refused at install

- **GIVEN** a bundle whose `extension.json` declares `apiVersion` `2.0.0`
- **WHEN** it is installed on a `1.x` host
- **THEN** it is refused with `requires Lunate ≥ 2.0.0 but this is Lunate <version>`, and nothing is written

#### Scenario: Undeclared folders are ignored and named

- **GIVEN** a package that carries a `build/` folder beside its declared content
- **WHEN** it is installed
- **THEN** `build/` is not installed and one line names it as ignored

#### Scenario: A package beyond the bounds is refused before extraction

- **GIVEN** a package whose archive expands past 512 MiB
- **WHEN** it is installed
- **THEN** it is refused, naming the limit and the observed size, and no file is extracted

### Requirement: Dependency resolution

A package's declared NuGet dependencies SHALL be resolved through the existing restore pipeline — the same resolver, conformance corpus and global packages folder, the same lock format with its graph and toolset halves, and the same exclusion of host-owned assemblies from the graph — and the resolved assemblies SHALL be materialised in the extension directory beside the entry assembly, which is the layout the packaged loader binds. The lock SHALL be written as `<root>/cache/extensions/<extension-id>/extension.lock.json`, the location every single-file lock already uses, and SHALL NOT be a second file type or a second location. Install SHALL NOT compile anything and SHALL write no build. A dependency that no configured feed serves, or a graph the resolver cannot settle, SHALL be refused before anything is written, naming the dependency and the feeds tried, and a package SHALL NOT be reported installed with a partial closure.

#### Scenario: A declared dependency is resolved and materialised

- **GIVEN** a packaged extension that declares `Octokit` and a feed that serves it
- **WHEN** it is installed
- **THEN** `Octokit` and its own dependencies are materialised beside the entry assembly, the lock names the exact versions, and a session loads the extension

#### Scenario: An unresolvable dependency is refused, not half-installed

- **GIVEN** a packaged extension declaring a dependency that no configured feed serves
- **WHEN** it is installed
- **THEN** it is refused naming the dependency and the feeds tried, and no extension directory and no record exist

#### Scenario: Host-owned assemblies stay out of the graph

- **GIVEN** a package whose dependency closure would include the host's own contract assembly
- **WHEN** it is installed
- **THEN** the lock's graph does not name that assembly and the load uses the host's copy

#### Scenario: Offline resolves the whole closure or nothing

- **GIVEN** the package nupkg and one of its dependencies in the global packages folder, and another dependency missing
- **WHEN** `lunate install --offline` runs
- **THEN** it fails naming the missing dependency, and no content or record is written

### Requirement: Install runs no package code and records no trust

`lunate install` SHALL download, resolve, verify and write files, and SHALL NOT load an assembly, run a factory, start a process, dispatch a hook or write any trust section of the trust store. Installing SHALL NOT be trusting in either scope: a descriptor whose directory an install record owns SHALL be pending wherever it landed, so a user-scope install asks before its extension loads, exactly as a project-scope install does — while a hand-placed user-scope extension stays trusted by placement and a hand-placed project extension still asks. Approving through the existing question or `lunate trust` SHALL be what makes an installed extension load, with no new trust record shape: the decision binds the package identity and the directory content hash in the existing extensions section. A project-scope install's skills SHALL stay untrusted until the project skills question is answered. Install SHALL only change the pending set that `lunate trust` lists, and that pending set SHALL carry the provenance the questions show.

#### Scenario: A project install leaves the extension untrusted

- **GIVEN** a repository with no trust record and a project install of a bundle carrying an extension
- **WHEN** `lunate install --project` runs and then a session starts
- **THEN** the install wrote no trust record, the session asks the existing extension question naming the id, version, directory and source package, and `/extensions` reads `trust-denied` until it is answered

#### Scenario: Install never runs the extension's code

- **GIVEN** a bundle whose extension factory writes a marker file
- **WHEN** `lunate install` runs and the marker path is checked
- **THEN** the marker is absent

#### Scenario: A project install shows up in the trust inventory

- **GIVEN** a project install of a bundle carrying an extension and two skills
- **WHEN** `lunate trust` runs
- **THEN** the extension and the skills appear among the pending decisions with the source package, and `lunate trust --yes` records them so a later session asks nothing

#### Scenario: A user install asks before it loads

- **GIVEN** no existing trust records
- **WHEN** `lunate install Acme.Toolkit` runs at the user scope and a session starts
- **THEN** the existing extension question names the package with `from package Acme.Toolkit@<version> (<source>)`, the extension does not load until it is approved, and a later session asks nothing

#### Scenario: A hand-placed user-scope extension is still trusted by placement

- **GIVEN** an extension directory under `<root>/extensions/` that no install record names
- **WHEN** a session starts
- **THEN** no question is asked and the extension loads

### Requirement: Where installed content lands

A user-scope install SHALL write under the state root: the record at `<root>/packages/<package-id>/install.json`, the extension at `<root>/extensions/<extension-id>/`, and each skill at `<root>/skills/<name>/`. A project-scope install SHALL write the content into the repository — the extension at `<worktree>/.lunate/extensions/<extension-id>/` and each skill at `<worktree>/.agents/skills/<name>/` — so the content can be committed and reaches teammates through version control, while its record SHALL stay in the state root at `<root>/packages/<package-id>/install.json` and SHALL name the worktree it belongs to. A project install SHALL NOT write to `<root>/skills`, SHALL NOT write under `<root>/cache/extensions/<extension-id>/builds/`, and SHALL NOT copy anything into the global packages folder beyond the nupkgs the download and the dependency walk put there. The package-id directory name SHALL be the lowercased package id, because NuGet ids are case-insensitive and filesystems may be too, while the record SHALL carry the canonical spelling. With `LUNATE_HOME` set, every user-scope path SHALL resolve under it and nothing SHALL be written under the profile, and the existing store guard SHALL run before the first write, exactly as it does for every command that writes the store.

#### Scenario: A user install follows the state root

- **GIVEN** `LUNATE_HOME` set to an absolute temporary directory
- **WHEN** a user-scope install runs
- **THEN** the record, the extension and the skills are all under that directory and nothing is written under the profile

#### Scenario: A project install writes committable content and a local record

- **GIVEN** a repository worktree with no `.lunate/` directory and a state root outside it
- **WHEN** `lunate install --project` runs
- **THEN** the extension and the skills are under `<worktree>/.lunate/extensions/` and `<worktree>/.agents/skills/`, the record is under the state root and names the worktree, and `git status` shows the content as untracked additions

#### Scenario: A project record does not follow the content to another worktree

- **GIVEN** a project install in one worktree
- **WHEN** `lunate packages` runs in a second worktree of the same repository
- **THEN** the package is not listed as installed there

#### Scenario: The build cache is untouched

- **GIVEN** a user install
- **WHEN** `<root>/cache/extensions/<extension-id>/builds/` is compared before and after
- **THEN** it is unchanged, and `lunate cache prune` leaves the install's lock and content alone

#### Scenario: A store inside the workspace is refused

- **GIVEN** `LUNATE_HOME` set to a directory inside the worktree
- **WHEN** `lunate install` runs
- **THEN** it fails with `LUNATE_HOME points inside the workspace; trust records there could be written by the repository` and nothing is written to that path

### Requirement: The install record

Each installed package SHALL have one record at `<root>/packages/<package-id>/install.json` with two halves: `package` — the sources used, the canonical package id, the exact version, the nupkg's SHA-512, the archive's file list and the extension id whose cache entry holds the dependency lock — and `content` — the extension id and version when one was installed, the skill names, and every written path relative to the scope root with its size and SHA-256. It SHALL also carry the scope, the worktree when the scope is project, the install time (ISO 8601, invariant) and an integrity hash over its own canonical bytes, computed with that field empty. Every path in the record SHALL be relative to the scope root; a record naming an absolute path or a path that resolves outside the root SHALL be refused as `broken`. A record whose integrity hash does not answer for its contents SHALL be reported as `broken` and SHALL NOT be used to uninstall or trust anything. An install SHALL NOT be blocked by an unreadable record at its own path: a reinstall SHALL write the content and a new record over it, and SHALL name the bytes it could not account for instead of claiming to have removed them. `--force` SHALL remove the content of a broken record: the record's own directory and `<root>/extensions/<package-id>/` when it exists, printing every path it removed and naming what only a readable record could have known — the skill directories, and an extension id unlike the package id. Writing SHALL be atomic per file, and the record SHALL be written last, so a record that exists names content that exists. The dependency lock SHALL NOT be duplicated into the record: the record names the lock's location, and the lock remains the existing `extension.lock.json`.

#### Scenario: The record names everything that was written

- **GIVEN** a completed install of a bundle carrying an extension and two skills
- **WHEN** the record is read
- **THEN** `content` lists the extension directory and every file under it, both skill directories and their files, each with a size and a hash, and `package` names the lock's cache entry

#### Scenario: A hand-edited record is refused by the view and the uninstall

- **GIVEN** an installed package whose record's version is edited by hand
- **WHEN** `lunate packages` and `lunate uninstall` run
- **THEN** the package reads `broken` and both commands refuse to act on the record, naming it

#### Scenario: A reinstall is the repair

- **GIVEN** an installed package whose record no longer answers for itself
- **WHEN** the same version is installed again
- **THEN** the content and a new record are written, the record reads `broken` no more, `lunate packages --verify` is clean, and the run names any file it left in place because the old record could not name it

#### Scenario: Uninstall --force removes what a broken record cannot list

- **GIVEN** an installed package whose record is unreadable, and a skill directory the record would have named
- **WHEN** `lunate uninstall <package-id> --force` runs
- **THEN** the record's directory and `<root>/extensions/<package-id>/` are removed, every removed path is printed, the run says that only a readable record could have named the skill directory, and the skill directory is left in place

#### Scenario: Uninstall without --force still refuses a broken record

- **GIVEN** an installed package whose record is unreadable
- **WHEN** `lunate uninstall <package-id>` runs
- **THEN** it refuses, naming the record, and deletes nothing

#### Scenario: Installing the same version again writes nothing

- **GIVEN** an installed package whose record and files verify
- **WHEN** the same version is installed again
- **THEN** it reports that the version is already installed, exits 0, and no file's modification time moves

### Requirement: Scope, precedence and collisions

The default scope SHALL be user and `--project` SHALL select the current worktree; `--project` outside a worktree SHALL be a usage error naming it. An install SHALL be refused when its extension id is already provided by another installed package, by a hand-placed packaged extension, or by a single-file extension, naming both providers and the command that clears one — the discovery error that would otherwise appear at the next session start becomes a refusal at install. An install SHALL be refused when a skill directory it would write already exists, naming the directory and the record that owns it when one does, and SHALL NOT overwrite it. Where a skill of the same name exists in the other scope, the existing shadowing rule SHALL apply and install SHALL warn once, naming the winner. No scope SHALL silently override another.

#### Scenario: A cross-scope duplicate is refused at install

- **GIVEN** `Acme.Toolkit` installed at the user scope, and a project install of a package whose extension id is the same
- **WHEN** the project install runs
- **THEN** it is refused, naming both providers and the uninstall that clears one, and nothing is written

#### Scenario: An existing skill directory is never overwritten

- **GIVEN** a hand-written project skill named `review`
- **WHEN** a project install of a bundle carrying `review` runs
- **THEN** it is refused, naming the existing directory, and the hand-written skill is unchanged

#### Scenario: A shadowed skill warns in the other scope

- **GIVEN** `review` installed at the user scope and another package carrying `review` installed for the project
- **WHEN** the project install runs
- **THEN** it succeeds, warns once naming the winner, and the skill index lists one `review`

### Requirement: Pinning and upgrade

An install SHALL record the exact version it resolved and SHALL never re-resolve on its own. There SHALL be no automatic update, no background version check and no version check at session start, and no separate update command in this version: installing a different version of an installed package SHALL be the upgrade, and SHALL behave as a pinned replacement — download, verify and resolve into `<root>/packages/<package-id>/staging/`, remove the previously recorded paths through the old record, write the new content, then write the lock and the record, record last — so a failure at any point before the record write leaves the previous install's content and record byte-identical. A bare `install` SHALL resolve the latest stable version only; an explicit prerelease version SHALL be accepted when named exactly. A package that is no longer on any feed SHALL fail, naming the package and the feeds tried, and SHALL remove nothing. An install that would collide under the scope rules SHALL be refused before anything is replaced. An upgrade that changes the package identity or the content SHALL change the trust digest and therefore re-prompt at the project scope.

#### Scenario: A newer version replaces the old one in place

- **GIVEN** `Acme.Toolkit@1.4.2` installed
- **WHEN** `lunate install Acme.Toolkit@1.5.0` runs
- **THEN** the 1.4.2 content is removed through its record, the 1.5.0 content and record are written, and no file from 1.4.2 remains

#### Scenario: A failed upgrade keeps the previous install

- **GIVEN** `Acme.Toolkit@1.4.2` installed
- **WHEN** the upgrade fails while downloading or resolving dependencies
- **THEN** the installed content and the record are unchanged, and the failure names the package and the reason

#### Scenario: A pinned upgrade may go backwards

- **GIVEN** `Acme.Toolkit@1.5.0` installed
- **WHEN** `lunate install Acme.Toolkit@1.4.2` runs
- **THEN** `1.4.2` is installed and recorded

#### Scenario: An upgraded package re-prompts at the project scope

- **GIVEN** a trusted project install of `Acme.Toolkit@1.4.2`
- **WHEN** `lunate install Acme.Toolkit@1.5.0 --project` runs and a session starts
- **THEN** the trust question is asked again, naming `Acme.Toolkit@1.5.0`

### Requirement: Uninstall

`lunate uninstall <package-id | extension-id>` SHALL delete exactly the paths the record lists and the record's own directory, then remove the directories the install created that are now empty, and SHALL NOT delete anything else. It SHALL accept an extension id the record names when exactly one record matches, and SHALL refuse an ambiguous one, naming the candidates. It SHALL refuse, naming the files, when a recorded file's hash no longer matches, and SHALL proceed with `--force`. Files inside a directory the package created but did not write SHALL be left and named, with a line saying the directory is not empty and where it is. For a record that does not answer for itself, a plain uninstall SHALL still refuse, naming the file; `--force` SHALL remove the record's own directory and `<root>/extensions/<package-id>/` when it exists, print every path it removed, and name what only a readable record could have known. The dependency lock SHALL be left in place: it is the record of a resolution, not installed content, and a reinstall reuses it. Trust records SHALL NOT be touched: trust is a decision about bytes at a path, so removing the content does not revoke it and an identical reinstall keeps it. A session that already loaded the extension SHALL keep it until the process exits; the next session sees the content gone. A recorded file that cannot be deleted because a running process has it loaded SHALL stop the uninstall with exit 1, name that file and say it is in use, delete nothing after it and leave the record intact, so re-running the command once the session has stopped finishes the job.

#### Scenario: Uninstall removes what was written and nothing else

- **GIVEN** an installed bundle carrying an extension and two skills
- **WHEN** `lunate uninstall Acme.Toolkit` runs
- **THEN** the extension directory, the two skill directories and the record are gone, and the nupkgs and the lock are untouched

#### Scenario: A hand-added file is left and named

- **GIVEN** an installed extension directory holding one file the package did not write
- **WHEN** `lunate uninstall` runs
- **THEN** the recorded paths are deleted, the extra file is left, and one line names it

#### Scenario: A changed file refuses without --force

- **GIVEN** an installed extension whose recorded entry assembly was edited
- **WHEN** `lunate uninstall` runs
- **THEN** it refuses, naming the file, and nothing is deleted

#### Scenario: Uninstall by extension id

- **GIVEN** one installed package whose extension id is `acme-toolkit`
- **WHEN** `lunate uninstall acme-toolkit` runs
- **THEN** the package that provides it is removed

### Requirement: Failure, offline and private feeds

Install and upgrade SHALL be all-or-nothing per command: the record is the commit point, and a failed command SHALL leave no content that no record names. Uninstall SHALL delete the record last, so a failed uninstall still leaves a record that describes what remains. `--offline` SHALL resolve the package and its whole dependency closure from the global packages folder only, SHALL make no feed request, and SHALL fail naming the package, the missing dependency and the packages folder (`NUGET_PACKAGES` when set, the default otherwise) when the exact versions are not all there. Feeds and credentials SHALL come from NuGet's own configuration chain rooted at the working directory, so a repository's `nuget.config` applies to a project install and the user's applies to a user install; there SHALL be no interactive credential prompt. An authentication failure SHALL name the source and the configuration file to fix. An id that no configured feed serves, or a version that id does not publish, SHALL fail naming the id, the version and the feeds tried, and SHALL write nothing. `--force` SHALL repair an install whose content is partial or changed by rewriting the recorded paths from the recorded version's nupkg and lock.

#### Scenario: Offline installs from a warm packages folder

- **GIVEN** the exact nupkg and every dependency nupkg already in `NUGET_PACKAGES` and the observer on the feed seam
- **WHEN** `lunate install --offline` runs
- **THEN** the install completes and the observer records zero feed requests

#### Scenario: Offline with a cold folder fails and writes nothing

- **GIVEN** an empty packages folder
- **WHEN** `lunate install --offline` runs
- **THEN** it fails naming the package and the packages folder, and no content or record is written

#### Scenario: An unknown version names the feeds

- **GIVEN** a feed that serves `Acme.Toolkit` at `1.4.2` only
- **WHEN** `lunate install Acme.Toolkit@9.9.9` runs
- **THEN** it fails naming the id, the version and the feed, and nothing is written

#### Scenario: A private feed that refuses credentials says which file to fix

- **GIVEN** a configured feed that answers 401
- **WHEN** an install runs
- **THEN** the failure names the source and the `nuget.config` that must carry the credentials

#### Scenario: A partial install is repaired

- **GIVEN** an installed package with one recorded file missing
- **WHEN** `lunate install Acme.Toolkit@<the recorded version> --force` runs
- **THEN** every recorded path matches its record again and the record's version is unchanged

### Requirement: Install output and `--dry-run`

`lunate install` SHALL print, before writing: the package id in its canonical spelling, the exact version, the source it came from, the nupkg's SHA-512, the size, the extension id and version when one is carried, the skill names, and the dependency closure with each resolved version — then the total content size it wrote (the entry assembly, the materialised dependencies and the skills, summed from the record) in both scopes, and one line saying what was written and where. A project-scope install whose total content exceeds 10 MiB SHALL warn once, naming the total and the largest entries, because that content is committed; a user-scope install SHALL NOT warn about size. Install SHALL print nothing to stdout on a failure, the reason on stderr, and exit 1; when the package's identity was resolved before the failure, the reason SHALL carry the id, the version and the source. A usage error SHALL exit 2; a successful install and an already-installed version SHALL exit 0. `--dry-run` SHALL print the same identity, contents and closure, resolve nothing into the store, write nothing, and exit 0. `--json` SHALL print one object carrying the same fields, canonical newlines, invariant numbers and no colour. `lunate uninstall` SHALL follow the same printing and exit-code rules for what it did, with `--json` printing one object naming what changed.

#### Scenario: Dry-run prints the identity and writes nothing

- **GIVEN** a bundle reachable on a configured feed
- **WHEN** `lunate install --dry-run` runs
- **THEN** the id, version, source, SHA-512, extension id, skill names and dependency versions print, and no file or record is written

#### Scenario: A refusal carries the identity it resolved

- **GIVEN** a bundle whose skills would collide with an existing skill directory
- **WHEN** `lunate install` runs
- **THEN** stderr names the package, the version and the colliding directory, stdout is empty, the exit code is 1, and nothing is written

#### Scenario: The total content size is printed in both scopes

- **GIVEN** a bundle carrying an extension, a skill and a dependency
- **WHEN** it is installed at the user scope and at the project scope
- **THEN** each run prints `content: <size>` for the files the record names, and `--json` carries `contentBytes`

#### Scenario: A large project install warns about what it commits

- **GIVEN** a bundle whose content totals more than 10 MiB
- **WHEN** `lunate install --project` runs
- **THEN** it completes, prints the total, and warns once naming the total and the largest entries; the same bundle at the user scope prints the total and warns nothing

#### Scenario: JSON carries the same fields

- **GIVEN** a completed install
- **WHEN** the same install runs a second time with `--json` and nothing has changed
- **THEN** the object carries the id, version, source, SHA-512, extension id, skill names, dependency versions, scope, the written paths and that it was already installed

### Requirement: An install started inside a session

The bash tool SHALL set `LUNATE_SESSION=1` in the environment of every child it starts, additively: the marker is added to what the child inherited, never a replacement for it. `lunate install` SHALL read the marker and, when it is set and `--yes` is absent, SHALL refuse with exit 2 and a message naming `--yes` and saying why — an install started from a command inside a session puts code on the machine that runs at the next load, so the flag has to be visible in the command the user approved. `--yes` SHALL be documented in the command's usage line and in `--help`. Outside a session the marker is absent and nothing changes.

The marker is a visibility guard, not a security boundary: a caller can unset it, and no run that skips approval prompts shows anything. The guarantee that holds regardless SHALL be the pending trust decision: `lunate trust --yes` SHALL refuse with exit 2 inside a session and record nothing, because approval is the user's answer and `--yes` is exactly the flag that would make it silent. Clearing trust SHALL remain available inside a session: revoking a decision escalates nothing.

#### Scenario: A session install without --yes is refused

- **GIVEN** a command running inside a Lunate session
- **WHEN** `lunate install Acme.Toolkit` runs without `--yes`
- **THEN** it exits 2, the message names `--yes` and says that the code would run at the next load, and nothing is written

#### Scenario: A session install with --yes proceeds

- **GIVEN** the same session marker
- **WHEN** `lunate install Acme.Toolkit --yes --dry-run` runs
- **THEN** it resolves the identity and prints the dry run, and writes nothing

#### Scenario: Outside a session nothing is gated

- **GIVEN** no `LUNATE_SESSION` marker in the environment
- **WHEN** `lunate install Acme.Toolkit` runs
- **THEN** it installs, with no `--yes` needed

#### Scenario: Approval is refused inside a session

- **GIVEN** a session marker and a pending installed extension
- **WHEN** `lunate trust --yes` runs
- **THEN** it exits 2 with a message that approval belongs to the session's prompt, and no trust record is written

#### Scenario: Revoking is allowed inside a session

- **GIVEN** the same marker and an approved extension
- **WHEN** `lunate trust --clear` runs
- **THEN** the record is removed, so the next load asks again

### Requirement: The `lunate packages` view

`lunate packages [--json] [--verify]` SHALL list every installed package this working directory can see — the user scope and the project installs whose worktree is the current one — one line each, ordered by scope then package id, carrying the canonical id, the version, the scope, and what it provides (the extension id and the skill names). `--verify` SHALL recompute each recorded file's hash and append the count of changed and missing files, and SHALL exit 3 when any record is `broken`, lists a file whose hash changed, or names content that is gone; without `--verify` the command SHALL hash nothing. `--json` SHALL print one array of objects carrying the line's fields plus `installedAt`, `verified` and the changed and missing counts. With nothing installed it SHALL print `packages: none installed` and `--json` SHALL print `[]`. A store that cannot be read SHALL exit 1 with the reason on stderr; a usage error SHALL exit 2.

#### Scenario: The view lists both scopes

- **GIVEN** one user install and one project install in the current worktree
- **WHEN** `lunate packages` runs
- **THEN** two lines print, the user one first, each with its id, version, scope and contents

#### Scenario: Verify reports a changed file and exits 3

- **GIVEN** an installed extension whose entry assembly was edited
- **WHEN** `lunate packages --verify` runs
- **THEN** its line carries the changed count, names the file, and the exit code is 3

#### Scenario: The empty view

- **GIVEN** nothing installed
- **WHEN** `lunate packages` runs
- **THEN** it prints `packages: none installed` and exits 0, and `--json` prints `[]`

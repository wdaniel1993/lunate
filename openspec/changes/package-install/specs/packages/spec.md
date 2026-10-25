## Purpose

Installing, updating and removing distributed packages: what a package has to contain, where its content lands for each scope, how an install is recorded and verified, and what the four `lunate` commands print. A package carries extensions and skills; it is never a second trust decision.

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

The package manifest SHALL accept `schema` (the integer `1`), `extension` (one package-relative directory) and `skills` (one package-relative directory), SHALL ignore unknown fields, and SHALL refuse a known field of the wrong type naming the field. Every path a manifest names, and every entry the archive yields, SHALL resolve inside the package root: a rooted path, a `..` segment or an entry whose resolved path leaves the root SHALL be refused, naming the entry. The `extension` directory SHALL contain an `extension.json` that parses under the existing manifest rules, whose `apiVersion` range the running host meets, and whose `entryAssembly` exists in that directory; a violation SHALL be refused with the existing wording (including `requires Lunate ≥ x.y` for a version above the host). Each immediate subdirectory of `skills` SHALL be one skill holding a `SKILL.md` whose frontmatter parses and whose name equals the directory name; a malformed one SHALL be refused, naming the directory and the reason, and two skills with one name SHALL be refused. A package that declares NuGet dependencies SHALL be refused, naming them, because a packaged extension is self-contained and a dependency that is not installed must not look installed. Folders outside the declared kinds — `lib/`, `build/`, `buildTransitive/` — SHALL NOT be installed and SHALL be named once in the output. A package SHALL be refused, before extraction, when it holds more than 20,000 entries or expands beyond 512 MiB, naming both the limit and the observed value.

#### Scenario: An entry that escapes the package is refused

- **GIVEN** a package whose manifest names `../../etc`
- **WHEN** it is installed
- **THEN** it is refused, naming the entry, and nothing is written outside the scope

#### Scenario: An extension this host cannot run is refused at install

- **GIVEN** a bundle whose `extension.json` declares `apiVersion` `2.0.0`
- **WHEN** it is installed on a `1.x` host
- **THEN** it is refused with `requires Lunate ≥ 2.0.0 but this is Lunate <version>`, and nothing is written

#### Scenario: A declared dependency is refused, not ignored

- **GIVEN** a bundle package that declares a NuGet dependency on `Octokit`
- **WHEN** it is installed
- **THEN** it is refused, naming `Octokit` and saying that a package must carry its own files

#### Scenario: Undeclared folders are ignored and named

- **GIVEN** a package that carries a `build/` folder beside its declared content
- **WHEN** it is installed
- **THEN** `build/` is not installed and one line names it as ignored

#### Scenario: A package beyond the bounds is refused before extraction

- **GIVEN** a package whose archive expands past 512 MiB
- **WHEN** it is installed
- **THEN** it is refused, naming the limit and the observed size, and no file is extracted

### Requirement: Install runs no package code and records no trust

`lunate install` SHALL download, verify and write files, and SHALL NOT load an assembly, run a factory, start a process, dispatch a hook or write any trust section of the trust store. A user-scope install SHALL be trusted the way a global extension and a user skill already are, so it prompts nothing. A project-scope install SHALL stay untrusted until the existing questions are answered: the project extension question for the extension directory and the project skills question for the installed skills. Install SHALL only change the pending set that `lunate trust` lists.

#### Scenario: A project install leaves the extension untrusted

- **GIVEN** a repository with no trust record and a project install of a bundle carrying an extension
- **WHEN** `lunate install --project` runs and then a session starts
- **THEN** the install wrote no trust record, the session asks the existing extension question naming the id, version and directory, and `/extensions` reads `trust-denied` until it is answered

#### Scenario: Install never runs the extension's code

- **GIVEN** a bundle whose extension factory writes a marker file
- **WHEN** `lunate install` runs and the marker path is checked
- **THEN** the marker is absent

#### Scenario: A project install shows up in the trust inventory

- **GIVEN** a project install of a bundle carrying an extension and two skills
- **WHEN** `lunate trust` runs
- **THEN** the extension and the skills appear among the pending decisions, and `lunate trust --yes` records them so a later session asks nothing

#### Scenario: A user install asks nothing

- **GIVEN** no existing trust records
- **WHEN** `lunate install Acme.Toolkit` runs at the user scope and a session starts
- **THEN** no question is asked and the extension loads

### Requirement: Where installed content lands

A user-scope install SHALL write under the state root: the record at `<root>/packages/<package-id>/install.json`, the extension at `<root>/extensions/<extension-id>/`, and each skill at `<root>/skills/<name>/`. A project-scope install SHALL write the same three kinds under `<worktree>/.lunate/`. Install SHALL NOT write to `~/.agents/skills`, SHALL NOT write under `<root>/cache/`, and SHALL NOT copy anything into the global packages folder beyond the nupkg the download put there. The package-id directory name SHALL be the lowercased package id, because NuGet ids are case-insensitive and filesystems may be too, while the record SHALL carry the canonical spelling. With `LUNATE_HOME` set, every user-scope path SHALL resolve under it and nothing SHALL be written under the profile, and the existing store guard SHALL run before the first write, exactly as it does for every command that writes the store.

#### Scenario: A user install follows the state root

- **GIVEN** `LUNATE_HOME` set to an absolute temporary directory
- **WHEN** a user-scope install runs
- **THEN** the record, the extension and the skills are all under that directory and nothing is written under the profile

#### Scenario: A project install writes only under the worktree

- **GIVEN** a repository worktree with no `.lunate/` directory
- **WHEN** `lunate install --project` runs
- **THEN** every written path is under `<worktree>/.lunate/`, the record included

#### Scenario: The shared skill convention is untouched

- **GIVEN** a user install of a bundle carrying skills
- **WHEN** `~/.agents/skills` is compared before and after
- **THEN** it is unchanged

#### Scenario: The build cache is untouched

- **GIVEN** a user install
- **WHEN** `<root>/cache/` is compared before and after
- **THEN** it is unchanged, and `lunate cache prune` leaves the install alone

#### Scenario: A store inside the workspace is refused

- **GIVEN** `LUNATE_HOME` set to a directory inside the worktree
- **WHEN** `lunate install` runs
- **THEN** it fails with `LUNATE_HOME points inside the workspace; trust records there could be written by the repository` and nothing is written to that path

### Requirement: The install record

Each installed package SHALL have one record at `<scope>/packages/<package-id>/install.json` with two halves: `package` — the sources used, the canonical package id, the exact version, the nupkg's SHA-512 and the archive's file list — and `content` — the extension id and version when one was installed, the skill names, and every written path relative to the scope root with its size and SHA-256. It SHALL also carry the scope, the install time (ISO 8601, invariant) and an integrity hash over its own canonical bytes, computed with that field empty. Every path in the record SHALL be relative to the scope root; a record naming an absolute path or a path that resolves outside the root SHALL be refused as `broken`. A record whose integrity hash does not answer for its contents SHALL be reported as `broken` and SHALL NOT be used to uninstall, update or trust anything, except that `--force` may act on one whose paths are all confined. Writing SHALL be atomic per file, and the record SHALL be written last, so a record that exists names content that exists. There SHALL be no separate lock file: the single-file lock exists so a compile never re-resolves, and nothing here compiles.

#### Scenario: The record names everything that was written

- **GIVEN** a completed install of a bundle carrying an extension and two skills
- **WHEN** the record is read
- **THEN** `content` lists the extension directory and every file under it, both skill directories and their files, each with a size and a hash

#### Scenario: A hand-edited record is refused

- **GIVEN** an installed package whose record's version is edited by hand
- **WHEN** `lunate packages` and `lunate uninstall` run
- **THEN** the package reads `broken` and the commands refuse to act on the record, naming it

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

### Requirement: Pinning and update

An install SHALL record the exact version it resolved and SHALL never re-resolve on its own. Installing a different version of an installed package SHALL behave exactly like a pinned update: staging, replace, record last. A bare `install` or `update` SHALL resolve the latest stable version only; an explicit prerelease version SHALL be accepted when named exactly. There SHALL be no automatic update, no background version check and no version check at session start. `lunate update <id>` SHALL resolve the latest stable version on the configured feeds, download it, and replace the installed content; `lunate update <id>@<version>` SHALL move to that exact version, including a downgrade; `lunate update --all` SHALL update every package in the selected scope. An update SHALL download and verify into a staging directory, replace the recorded paths, and write the record last, so a failure at any point before the record write leaves the previous install's content and record byte-identical. A package that is no longer on any feed SHALL fail the update, naming the package and the feeds tried, and SHALL remove nothing. An update that would collide under the scope rules SHALL be refused before anything is replaced.

#### Scenario: An update replaces the content and the record

- **GIVEN** `Acme.Toolkit@1.4.2` installed
- **WHEN** `lunate update Acme.Toolkit` finds `1.5.0`
- **THEN** the content and the record name `1.5.0`, and the record's integrity hash answers for the new bytes

#### Scenario: A failed update keeps the previous install

- **GIVEN** `Acme.Toolkit@1.4.2` installed
- **WHEN** an update fails while downloading
- **THEN** the installed content and the record are unchanged, and the failure names the package and the reason

#### Scenario: A vanished package removes nothing

- **GIVEN** an installed package that the feed no longer serves
- **WHEN** `lunate update` runs for it
- **THEN** it fails naming the package and the feeds tried, and the install is untouched

#### Scenario: A pinned update may go backwards

- **GIVEN** `Acme.Toolkit@1.5.0` installed
- **WHEN** `lunate update Acme.Toolkit@1.4.2` runs
- **THEN** `1.4.2` is installed and recorded, with no question asked

#### Scenario: Installing a different version is an update

- **GIVEN** `Acme.Toolkit@1.4.2` installed
- **WHEN** `lunate install Acme.Toolkit@1.5.0` runs
- **THEN** `1.5.0` replaces the content and the record under the same staging and commit rules as an update

### Requirement: Uninstall

`lunate uninstall <package-id | extension-id>` SHALL delete exactly the paths the record lists and the record's own directory, then remove the directories the install created that are now empty, and SHALL NOT delete anything else. It SHALL accept an extension id the record names when exactly one record matches, and SHALL refuse an ambiguous one, naming the candidates. It SHALL refuse, naming the files, when a recorded file's hash no longer matches, and SHALL proceed with `--force`. Files inside a directory the package created but did not write SHALL be left and named, with a line saying the directory is not empty and where it is. Trust records SHALL NOT be touched: trust is a decision about bytes at a path, so removing the content does not revoke it and an identical reinstall keeps it. A session that already loaded the extension SHALL keep it until the process exits; the next session sees the content gone.

#### Scenario: Uninstall removes what was written and nothing else

- **GIVEN** an installed bundle carrying an extension and two skills
- **WHEN** `lunate uninstall Acme.Toolkit` runs
- **THEN** the extension directory, the two skill directories and the record are gone, and the nupkg in the packages folder is untouched

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

Install and update SHALL be all-or-nothing per command: the record is the commit point, and a failed command SHALL leave no content that no record names. Uninstall SHALL delete the record last, so a failed uninstall still leaves a record that describes what remains. `--offline` SHALL resolve only from the global packages folder, SHALL make no feed request, and SHALL fail naming the package and the packages folder (`NUGET_PACKAGES` when set, the default otherwise) when the exact version is not there. Feeds and credentials SHALL come from NuGet's own configuration chain rooted at the working directory, so a repository's `nuget.config` applies to a project install and the user's applies to a user install; there SHALL be no interactive credential prompt. An authentication failure SHALL name the source and the configuration file to fix. An id that no configured feed serves, or a version that id does not publish, SHALL fail naming the id, the version and the feeds tried, and SHALL write nothing. `--force` SHALL repair an install whose content is partial or changed by rewriting the recorded paths from the recorded version's nupkg.

#### Scenario: Offline installs from a warm packages folder

- **GIVEN** the exact nupkg already in `NUGET_PACKAGES` and the observer on the feed seam
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

`lunate install` SHALL print, before writing: the package id in its canonical spelling, the exact version, the source it came from, the nupkg's SHA-512, the size, the extension id and version when one is carried, and the skill names — then one line saying what was written and where. Install SHALL print nothing to stdout on a failure, the reason on stderr, and exit 1; when the package's identity was resolved before the failure, the reason SHALL carry the id, the version and the source. A usage error SHALL exit 2; a successful install and an already-installed version SHALL exit 0. `--dry-run` SHALL print the same identity and contents, write nothing, and exit 0. `--json` SHALL print one object carrying the same fields, canonical newlines, invariant numbers and no colour. `lunate update` and `lunate uninstall` SHALL follow the same printing and exit-code rules for what they did, with `--json` printing one object naming what changed.

#### Scenario: Dry-run prints the identity and writes nothing

- **GIVEN** a bundle reachable on a configured feed
- **WHEN** `lunate install --dry-run` runs
- **THEN** the id, version, source, SHA-512, extension id and skill names print, and no file or record is written

#### Scenario: A refusal carries the identity it resolved

- **GIVEN** a bundle whose skills would collide with an existing skill directory
- **WHEN** `lunate install` runs
- **THEN** stderr names the package, the version and the colliding directory, stdout is empty, the exit code is 1, and nothing is written

#### Scenario: JSON carries the same fields

- **GIVEN** a completed install
- **WHEN** the same install runs a second time with `--json` and nothing has changed
- **THEN** the object carries the id, version, source, SHA-512, extension id, skill names, scope, the written paths and that it was already installed

### Requirement: The `lunate packages` view

`lunate packages [--json] [--verify]` SHALL list every installed package this working directory can see — the user scope and the current worktree's project scope — one line each, ordered by scope then package id, carrying the canonical id, the version, the scope, and what it provides (the extension id and the skill names). `--verify` SHALL recompute each recorded file's hash and append the count of changed and missing files, and SHALL exit 3 when any record is `broken`, lists a file whose hash changed, or names content that is gone; without `--verify` the command SHALL hash nothing. `--json` SHALL print one array of objects carrying the line's fields plus `installedAt`, `verified` and the changed and missing counts. With nothing installed it SHALL print `packages: none installed` and `--json` SHALL print `[]`. A store that cannot be read SHALL exit 1 with the reason on stderr; a usage error SHALL exit 2.

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

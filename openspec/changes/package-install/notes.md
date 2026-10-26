# package-install — phase 6 run notes

The end-to-end run task 6.4 asks for, recorded from the real commands. The feed is the test suite's
fixture folder (`PackageFeedFixture`: a directory feed plus a `nuget.config` naming it, no network),
the bundle carries one tool (`echo`, registered by the built `ToolExtension` fixture), one skill
(`greek`) and one dependency (`Acme.Lib`, a `lib/net10.0` package in the same feed). Session records
are `<tmp>/home/.lunate`; the temporary root is printed as `<tmp>` below only to keep the lines
readable — every other byte is the command's own output.

## User scope: install, session, verify, uninstall

```text
$ lunate install Acme.Toolkit@1.4.2
package: Acme.Toolkit 1.4.2
source: local
sha512: Efm4xwEqACCPBevUQa97vs0ikyW4fyv+fSumfowMWlNjIasjRpi53teyDnh0qLWMm4WkXwHZ8pJqMgBYH+2ujw==
archive: 5 files, 12987 bytes expanded
extension: acme-tools 0.1.0 (apiVersion 1.11.0, entry acme-tools.dll)
skills: greek
dependencies: Acme.Lib 1.0.0
scope: user
content: 12.2 KiB in 4 files
installed: 4 files under '<tmp>/home/.lunate' (record '<tmp>/home/.lunate/packages/acme.toolkit/install.json')

session trust question: from package Acme.Toolkit@1.4.2 (local)
tools offered: read, write, edit, bash, acme-tools__echo
skills: greek (user, from Acme.Toolkit@1.4.2) — The greek skill.

$ lunate packages --verify
exit 0
packages: Acme.Toolkit 1.4.2 — user, extension acme-tools 0.1.0, skills greek, verified

$ lunate uninstall Acme.Toolkit
package: Acme.Toolkit 1.4.2
scope: user
removed: 4 files under '<tmp>/home/.lunate'
record: '<tmp>/home/.lunate/packages/acme.toolkit/install.json' removed

after uninstall:
  extensions/acme-tools exists: False
  skills/greek exists: False
  record exists: False
```

The tool arrives namespaced by the extension (`acme-tools__echo`), which is the existing naming rule
for extension tools; the skill line carries `from Acme.Toolkit@1.4.2` from the `/skills` view. The
dependency assembly the walk materialised is one of the four removed files (`Acme.Lib.dll` beside the
entry assembly), so `uninstall` takes it too.

## Project scope: committed content, local record, one question

```text
$ lunate install Acme.Toolkit@1.4.2 --project
package: Acme.Toolkit 1.4.2
source: local
sha512: D0zgGo+1U0khlgi6nyBX7GPNlunIzW6wcaLPvXvwSPVRWwBwXOH9C9QRoDkDdMaBpAteYCxTrpi9tPTxP5LKtw==
archive: 5 files, 12987 bytes expanded
extension: acme-tools 0.1.0 (apiVersion 1.11.0, entry acme-tools.dll)
skills: greek
dependencies: Acme.Lib 1.0.0
scope: project
worktree: <tmp>/worktree
content: 12.2 KiB in 4 files
installed: 4 files under '<tmp>/worktree' (record '<tmp>/home/.lunate/packages/acme.toolkit/install.json')

$ git status --porcelain
?? .agents/
?? .lunate/
?? nuget.config

record outside the worktree: True
project session trust question: from package Acme.Toolkit@1.4.2 (local)
project tools offered: read, write, edit, bash, acme-tools__echo
skills: greek (project, from Acme.Toolkit@1.4.2) — The greek skill.
```

The content is in the worktree and shows up as untracked additions; the record lives under the state
root and names the worktree. The extension question carries the provenance line, and after approval
the session loads the extension with its tool and lists the skill (the project skills question is the
second one, answered in the live area for this run).

## The session gate

```text
$ LUNATE_SESSION=1 lunate install Acme.Toolkit@1.4.2
exit 2
stdout: (empty)
stderr: lunate: this install runs inside a Lunate session, so it needs --yes — an install started from a
command puts code on the machine that runs at the next load, and that has to be visible in the command
you approved. Run 'lunate install Acme.Toolkit@1.4.2 --yes', or run the install outside the session.
```

## A reinstall over a hand-broken record

```text
$ lunate install Acme.Toolkit@1.4.2      (record hand-broken, one file a previous version wrote)
package: Acme.Toolkit 1.4.2
source: local
sha512: Tipm/GG2LBybh0/FEm3p2x+XzKSkYgzwslFLUpZuxRy8Ao9Q6oVlw0BvgE1lj3k2ka702n5CiHavIpY/Ls208w==
archive: 5 files, 562 bytes expanded
extension: acme-tools 0.1.0 (apiVersion 1.11.0, entry acme-tools.dll)
skills: greek
dependencies: none
scope: user
content: 188 B in 3 files
installed: 3 files under '<tmp>/home/.lunate' (record '<tmp>/home/.lunate/packages/acme.toolkit/install.json')
stderr:
lunate: the record file '<tmp>/home/.lunate/packages/acme.toolkit/install.json' is not valid JSON: 't' is an
invalid start of a property name. Expected a '"'. LineNumber: 0 | BytePositionInLine: 2. This run replaced the
content and wrote a new record; bytes a previous version wrote are left where they are rather than removed by name.
lunate: left in place: '<tmp>/home/.lunate/extensions/acme-tools/old-dependency.dll' — no readable record
named it, so nothing removed it.
lunate: a previous version may have written skill directories this record cannot name;
'<tmp>/home/.lunate/skills' was not swept.
```

The install succeeds, exits 0, writes a record that answers for itself (`packages --verify` is clean
afterwards), and says exactly what it could not account for instead of claiming to have removed it.

## What the run exercises

| Task | Evidence |
| --- | --- |
| 6.4 | the four blocks above: install → session (tool + skill provenance) → verify 0 → uninstall, and the `--project` variant |
| 6.8 | the gate block: exit 2, `--yes` named, nothing written |
| 6.9 | the reinstall block: a broken record replaced, `--verify` clean, the leftover named |
| 6.10 | `content: 12.2 KiB in 4 files` on both scopes (the >10 MiB warning is covered by its own test; the fixture here is too small to warn) |
| 6.11 | the user-scope trust question with `from package Acme.Toolkit@1.4.2 (local)`, and the project one |

The same assertions run as tests: `PackageInstallEndToEndTests` (both scopes),
`InstallCommandTests` (gate, repair, size), `UninstallCommandTests` (broken record),
`PackageTrustTests` (user-scope pending, hand-placed placement).

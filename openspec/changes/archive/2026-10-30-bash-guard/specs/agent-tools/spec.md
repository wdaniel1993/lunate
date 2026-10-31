## ADDED Requirements

### Requirement: Bash guard
A `bash` command SHALL run wrapped in a guard profile, not a jail: writes under the state root (`LUNATE_HOME`, default `~/.lunate`) and under the install prefix SHALL be denied at the OS level, `auth.json` under the state root SHALL be read-denied, and every other access SHALL keep its ordinary behaviour (normal work inside and outside the workspace survives the guard). The mechanism is per platform: `sandbox-exec` on macOS, `bwrap` on Linux with `setpriv`'s Landlock rules as the Linux fallback; Windows has no mechanism, and the tool SHALL say so in its result rather than run the command unconfined silently. Every resolution SHALL run the mechanism once before use, so "installed" cannot stand in for "working": when the mechanism cannot start — including the AppArmor restriction of unprivileged user namespaces on Ubuntu 23.10+ — the report SHALL carry the probe's output and the restriction's name, and the command SHALL run unconfined behind a visible notice. When a mechanism enforces less than the profile, the resolution SHALL report that instead of falling back silently. A command whose redirection would write a denied target SHALL be refused before the spawn with a result naming the guard and its mechanism; a failed command that touched a denied target and ended in a filesystem denial SHALL carry a hint that names the guard, marked as best-effort.

#### Scenario: A state-root write is refused and never lands
- **GIVEN** a guarded shell (the mechanism resolved and probed)
- **WHEN** a command redirects into a file under the state root
- **THEN** the result is an error naming the guard and its mechanism, and the file does not exist

#### Scenario: A normal command is untouched
- **GIVEN** a guarded shell
- **WHEN** a command writes inside the workspace, reads its tools, or starts a child
- **THEN** the command behaves as it would unguarded, apart from the wrapping cost

#### Scenario: Reading auth.json is denied
- **GIVEN** a guarded shell and credentials under the state root
- **WHEN** a command reads `auth.json`
- **THEN** the read fails with a filesystem denial (or sees the guard's substitute), the result carries the guard hint, and the secret's text does not reach the result

#### Scenario: normal access survives outside the denied targets
- **GIVEN** a guarded shell
- **WHEN** a command reads a file or a directory that is not a denied target
- **THEN** the access succeeds under every mechanism, the Landlock fallback included

#### Scenario: A compliant command that fails a filesystem gate names the guard
- **GIVEN** the tool result failed with a filesystem denial and the command touched a denied target
- **WHEN** the result is composed
- **THEN** a best-effort hint naming the guard and mechanism is appended

#### Scenario: An unavailable mechanism is reported, never assumed
- **GIVEN** a Linux host where user namespaces are restricted (`apparmor_restrict_unprivileged_userns=1`)
- **WHEN** the first bash command resolves the guard and the mechanism's probe fails
- **THEN** the result names the restriction and the probe's output, states the command runs unconfined, and the same command still produces its honest result


# Agent tools delta — optional search tools

## ADDED Requirements

### Requirement: Optional search tools

When configured, three read-only tools SHALL be registered beside the core set: `grep` (regex search with `path`, `glob`, `case_sensitive` and `max_results` of 100 default / 500 cap, using the ripgrep binary when present and an internal walker otherwise, both honouring `.gitignore` and skipping binary files), `find` (glob matching with `path`, `type` of `file` or `dir`, 100/500 bounds, case-insensitive on Windows and macOS, case-sensitive on Linux) and `ls` (one directory listing with `path` and `depth` of 1 default / 3 max, directories first then files, `.git` always skipped). All three SHALL refuse a path outside the workspace exactly as `read` does, SHALL require no approval, SHALL follow no directory symlink and SHALL route their output through the existing truncation path. Their results SHALL name what was searched and SHALL end with a `[N more …]` line when a cap cut the result. When the configured list is empty — the default — none SHALL be registered, and the tool set SHALL be exactly the core set.

#### Scenario: Nothing extra by default

- **GIVEN** a settings file without `tools.extra`
- **WHEN** the harness is built
- **THEN** only the core tools are registered and no `grep`, `find` or `ls` exists

#### Scenario: A configured list registers exactly its tools

- **GIVEN** `tools.extra` set to `["grep", "ls"]`
- **WHEN** the harness is built
- **THEN** `grep` and `ls` are registered, `find` is not, and a `bash` name in the list is rejected with the accepted values

#### Scenario: grep honours gitignore and the cap

- **GIVEN** a workspace whose `.gitignore` excludes `bin/` and one file with 600 matching lines
- **WHEN** `grep` runs with a 100 default
- **THEN** nothing from `bin/` appears, exactly 100 matches are returned and the result ends with `[500 more matches]`

#### Scenario: A path outside the workspace is refused

- **GIVEN** a `grep` call with `path: "../.."`
- **WHEN** the workspace checks it
- **THEN** the call is refused naming the path, the resolved target and the allowed roots, and no file is read

#### Scenario: ls skips .git at every depth

- **GIVEN** a repository root and `depth: 3`
- **WHEN** `ls` renders the tree
- **THEN** no `.git` entry appears and directories precede files at each level

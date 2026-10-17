# extensions — delta for bash-change-marks

## MODIFIED Requirements

### Requirement: File change bus

The core SHALL emit exactly one `FileChanged` notification after every successful file mutation performed by the `write` and `edit` tools; refused or failed mutations SHALL emit nothing. Each notification SHALL carry the canonical absolute path, the workspace-relative path, the workspace id, the run id when the mutation ran inside a run, and the change kind (`created` or `modified`), and the source (`tool` or `bash`); a write whose content is identical to the existing file still emits (`modified`). The workspace id SHALL be the run's `WorktreeRoot`. Extensions SHALL subscribe with a handler interface and an optional `*`/`?` pattern filter matched against the absolute path. Delivery SHALL be ordered per workspace and SHALL never throw into the mutation path; handler failures follow the hook failure policy (report); an event SHALL be observable only after the file write has completed — a subscriber reading the file on delivery SHALL see the new content. A flush SHALL complete when every notification enqueued so far has been dispatched, without beginning the drain — notifications arriving afterwards are still delivered. Once a drain begins, further notifications SHALL be dropped and logged. Limits: after each bash call the host SHALL diff the workspace's `git status` against the previous scan of the same run and publish every path that really changed through the same sink — git worktrees only; untracked files included; untracked directories, `.gitignore`d files and submodule dirty state excluded; an already-dirty file is not re-published without a real change, a revert is published and a commit is not; a scan that cannot complete (timeout, no repository) leaves the content-stamp scan as the backstop. Extension and MCP tools that write files publish through their `ToolContext` file-change sink themselves — the same mechanism the core tools use. No `deleted` kind exists (no tool deletes files).

#### Scenario: Events after write and edit

- **GIVEN** a subscribed extension
- **WHEN** `write` and `edit` mutate files successfully
- **THEN** one event per mutation arrives with the canonical path and the workspace id

#### Scenario: No event for a refused mutation

- **GIVEN** an `edit` that is refused (no match or ambiguous)
- **WHEN** it returns
- **THEN** no event is emitted

#### Scenario: Ordering under the queue

- **GIVEN** concurrent queued mutations on one file
- **WHEN** they complete
- **THEN** events arrive in the same order as the mutations applied

#### Scenario: A notification carries the full path set and kind

- **GIVEN** a subscribed extension
- **WHEN** `write` creates a file and `edit` modifies it
- **THEN** the events carry the absolute path, the workspace-relative path, the worktree-root workspace id, the run id, and the kind (`created` then `modified`)

#### Scenario: An identical-content write still emits

- **GIVEN** a subscribed extension
- **WHEN** `write` writes content identical to the file's present content
- **THEN** one event with kind `modified` arrives

#### Scenario: A subscriber reads the new content

- **GIVEN** a subscriber that reads the file when an event arrives
- **WHEN** a `write` completes
- **THEN** the read observes the new content

#### Scenario: A flush observes delivery without ending it

- **GIVEN** a subscribed extension and a dispatched notification
- **WHEN** a flush completes
- **THEN** the subscriber has seen the notification and a later mutation still delivers

#### Scenario: Events after a drain begins are dropped

- **GIVEN** a drain in progress
- **WHEN** a mutation completes
- **THEN** no event is delivered and the drop is logged

#### Scenario: A bash call publishes only real changes

- **GIVEN** a git worktree with an already-dirty file and a run whose first bash call has baselined
- **WHEN** a bash call changes one other file
- **THEN** exactly that file is published (source `bash`), and the already-dirty file emits nothing

#### Scenario: Reverts are published and commits are not

- **GIVEN** a baselined run with a dirty file
- **WHEN** the file is reverted (`git checkout --`) and a later bash call runs
- **THEN** the revert is published; when a commit instead leaves the content unchanged, nothing is published

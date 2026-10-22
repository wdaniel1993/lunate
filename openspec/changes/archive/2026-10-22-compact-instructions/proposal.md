## Why

`/compact` summarises with one fixed prompt: goal, decisions, files touched, open problems. That is the right default, but it cannot know what this session is *for*. A user debugging a flaky test and a user half-way through a design discussion both get the same summary, and the detail they care about is the detail that gets dropped.

Every comparable agent lets the user steer the summary. The mechanism is small: the text after `/compact` is handed to the summarizer as focus, for that compaction only.

## What Changes

- `/compact <instructions>` passes the text to the summarization call as focus instructions; the fixed sections stay, so the summary still covers goal, decisions, files and open problems.
- The instructions apply to **that compaction only**. They are not persisted in the session, not stored with the compaction entry, and not replayed on resume.
- An empty `/compact` behaves exactly as today.
- The instructions are capped (1,000 characters) with a notice when the argument is longer, rather than being truncated silently.
- The instructions travel as an explicit argument through the compaction path; the `IChatReducer` entry point, which has no room for them, passes `null` and keeps the fixed prompt.

## What Does Not Change

- The compaction trigger, the kept-tail boundary, tool-pair expansion and the session entry shape are untouched; the `compaction` entry still records `summary` and `replaces` only.
- Automatic compaction keeps using the fixed prompt: there is no way for a session to smuggle standing instructions into every later summary.
- The summary model is still the session's model.

# ADR review — debug-command

ADR review completed for this change.

**In-force ADR context reviewed:** 0003 (own loop on `IChatClient`), 0012 (tool contract and
declaration adapter), 0013 (agent loop), 0015 (session format), 0016 (workspace boundary), 0017
(extensibility architecture and its addenda), 0018 (session schema 2 and its addenda), 0021 (per-run
workspaces), 0022 (model catalog and provider auth). The credential-redaction rule this requirement
states already lives in the export and session paths that those decisions constrain; this change
only writes the rule down for `/debug`.

**No major durable architectural decisions were introduced, so no repository-level ADR file was
created.** This change is documentation only: it records behaviour that merged in #275, and the
archive of its delta into `openspec/specs/tui/spec.md` is its only implementation step. No source,
test, dependency or session-format change follows from it.

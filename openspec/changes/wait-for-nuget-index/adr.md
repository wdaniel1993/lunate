# ADR review — wait-for-nuget-index

ADR review completed for this change.

**In-force ADR context reviewed:** 0005 budget calibration (and its addendum), 0008 release publish
flags, 0011 repo quality tooling, 0017 extensibility architecture (and its package addendum), 0021
per-run workspaces, 0022 model catalog and provider auth. None of them constrains how the smoke
waits for a feed: the change touches CI workflow steps and gate scripts, not the CLI's architecture,
its layering, or its release artifacts.

**No major durable architectural decisions were introduced, so no repository-level ADR file was
created.** The decisions in `design.md` are tactical and local to the release smoke: wait on the
feed's own resources rather than retrying the install, one ten-minute budget for the three
resources, a helper script with fixture-testable feed bases, and a per-resource failure report. They
are reversed by reverting one workflow step and one script, and no future change inherits a
commitment from them.

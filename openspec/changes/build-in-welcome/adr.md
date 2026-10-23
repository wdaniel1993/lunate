## ADR: the welcome header names the running build

**Status:** accepted (2026-10-23)

**Context.** The header showed `v0.1.0`; the maintainer could not tell two builds apart and reported a stale install twice while already current.

**Decision.** The header shows `v<semver>+<7-char hash>`. `--version` keeps the full informational version; the footer keeps the semver. The mapping lives in one pure function (`ProductVersion.BuildForm`) with table-driven tests.

**Consequences.** The header answers "which build am I running" without a second command. Golden fixtures that contain the header must normalise the token; the end-to-end golden does.

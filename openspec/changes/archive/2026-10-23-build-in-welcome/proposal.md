## Why

The welcome header prints `lunate v0.1.0` with nothing that identifies the running binary. The maintainer, testing builds from the Mac mini, twice reported "still looks like an old version" while already on the newest build — the header could not answer the only question he had. `--version` already prints the full informational version including the commit, but nobody reads it before typing.

## What Changes

- The welcome header's version token becomes the display version plus the short build hash: `v0.1.0+0c96dc6`.
- A build without a hash (a working tree with no commit metadata) keeps the plain display form; the header never advertises a commit it cannot name.
- `--version` and the footer keep the plain semver form: the header is the place where "which build is this" is answered.
- The end-to-end scrollback golden normalises the build token the same way it already normalises the workspace path, so a golden does not go stale on every commit.

Non-goals: no build date, no branch name, no dirty marker — the hash answers the question that was actually asked.

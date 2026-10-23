## Context

`ProductVersion` reads the assembly's informational version once. It already exposes two forms: `Informational` (with `+sha`, what `--version` prints) and `Display` (semver only, what the header showed).

## Decision

Add a third form, `Build`: `Display` + `+` + the first seven characters of the hash, produced by a pure function `BuildForm(string)` that the tests pin directly. The header takes `Build`; nothing else changes.

Seven characters is the git default abbreviation and what every forge shows; a longer prefix costs header width for no legibility gain. When the informational version carries no `+metadata`, or the metadata is shorter than seven characters, `BuildForm` returns the input unchanged — a developer build says `v0.1.0`, which is honest.

## Alternatives considered

- **Print the informational version verbatim in the header.** Correct, but 40 characters of hash in a header that shares its line with the wordmark; the short form carries the same information at a glance.
- **Show the build only in `--version`.** That is the status quo the maintainer already failed to use.
- **Environment variable to override the header version.** No: the point is that the header cannot lie about the build it is.

## Risks

The header is inside the startup budget gate; the extra string work is a substring on a value computed once at type initialisation, so the cost is in the noise. The end-to-end golden would churn per commit without normalisation, which is why that lands in the same change.

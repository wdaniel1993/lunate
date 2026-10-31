## Why

The v0.1.0 release smoke failed on all three operating systems at the same step: `lunate install
Lunate.Extensions.Subagents@0.1.0` could not download the package, five attempts over about 3.3
minutes, minutes after the Release job had pushed it. nuget.org had not finished indexing the fresh
push; the same install succeeded by hand ten minutes later. The fixed retry loop turned an indexing
lag into an opaque failure, and it would have hidden a genuinely missing package behind the same
message.

## What Changes

- A new helper, `scripts/wait-for-nuget.sh`, waits on the feed itself before an installer asks for a
  package: it polls the flatcontainer index, the package content and the registration resource — the
  three resources a package client reads, in that order — under one budget (600 s by default,
  probing every 10 s). It exits 0 once all three name the version, and exits 1 with the feed state
  named for each resource when the budget runs out. Both feed bases are flags, so a `file://`
  fixture stands in for nuget.org in tests.
- `smoke.yml` gains a step that runs the wait before the extension install, with the budget of 600 s,
  and the install step loses the blind retry loop: it installs once and, on failure, prints the
  client's error and says that the feed had listed the version — so a failure there names a real
  defect rather than an indexing lag.
- `scripts/gate-tests.sh` proves the helper against fixture feeds: a version indexed in the middle
  of the budget, a version that never appears, and a version whose content is missing.
- `docs/distribution.md` names the wait in the release-smoke section, so the runbook says what the
  smoke waits for and what a budget exhaustion means.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `distribution`: the release-smoke requirement gains the wait for the feed to index the tag's
  package version, the budget that bounds it, and the report that names the feed state when the
  budget runs out.

## Impact

- `.github/workflows/smoke.yml` (a new step, a rewritten install step), `scripts/wait-for-nuget.sh`
  (new), `scripts/gate-tests.sh` (new fixture-fed cases), `docs/distribution.md`.
- No product code and no public API: the smoke drives the published binary, it does not change it.
- The smoke step needs `curl` and `jq`, both already on every runner image and already installed by
  the verify job's Windows step; the helper refuses with exit 2 rather than guessing when either is
  missing.

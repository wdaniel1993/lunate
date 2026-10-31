## 1. The wait helper

- [x] 1.1 Add `scripts/wait-for-nuget.sh`: `--package`, `--version`, `--budget-seconds` (default
      600), `--interval-seconds` (default 10), and the two feed bases as the test seam; polls the
      flatcontainer index, then the package content, then the registration resource, and exits 0
      only when all three name the version.
- [x] 1.2 Report per resource: on every poll and again when the budget runs out, print what the
      index served (version count, newest version, whether the version is absent), whether the
      `.nupkg` is downloadable, and what the registration pages carry, so a maintainer can tell an
      indexing lag from a missing package.
- [x] 1.3 Refuse to run without `curl` or `jq` (exit 2, named dependency), and reject a
      non-numeric budget or interval.

## 2. The smoke

- [x] 2.1 `smoke.yml`: add a step before the extension install that runs the helper with a 600 s
      budget for the tag's version.
- [x] 2.2 `smoke.yml`: drop the five-attempt retry loop from the install step; install once and, on
      failure, print the client's error plus the fact that the feed had listed the version.
- [x] 2.3 Keep the stable-tag bare-install step as it is: the version gate already proves the
      registration resolution it depends on.

## 3. Tests

- [x] 3.1 `scripts/gate-tests.sh`: fixture feed where the version appears mid-budget — the helper
      waits, then succeeds, and the linked registration page is read.
- [x] 3.2 `scripts/gate-tests.sh`: fixture feed where the version never appears — the helper fails,
      and the report names the version, the absence from the index, and the resources it did not
      check.
- [x] 3.3 `scripts/gate-tests.sh`: fixture feed where the index lists the version but the `.nupkg`
      is missing — the helper fails naming the content.
- [x] 3.4 Run the fixture cases against a real run of the helper for the published `0.1.0` (live
      feed) and for a version the feed does not carry, to confirm the success and exhaustion paths.

## 4. Documentation and archive

- [x] 4.1 `docs/distribution.md`: say in the release-smoke section that the workflow waits for the
      feed to index the tag's version, that the budget is about ten minutes, and that exhaustion
      names the feed state rather than a retry count.
- [x] 4.2 `CHANGELOG.md`: record the fix under the next release.
- [x] 4.3 `openspec validate wait-for-nuget-index --strict` passes before the archive.

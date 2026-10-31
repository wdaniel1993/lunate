## Context

The release smoke installs `Lunate.Extensions.Subagents@<tag>` from nuget.org minutes after the
Release job pushes it. nuget.org indexes a fresh push asynchronously: the flatcontainer index, the
package content and the registration resource become available at different moments, minutes apart.
The v0.1.0 smoke asked the installer immediately and retried five times over about 3.3 minutes; all
three operating systems failed, and the same install worked by hand ten minutes later.

The smoke is a verification step, not a service: it runs once per tag, on three runners, with no
state to preserve and no user waiting on it. Its only jobs are to pass when the release is
installable and to fail with a reason a maintainer can act on.

## Goals / Non-Goals

**Goals**

- Pass when the tag's package is fine and the feed was merely slow.
- Fail, with a named feed state, when the package is genuinely not there.
- Never install against a feed the smoke has not just seen serve the version.
- Prove the wait without the network, so the gate does not depend on nuget.org's timing.

**Non-Goals**

- Making `lunate install` itself wait for indexing: the client reports what the feed serves, and a
  user who asks for a package that is not there should hear that immediately.
- Retrying the install: after the wait, a failure is a real defect, and a retry would hide it.
- Speeding up nuget.org, or depending on its search endpoint (a different, slower index).

## Decisions

**Gate on the feed's resources, in the order a client reads them, instead of retrying the install.**
The flatcontainer index answers "does this id exist and list this version", the package content
answers "is the `.nupkg` downloadable", and the registration resource answers "does version
resolution see it". A package client needs all three, and they lag independently; waiting on the
last one alone would let the smoke pass while the download still 404s. Each gate is only probed once
the previous one passed, so a dead tag costs one request per poll rather than three.

**One budget, shared by all three gates, ten minutes.** The observed lag is minutes; ten minutes is
long enough for a slow indexing run and short enough that a maintainer waiting on the smoke knows
when to stop. A per-gate budget would let a stuck feed spend three times that. The wait polls every
10 s, which bounds detection latency without hammering the feed.

**A helper script, `scripts/wait-for-nuget.sh`, not inline workflow YAML.** The loop is the part
worth testing, and workflow YAML cannot be run locally. The script takes both feed bases as flags,
so `scripts/gate-tests.sh` can point it at a `file://` fixture and prove the three outcomes (late
index, never indexed, content missing) without the network — the same seam `install.sh` already
exposes through `LUNATE_INSTALL_BASE_URL`.

**The install step runs once, after the wait.** Keeping a short retry "just in case" would leave the
old failure message reachable and make the wait's guarantee untestable. A failure after the wait
prints the client's error plus the fact that the feed had listed the version, which points a
maintainer at the client rather than at nuget.org.

**Report per resource, not one boolean.** On exhaustion the step prints what each resource served:
the index's version count and newest version, whether the content URL answered, how many versions
the registration pages carry, or the transport error. That is the difference between "the tag was
never pushed" (index 404) and "the feed is mid-indexing" (index lists a version, content or
registration does not).

Alternatives considered: a longer fixed sleep (blinds the run for ten minutes even when the feed is
ready); more retries (the failure stays opaque, which is the bug); `dotnet nuget locals` cache
warming (warms the wrong cache — the runner's, not the feed's); querying the search endpoint
(`azuresearch-usnc.nuget.org` lags further behind than the resources a client reads).

## Risks / Trade-offs

- [A genuinely dead tag now costs ten minutes per runner] -> The failure names the feed state, and
  the runbook says a missing version is the answer for a tag cut before the package existed.
- [The smoke depends on `curl` and `jq` on all three runners] -> Both ship on every GitHub runner
  image, the verify job already installs `jq` on Windows, and the helper exits 2 with a named
  dependency rather than guessing.
- [`jq` may be absent from a maintainer's machine] -> The helper says so; the fixture tests run in
  CI where it is installed.
- [The registration page walk could miss a version spread over many pages] -> The walk reads every
  linked page, not just the first, and the index of a package this young has one page.
- [Windows `file://` paths differ] -> The fixture test converts its paths with `cygpath -m` when
  present, the seam `verify.sh` already uses for paths that cross into a native process.

## Migration Plan

The wait runs before the install in `smoke.yml`; a tag already published changes nothing. The
ChangeLog records the fix under the next release. No rollback beyond reverting the workflow step.

## Open Questions

None.

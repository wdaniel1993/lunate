# Exact SDK pin, delivered by Dependabot

## Why

Every job in this repository failed at `actions/setup-dotnet` on 2026-10-25 with `Could not find .NET Core SDK with version = 10.0.403`. That version does not exist: `https://aka.ms/dotnet/10.0.403/dotnet-sdk-osx-arm64.tar.gz` redirects to bing.com, and the release metadata still says `latest-sdk: 10.0.402`. `global.json` carried `rollForward: latestFeature`, so the gate followed setup's metadata into a patch the feed never served — a red repository nobody in it could fix.

The pin also matters beyond CI: the SDK decides the runtime bundled into the self-contained binary, so following the newest advertised patch means shipping a runtime that was never reviewed. An exact pin plus a weekly Dependabot PR gives the same currency with a reviewed change behind it.

## What changes

- `global.json` pins `10.0.402` with `rollForward: disable` and `allowPrerelease: false`.
- `.github/dependabot.yml` adds the `dotnet-sdk` ecosystem, weekly, against `global.json`.
- `CONTRIBUTING.md` states that local builds need exactly that SDK, with the install command, and that bumps arrive through the Dependabot PR.
- The `ci-pipeline` spec's *SDK pinning* requirement is amended: the pin is exact, and a patch arrives as a reviewed pull request.

## Impact

No code, no dependency. `global.json`, `.github/dependabot.yml`, `CONTRIBUTING.md`, and one MODIFIED requirement with its scenarios.

## Verification

The probe that proved the fix: with `{"version": "10.0.402", "rollForward": "disable"}`, all four CI legs got past `setup-dotnet` into `Verify (bash)` — the failing runs died at 70 s inside setup.

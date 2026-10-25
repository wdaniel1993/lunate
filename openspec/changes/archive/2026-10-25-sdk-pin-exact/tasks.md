## 1. The pin

- [x] 1.1 `global.json` pins `10.0.402` with `rollForward: disable` and `allowPrerelease: false`.
- [x] 1.2 `.github/dependabot.yml` adds the `dotnet-sdk` ecosystem against `global.json`, weekly, with the reason stated in the file.
- [x] 1.3 `CONTRIBUTING.md` names the exact SDK, the install command, and the Dependabot path for a bump.
- [x] 1.4 The `ci-pipeline` spec's *SDK pinning* requirement is amended with the exact-pin rule and three scenarios.

## 2. Gate

- [x] 2.1 `openspec validate sdk-pin-exact --strict` green, and `openspec validate --all --strict` still green.
- [x] 2.2 All four CI legs get past `setup-dotnet` and run the full gate — #165 settled green on all seven checks.
- [x] 2.3 Dependabot is visible on the repository: `.github/dependabot.yml` parses as the first check of every run (a dedicated `dependabot.yml` status, green on #162/#163), vulnerability alerts answer 204 and security updates read `enabled`.

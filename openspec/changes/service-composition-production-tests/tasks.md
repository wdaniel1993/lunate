# Tasks — service-composition production tests

- [x] 1.1 Fixture `ServiceConsumerExtension` (factory + run-start lookup handler) wired into `Lunate.Coding.Tests` (csproj copy + `LayeringChecker`).
- [x] 1.2 `ExtensionServiceCompositionTests`: lifecycle test (start once/stop once + registry reachability) and cross-extension consumption test (provider present + absent control) green (2/2, 2026-10-17).
- [x] 1.3 SessionStarted load-order discovery documented + filed for a maintainer decision (2026-10-17).
- [ ] 2.1 `scripts/verify.sh` green (full gate, both cultures).
- [ ] 2.2 Adversarial review; archive `service-composition-production-tests`; PR.

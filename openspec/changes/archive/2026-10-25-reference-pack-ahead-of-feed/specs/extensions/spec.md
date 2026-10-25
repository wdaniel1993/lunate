## MODIFIED Requirements

### Requirement: Reference assemblies for compilation

Compilation SHALL use the .NET reference assemblies matching the running runtime and the host SHALL NOT require an installed SDK to obtain them. When the feed publishes no pack at or above the runtime, the highest published pack of the same major and minor **below** it SHALL be used as a last resort, recorded in the lock with the rule `nearest-below-runtime`, because a patch release is API-compatible and compiling against a subset beats not compiling at all. The preference order SHALL be: the exact runtime version; otherwise the lowest published pack of the same major and minor at or above the runtime; otherwise the highest published pack of that major and minor below it. A runtime whose major and minor have no published pack at all SHALL fail, naming the runtime and the feeds tried. The reference pack (`Microsoft.NETCore.App.Ref`, the chosen version and RID) SHALL be restored through the same in-process NuGet engine into the global packages folder on first compile and reused from there, and the chosen version and rule SHALL be recorded in the lock's toolset half. A published self-contained single-file binary SHALL compile a single-file extension with an empty `HOME` and no `dotnet` on `PATH`; that run SHALL be a test.

#### Scenario: A self-contained binary compiles with no SDK and an empty home

- **GIVEN** the published `osx-arm64` single-file binary, `HOME` pointing at an empty directory, and no `dotnet` on `PATH`
- **WHEN** a single-file extension declaring one package is trusted and compiled
- **THEN** the reference pack is restored into the global packages folder, the compile succeeds, and a second run reuses the cache without a feed request

#### Scenario: A runtime ahead of the feed compiles against the highest pack below it

- **GIVEN** a host running runtime `10.0.14` and a feed that publishes `Microsoft.NETCore.App.Ref` up to `10.0.13`
- **WHEN** a single-file extension is restored and compiled
- **THEN** the pack `10.0.13` is used, the lock records the rule `nearest-below-runtime`, and the compile succeeds

#### Scenario: A pack at or above the runtime still wins over an older one

- **GIVEN** a host running runtime `10.0.14` and a feed publishing `10.0.13` and `10.0.15`
- **WHEN** the reference pack is chosen
- **THEN** `10.0.15` is chosen with the rule `nearest-at-least`

#### Scenario: No pack of the runtime's minor still fails and names the runtime

- **GIVEN** a host running runtime `10.0.5` and a feed that publishes only `9.0.4`
- **WHEN** a single-file extension is restored
- **THEN** the restore fails with `no Microsoft.NETCore.App.Ref is published for runtime 10.0.5`, naming the feeds it asked

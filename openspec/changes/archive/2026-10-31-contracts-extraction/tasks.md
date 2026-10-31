## 1. Shared contract assembly

- [ ] 1.1 Add failing API/ownership tests for the moved tool, result-view and complete event types,
      plus their public ToolContext signature closure.
- [ ] 1.2 Create `Lunate.Contracts` with only BCL and MEAI dependencies; move the contract types,
      preserving namespaces, and keep channels, validators, sinks and Agent-only execution state in
      their current implementation projects.
- [ ] 1.3 Add `[TypeForwardedTo]` for every moved type in each old defining assembly; update shipped
      and unshipped API lists and prove old namespace/type resolution.
- [ ] 1.4 Add Agent → Contracts and Abstractions → Contracts, remove Abstractions → Agent, and set
      Contracts and Abstractions to `ExtensionApi` 1.14.0 in lockstep.

## 2. Dependency graph and contract consumers

- [ ] 2.1 Add separate failing tests for Agent → Abstractions and Abstractions → Agent; update the
      allowed/forbidden layering rules and verify each injected edge fails its corresponding test.
- [ ] 2.2 Update extension, sample, template and test projects that reference Agent only for moved
      contracts; keep legitimate implementation references and make Contracts an allowed host
      contract reference.
- [ ] 2.3 Correct the pending `decision-layer` proposal/design/spec/tasks wording: request/result
      types in Contracts, `IDecisionService` in Abstractions, implementation in Agent, bridged by
      `Lunate.Extensibility`.
- [ ] 2.4 Re-check ADR-0026: `Lunate.Roslyn` and `Lunate.Roslyn.BuiltIn` reference only Abstractions
      among Lunate projects; record the verified result in the PR.

## 3. Extension loading and identity

- [ ] 3.1 Add Contracts to `HostOwnedAssemblies`, single-file compile references and load-context
      sharing while retaining other host-owned assemblies required by signatures.
- [ ] 3.2 Add a legacy extension fixture compiled against the old `Lunate.Agent` contract identity;
      prove the forward loads it and its tool registers.
- [ ] 3.3 Add an extension/package fixture carrying its own `Lunate.Contracts.dll`; prove the host copy
      is used and only one Contracts type identity is loaded.
- [ ] 3.4 Update the extension template and authoring/package manifests to compile against Contracts
      and Abstractions and keep both runtime copies host-owned.

## 4. Publication plumbing and finalize

- [ ] 4.1 Update solution, NuGet packing, package manifests, API-shipping lists and project-enumerating
      analyzer/editorconfig settings for Contracts.
- [ ] 4.2 Run `openspec validate --all --strict`, the docs profile, focused Release builds/tests,
      `dotnet csharpier check .`, and confirm golden sessions and e2e files are unchanged.
- [ ] 4.3 Archive `contracts-extraction` on this implementation branch after review and the fast gate;
      verify the archived extension delta reproduces the resulting spec exactly.

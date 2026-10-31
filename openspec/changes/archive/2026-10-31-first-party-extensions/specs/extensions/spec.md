# Delta — first-party extensions: the supported extension tier

## ADDED Requirements

### Requirement: The supported extension tier

The repository SHALL keep a supported extension tier in a dedicated directory beside
`samples/extensions/`, and the release tooling SHALL treat the two as different products: a sample
demonstrates an API surface and is copied by a reader learning the API; a supported extension is
installed by a user for what it does. An extension belongs in the tier only when it meets the whole
bar: it uses only the public extension API (the contract assembly and the core tool types it carries —
never host internals); every test project it carries is a solution member, so the verify gate runs it
on every change; its project is packable as a `Lunate.Extensions.*` bundle (a `lunate.package.json`, no
host assembly inside the package, readme and licence metadata on the package) and it is published to a
feed a user installs from on the release train; and its readme is a product page — what the extension
does, how to install and configure it — never a "capabilities proven" table. Promotion into the tier is
the maintainer's decision, recorded with the reasons on the tier's README beside the extensions left
out; the tier README ALSO states what "supported" does not promise: publishing is the release
workflow's job (a supported extension adds no release publishing of its own), and a supported extension
is not core — it runs as any extension does, through the same loader, the same trust flow, the same
`apiVersion` gate and the same settings store, so removing it or trusting it at install time behaves
exactly like any other extension. Promotion does not change the extension's manifest id, namespace or
tool names, so an installed user sees no rename.

#### Scenario: An extension promoted to the tier

- **GIVEN** an extension whose code used only the public extension API, whose tests are solution
  members, whose project packs as a `Lunate.Extensions.*` bundle, and whose readme is a product page
- **WHEN** the extension is moved into the tier directory and the maintainer's promotion decision names it
- **THEN** its project layout, manifest id, namespaces and tool names are unchanged, its tests still run
  in the gate, and a user who had installed it before the promotion keeps installing the same package id

#### Scenario: A sample stays a sample

- **GIVEN** an extension that exists in `samples/` to demonstrate an API surface, or one whose
  configuration story the platform does not yet support
- **WHEN** the tier's README lists what is promoted and what is not
- **THEN** the unpromoted extension stays beside the tier with a reason recorded, and nothing in the
  build packs or publishes it

#### Scenario: A promoted extension behaves like any extension at runtime

- **GIVEN** an installed extension from the tier
- **WHEN** a session discovers, trusts and loads it
- **THEN** discovery, the trust prompt, `/extensions`, settings and uninstall behave exactly as they do
  for any extension with the same manifest id — the tier is a repository and packaging contract, not a
  privileged runtime scope

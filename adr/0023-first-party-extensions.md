# 0023 — A first-party extension tier beside `samples/`

- Status: accepted — 2026-10-27 (maintainer decision on #242; recorded 2026-10-29)
- Date: 2026-10-29
- Relates to: ADR-0017 (extension architecture), OpenSpec capability `extensions`; issue #242
  (milestone 0.2), umbrella #182

## Context

Extension code lives in two homes: `templates/extension/` (a copyable starting point) and
`samples/extensions/` (the fitness suite — one sample per core capability, tests in the gate). Both are
learning tools, and the second's own rule says so ("a sample that needs a workaround means the core is
missing a primitive"). Users cannot tell which extensions they may install and rely on: the only one
published on the release train, `Lunate.Extensions.Subagents`, calls itself a "sample" in its docs, and
its README is a capabilities table with a copy-and-extend section. A user who wants, say, LSP
diagnostics or an extra approval gate has no answer, and the repository has no notion of supported.

## Decision

The repository keeps a first-party **supported** extension tier in `extensions/` beside `samples/`.
"Supported" promises four things, all grounded in the maintainer's ruling on #242:

1. **It works for a user, not a reader**: an extension is promoted only when a user installs it for
   what it does, not to learn the API. Promotion is recorded with reasons; unpromoted extensions stay
   samples with the reason on the tier README (`model-router`: needs a configuration surface that does
   not exist; the fitness-suite samples exist to demonstrate an API).
2. **Public contract only**: a promoted extension uses only the public extension API — the contract
   assembly and the core tool types it carries — never host internals.
3. **Tested like the product**: every test project a promoted extension carries is a solution member,
   so the verify gate runs it on every change, and a bug fix to one is an ordinary change.
4. **Shipped like the product**: its project packs and is published as `Lunate.Extensions.*` on the
   release train, and its README is a product page (behaviour and configuration).

The tier does **not** create a privileged runtime scope: a supported extension loads through the same
loader, the same trust flow, the same `apiVersion` gate and the same settings store as any extension,
and the release workflow (not the tier) owns what a tag publishes.

## Consequences

- Positive: users get an install-and-rely answer; the maintenance surface of "supported" is bounded and
  test-first; the fitness suite keeps doing its one job (proving the platform) without carrying product
  promises.
- Negative: the tier gains a compatibility duty — the API version a promoted extension declares and the
  behaviour its README promises are kept honest by maintainer review, not by the compiler.
- Follow-up: publishing the contract packages is #237's; tag-publishing every supported extension to
  nuget.org follows it (#242's acceptance: "the release workflow packs them from the same tag").
  Promotion decisions for future extensions (for example `worktree-tasks`, which needs a core-or-
  experimental call) land as maintainers resolve them, with reasons on the README.

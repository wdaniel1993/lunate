# Supported extensions

This directory is Lunate's first-party tier of extensions: the ones you install for what they do and
rely on. The directory beside it, [`samples/extensions/`](../samples/extensions/), is the fitness
suite — one sample per core capability, each proving the platform and teaching an API. Different
products, different promises.

## The extensions

| Extension | Package | What it does |
| --- | --- | --- |
| [`subagents`](./subagents/README.md) | `Lunate.Extensions.Subagents` | One tool, `subagents__spawn`, that runs a child agent in a linked child session on the same workspace, hooks and approvals as the parent run. |
| [`lsp-diagnostics`](./lsp-diagnostics/README.md) | `Lunate.Extensions.LspDiagnostics` | Attaches language-server diagnostics to every successful `write`/`edit` of a C# file. |
| [`permission-gate`](./permission-gate/README.md) | `Lunate.Extensions.PermissionGate` | Enforces a settings-driven policy on destructive tool calls before the approval prompt — block or confirm. |

## What "supported" promises

An extension belongs here only when it meets the whole bar (recorded in ADR-0023):

- **A user product, not a reader demo** — a user installs it for what it does, not to learn the API.
- **The public contract only** — it uses the public extension API (the contract assembly and the core
  tool types it carries), never host internals.
- **Tested like the product** — every test the extension carries is a solution member, so the verify
  gate runs it on every change; a bug fix is an ordinary change.
- **Shipped like the product** — its project packs and is published as `Lunate.Extensions.*` on the
  release train, and its README is a product page.
- **Maintenance** — bugs are fixed, and the extension's declared `apiVersion` and its documented
  behaviour are kept compatible changes instead.

## What it does not promise

- **No privileged runtime scope.** A supported extension is not core: it is discovered, trusted,
  loaded, configured and uninstalled exactly like any extension. Installing is not trusting; the
  first-load question still asks, and project extensions still need a per-repository decision.
- **No extra release machinery.** What a tag publishes is the release workflow's contract, not the
  tier's. Publishing the contract packages lands with #237, and tag-publishing every supported
  extension to nuget.org follows it — until then `Lunate.Extensions.Subagents` is the one package a tag
  publishes, and a supported extension's source directory is where the release tooling packs it from.
- **No promotion without a decision.** Extensions arrive here by the maintainer's recorded decision,
  not by their own code being good enough.

## What is not here, and why

From the samples, deliberately unpromoted (the maintainer's reasons are recorded on issue
[#242](https://github.com/wdaniel1993/lunate/issues/242)):

- `model-router` — its provider and virtual-model configuration needs a configuration surface that
  does not exist yet.
- `code-mode`, `memory-provider` and the other samples that exist to demonstrate an API seam keep
  their teaching job in the fitness suite; `code-mode` additionally waits for the 0.2 sandbox.

## Writing an extension

Start at [`docs/extensions/`](../docs/extensions/README.md) (quickstart, manifest, every surface) and
[`templates/extension/`](../templates/extension/README.md). Whether an extension lands in the tier is
the maintainer's decision: propose it on an issue with what the extension does for a user, and the
promotion is recorded on this page with reasons either way.

# Sample: model-router

A model-routing extension: the manifest declares one OpenAI-compatible provider, the extension
registers the virtual model `ext/model-router/auto` (per-request routing between the provider's
`fast` and `deep` models), a counting middleware at the `AroundBackend` slot, and the read-only
`ask` tool that makes a nested model call through the run-bound `ToolContext.Models`.

## Core capabilities proven

| Capability | Where |
| --- | --- |
| Declared-provider instantiation | The manifest declares `router-local` (`endpoint`, `secretName` `router-key`, `modelIds` `fast`/`deep`, `contextWindow` 32768); the host reads the secret from the extension's secrets store on first use and it only ever reaches the client's request headers. |
| Virtual model routing per request | `auto` selects `deep` when the request's text exceeds `deepThresholdChars` (default 4000) and `fast` otherwise; one client instance routes consecutive calls to different backends. |
| Capabilities derived | `auto` declares no window/tools: it derives the weakest backend's (both backends are 32768, tools supported). |
| Middleware at a defined slot | The counting middleware wraps every resolved backend client (`AroundBackend`), so it sees exactly the calls that reach a backend; counts are exposed through the `router-counters` service. |
| Nested model call with usage roll-up | `ask` calls `ctx.Models.For("ext/model-router/auto")`; the call emits `ModelRouted(auto → fast/deep)` and a `UsageUpdated` carrying the resolved backend's id, counted into the run's token budget and (inside a subagent) its tree. |
| Teaching errors | Unknown model ids, unknown backends, virtual-model cycles and depth > 8, missing secrets and `supportsTools: false` requests all fail with errors naming the fix. |
| Testing kit | `ModelRouterExtension.Tests` loads the built extension through the real loader, overrides the manifest endpoint to a local fake OpenAI endpoint (raw `TcpListener`, ephemeral port) and drives real harness runs, including a two-extension child-run composition with `subagents`. |

## Layout

```text
samples/extensions/model-router/
  ModelRouterExtension/        the extension: manifest, factory, selector, counting middleware, tool
  ModelRouterExtension.Tests/  end-to-end tests over the real loader, registry and harness
  README.md
```

## Semantics

- **Routing** — `auto`'s selector sums the request messages' text characters; more than
  `deepThresholdChars` routes to `deep`, otherwise `fast`. The decision is per request: the same
  client instance can route consecutive calls differently.
- **Counting** — one count per backend call after it completes, keyed by the wire model id the
  backend reported (`fast`/`deep`). A virtual-model call counts once (when it reaches its backend);
  a direct call to a backend counts too. Read it through the extension's `router-counters` service.
- **Tool** — `ask` is read-only and takes `prompt` (required). It returns the routed model's reply
  text; without a run context (`ToolContext.Models` null) it refuses with a teaching error.
- **Secret** — `router-key` is read by the host from the extension's secrets store
  (`~/.lunate/extensions-secrets/model-router.json`); the extension code never sees the value.

## Settings

`deepThresholdChars` (number, default 4000) is the request-size threshold between `fast` and
`deep`.

## Honest limits

- **The host-side secret read is hygiene, not a security boundary.** In-process extensions are
  fully trusted (ADR-0017); reading the secret host-side simply means the value never needs to
  reach extension code.
- **The declared endpoint must speak the OpenAI API** (`/chat/completions` under the declared
  base). The sample defaults to a local Ollama-style endpoint (`http://127.0.0.1:11434/v1`) that
  serves `fast` and `deep`.
- **The selector is a heuristic.** A real router would look at task type or cost; request size is
  the smallest honest example of a per-request decision.
- **No streaming special-casing**: the registry composes streams unchanged; the counting middleware
  records one call when a stream completes.

## Copy and extend

1. Copy this directory, rename the projects and namespaces, and update `extension.json`
   (`id`, `entryAssembly`, `apiVersion`, `modelProviders`, `tools`, `settingsSchema`).
2. Point the declared provider at your endpoint and add the secret under
   `~/.lunate/extensions-secrets/<id>.json` (`{"router-key": "..."}`).
3. Keep the tests: they rewrite the manifest endpoint to a local fake, so CI never needs a real
   model server.

See `templates/extension/README.md` for the full manifest and registration reference.

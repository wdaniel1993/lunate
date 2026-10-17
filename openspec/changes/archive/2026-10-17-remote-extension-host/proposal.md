# Remote extension host

## Why

ADR-0017 committed the architecture to a JSON boundary: "Everything crossing the extension boundary is JSON-serializable — no delegates, no live objects; handles instead — so out-of-process extensions in other languages stay possible", and recorded the deferred piece: "A later `Lunate.Extensibility.Remote` host will speak JSON-RPC over stdio, enabling TypeScript/Python extensions and real isolation."

The groundwork has landed: every hook payload and result is already `System.Text.Json`-conformant (the `HookContractTests` round-trip suite), the core process capability (`IProcessRunner`) runs interactive children line-by-line with tree kills, the manifest/trust/discovery machinery is scope-aware, and the MCP server registration already models "a child process declared by configuration" (command, args, env, working directory, call timeout).

What is missing is the host itself: an extension that lives in its own process, speaks the hook and service contract over stdio, and reaches the same hook pipeline as an in-process extension.

## What changes

- **New project `Lunate.Extensibility.Remote`** — the out-of-process host: a remote extension adapter (`IExtension` implementation) that starts the extension's process lazily, performs an `initialize` handshake, and bridges every hook call and service call over newline-delimited JSON-RPC 2.0 on the child's stdio, reusing the core process capability for the transport (no new packages).
- **Manifest**: `entryAssembly` becomes optional; a remote extension declares a `runtime` object (command, args, working directory, environment, call timeout) mirroring the MCP server registration shape. Exactly one of `entryAssembly` / `runtime` is required.
- **Lifecycle**: lazy start on first use; crash → a failed status and the session continues (the MCP server precedent); session end → a close notification, a bounded grace, then a tree kill; the child receives an explicit environment allow-list so Lunate's provider keys cannot leak.
- **Conformance**: every hook payload/result DTO and the service-call envelope round-trips over the actual wire, and a scripted remote extension (a small script speaking the protocol) proves the host end to end in CI.
- **ADR**: `adr/0017-addendum-remote-host.md` records the wire protocol and lifecycle decisions.

## Out of scope (named, not built)

- TypeScript/Python SDKs for extension authors (they follow the wire contract this change pins).
- Tools, renderers, widgets, MCP registrations and model providers crossing the boundary — hooks and services are the v1 surface; the design keeps the other families shaped for later.
- Permission sandboxing: the process boundary is runtime isolation (dependencies, language, crash containment), not a security boundary — in-process trust and the trust prompt are unchanged (ADR-0017).

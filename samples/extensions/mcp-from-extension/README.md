# Sample: mcp-from-extension

An extension that contributes one MCP server through `IExtensionContext.RegisterMcpServer`: the
`mcp.json` shape (command, args, env), exposure `Direct`, and one per-tool override (`secret` →
`Hidden`). Registration performs no I/O and starts nothing; the first tool listing starts the
server exactly once, its tools arrive namespaced `fake__tool`, and `/mcp` lists the server with its
source `extension 'mcp-from-extension'`. The bundled `FakeMcpServer` is a real stdio MCP process the
tests drive end to end through the extension testing kit.

## Core capabilities proven

| Capability | Where |
| --- | --- |
| `RegisterMcpServer` | `McpFromExtensionExtensionFactory` registers `fake` at creation; the manifest has no server field. |
| Namespaced tools | The adapter names every tool `{server}__{tool}` (`fake__echo`, `fake__add`, …) and sets the namespace to the server name. |
| Exposure and overrides | Exposure `Direct` with `{"secret": Hidden}`: `fake__secret` is registered but absent from the model's declarations and refused by nested execution. |
| Lazy server start | Nothing starts at registration or composition; the first listing starts the process once (marker file: one `started pid=` line across listings). |
| Failure isolation | A registration with a missing binary logs the server and its extension and the session continues without its tools. |
| Crash status | A started process that exits flips to failed (`the server process exited`) when next observed; one log line and, interactively, one notice. |
| Public testing surface | `Lunate.Extensibility.Testing` composes the registrations through the production mapping (`ExtensionTestHost.ComposeMcpServersAsync`); the sample's tests use only the kit and the extension. |

## Layout

```text
samples/extensions/mcp-from-extension/
  McpFromExtensionExtension/            the extension: manifest, factory, settings
  McpFromExtensionExtension.Tests/      kit-based tests, scripted model client, nested-call probe
  McpFromExtensionExtension.Tests/FakeMcpServer/  the deterministic fake MCP server
  README.md
```

## Behaviour

- **Registration** — the factory reads `markerPath` from the settings and registers the server
  with `dotnet exec FakeMcpServer.dll`, passing the marker path as the config's environment
  (`LUNATE_FAKE_MCP_MARKER`). The fake server must sit next to the extension assembly (the test
  build copies it there).
- **Listing** — the first listing starts the fake once; later listings return the cached tools and
  never restart it. The wrapped tools carry risk `Execute`, so the approval policy applies.
- **Exposure** — `echo`, `add` and `crash` are `Direct`; `secret` is overridden to `Hidden`: it is
  neither declared to the model nor callable via `ToolContext.ExecuteToolAsync` (the nested call is
  refused with the exposure in the message).
- **Crash** — `fake__crash` exits the process (the in-flight call errors). The next observation
  (`/mcp` or a kit re-composition) flips the server to failed with the pinned reason, logs once,
  and the crashed server's tools are not handed out again.

## Settings

`markerPath` (string, optional): the file the fake appends `started pid=<n>` and one line per tool
call to. Unset writes no marker; the server still runs.

## The fake server

`FakeMcpServer` mirrors `tests/tools/TestMcpServer` on the official `ModelContextProtocol` package:
stdio only, tools added before `McpServer.Create`, every diagnostic on stderr. Tools: `echo`
(text), `add` (structured `{sum}`), `secret` (the payload that must never reach the model) and
`crash` (exits the process). The marker file receives `started pid=<n>`, `echo <text>`,
`add <a> <b>`, `secret` and `crash` lines.

## Copy and extend

1. Copy this directory, rename the projects and namespaces, and update `extension.json`
   (`id`, `entryAssembly`, `apiVersion`, `settingsSchema`).
2. Replace the fake server with the real one: point `McpServerConfig` at its command and arguments
   (and keep the exposure map for tools that must never reach the model).
3. Keep the tests: they install the built extension into a temp store through
   `ExtensionTestHost`, compose the server with `ComposeMcpServersAsync` and assert names,
   exposures, results, failure isolation and process ownership.

See `templates/extension/README.md` for the full manifest and hook wiring reference.

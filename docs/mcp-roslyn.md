# Roslyn MCP server

`lunate mcp roslyn` serves Lunate's five C# tools over the Model Context Protocol on stdio, so an
MCP host such as Claude Code or OpenCode can use the same semantic navigation Lunate's own agent
uses. It is the Roslyn bundle without the agent: no model, no session, no approvals.

## Configure it

Add the server to the host's MCP configuration. The command is the same `lunate` binary:

```json
{
  "mcpServers": {
    "lunate-roslyn": {
      "command": "lunate",
      "args": ["mcp", "roslyn"]
    }
  }
}
```

Start it from the repository root, or let the client declare workspaces through MCP `roots`. The
`roslyn` built-in manifest activates tools only when a direct child of the selected root matches
`*.sln`, `*.slnx` or `*.csproj`; the host's `ToolActivationEvaluator` is the same one used by chat,
print, ACP and child runs. When roots are declared, the server selects the first matching root. If
the client has no roots channel or returns an empty roots list, the server uses its launch directory;
declared but non-matching roots do not fall back to the launch directory. The built-in is host-owned,
not installed or trust-gated, and `--no-extensions` does not disable built-ins. No Roslyn workspace
is loaded until the first C# tool call; the initial listing only evaluates the manifest rule.

## Tools

| Tool | What it answers |
| --- | --- |
| `cs_diagnostics` | Compiler errors and warnings for a file, the changed files or the whole solution |
| `cs_find_symbol` | Definition location and signature for a type, member or namespace name |
| `cs_find_references` | Usage sites of an exactly resolved symbol |
| `cs_outline` | A file's types and member signatures without bodies |
| `cs_rename` | A solution-wide rename **plan** as per-file line edits |

All five declare `readOnlyHint`. `cs_rename` plans only: it returns the edits and changes nothing on
disk.

## Repository without a solution

The server lists no `cs_*` tool for that root: a workspace with no `.sln`, `.slnx` or `.csproj` at its
root offers nothing to call. That is the same answer the chat path gives in the same directory — a
tool that is guaranteed to error costs the calling model a turn before it learns what one line says
for free. `/extensions` reports `roslyn` with scope `builtin` and the inactive manifest rule and
reason; it offers no uninstall, unload or reload action.

When a client announces `notifications/roots/list_changed`, the server re-reads declared roots and
re-evaluates activation. If the offered tool set changes, the server sends
`notifications/tools/list_changed`; this is supported only when the client advertised that roots can
change. Without a roots channel, or without a roots-change notification, the server cannot observe a
new or removed project file: restart the server to compose the launch directory again. A stale call
to a tool absent from the selected root returns a JSON-RPC invalid-params error and does not write
anything other than the protocol response to stdout.

If a listed solution disappears while the server runs and no roots event announces a root change,
the already-composed tool remains listed. Its call reports the backend's normal solution-load
explanation as a tool error, and the server keeps running.

## Absent by design

- **No approval panel, no write path.** Every exposed tool is read-only; `cs_rename` returns a plan
  and never applies it. Verified edits stay in Lunate, where the edit and write tools go through the
  approval policy. The server exposes no edit, write, bash or git capability.
- **No agent surface.** It serves tools only: no prompts, resources, sampling or elicitation, and no
  model or session features. It reads the client's `roots` when they are offered, but it serves no
  root list of its own.
- **No nested servers or third-party extensions.** The host-supplied Roslyn built-in is the only
  extension loaded by this command; it does not start configured MCP servers or discover extensions
  from disk.
- **No file watcher.** Source edits are re-read on the next call (the backend's freshness stamps),
  but a changed project, solution or build-configuration file needs a server restart.
- **No name prefix.** The tools keep their unprefixed `cs_*` names, unlike external MCP servers
  whose tools arrive as `<server>__<tool>` inside Lunate.

## Protocol purity

stdout carries JSON-RPC frames only. The startup diagnostic names the launch directory, and any fatal
error goes to stderr, the same rule ACP mode follows.

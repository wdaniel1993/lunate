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

Start it from the repository root, or let the client declare its workspace through MCP `roots`. The
tools are offered for a root only when the built-in's activation rule matches it — `.sln`, `.slnx` or
`.csproj` at that root — evaluated by the same host evaluator the chat path uses. A client that
declares no roots is answered for the server's working directory, so launching per project stays the
simple deployment. No solution is loaded before the first call: `initialize` and `tools/list` answer
immediately, and `cs_outline` works without a loaded solution.

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

The server lists no `cs_*` tool for that root and writes one line to stderr naming the rule and the
patterns it looked for: a workspace with no `.sln`, `.slnx` or `.csproj` at its root offers nothing to
call. That is the same answer the chat path gives in the same directory — a tool that is guaranteed to
error costs the calling model a turn before it learns what one line says for free.

Add a solution or a project file and the server re-evaluates: a client that declared `listChanged`
support receives `notifications/tools/list_changed` and the tools appear without a restart. A client
that did not declare it keeps the list it received at `initialize`; restart the server after adding
the solution.

If a listed solution disappears while the server runs and no roots event announces it, the tool stays
listed and answers with the loader's explanation — `No C# solution is available: no solution found: no
.sln or .slnx under '<root>' or its immediate subdirectories` — as a tool error, and the server keeps
running.

## Absent by design

- **No approval panel, no write path.** Every exposed tool is read-only; `cs_rename` returns a plan
  and never applies it. Verified edits stay in Lunate, where the edit and write tools go through the
  approval policy. The server exposes no edit, write, bash or git capability.
- **No agent surface.** It serves tools only: no prompts, resources, sampling or elicitation, and no
  extension, model or session features. It reads the client's `roots` when they are offered, but it
  serves no root list of its own.
- **No nested servers.** It does not start configured MCP servers or load extensions.
- **No file watcher.** Source edits are re-read on the next call (the backend's freshness stamps),
  but a changed project, solution or build-configuration file needs a server restart.
- **No name prefix.** The tools keep their unprefixed `cs_*` names, unlike external MCP servers
  whose tools arrive as `<server>__<tool>` inside Lunate.

## Protocol purity

stdout carries JSON-RPC frames only. One startup diagnostic — the tool count and working directory —
and any fatal error go to stderr, the same rule ACP mode follows.

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

Start it from the repository root. The server is bound to the working directory it starts in and has
no roots or cwd switching, so the host should launch it per project. No solution is loaded before
the first call: `initialize` and `tools/list` answer immediately, and `cs_outline` works without a
loaded solution.

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

The server still starts and lists all five tools. Each solution-based call returns the loader's
answer — `No C# solution is available: no solution found: no .sln or .slnx under '<root>' or its
immediate subdirectories` — as a tool error, and the server keeps running. Restart it in the
directory that holds the `.sln`/`.slnx` (the root or one level down) to use it.

## Absent by design

- **No approval panel, no write path.** Every exposed tool is read-only; `cs_rename` returns a plan
  and never applies it. Verified edits stay in Lunate, where the edit and write tools go through the
  approval policy. The server exposes no edit, write, bash or git capability.
- **No agent surface.** It serves tools only: no prompts, resources, sampling, roots or elicitation,
  and no extension, model or session features.
- **No nested servers.** It does not start configured MCP servers or load extensions.
- **No file watcher.** Source edits are re-read on the next call (the backend's freshness stamps),
  but a changed project, solution or build-configuration file needs a server restart.
- **No name prefix.** The tools keep their unprefixed `cs_*` names, unlike external MCP servers
  whose tools arrive as `<server>__<tool>` inside Lunate.

## Protocol purity

stdout carries JSON-RPC frames only. One startup diagnostic — the tool count and working directory —
and any fatal error go to stderr, the same rule ACP mode follows.

# Security policy

## Reporting a vulnerability

Use GitHub's private vulnerability reporting on this repository — the **Report a vulnerability**
button under the Security tab — to send a report privately to the maintainer. That is the only
channel: please do not open a public issue, and do not post the details anywhere public before a fix
has shipped. A useful report says what you ran, what happened, what you expected, and on which
platform and version (`lunate --version`).

This is a small, personal, MIT-licensed project: there is no bounty and no response-time promise.
Reports are read and answered as quickly as the maintainer can, and credit is given in the release
notes unless you ask otherwise.

## The trust model in one page

Lunate runs with your user privileges, and its security posture is deliberately that of a
single-user CLI tool, not a sandbox. Four boundaries are worth knowing about.

**Approval prompts.** The `read`, `write`, `edit` and `bash` tools all run as you, on model-chosen
arguments. Every write, edit and command is shown as a prompt — `Allow edit src/Order.cs?  [y]es
[n]o  [a]lways this session` — before it runs, and the `approval` setting (`ask`, the default, or
`auto-edit`) decides what still asks. `--yolo` approves everything for one run and is never a saved
setting. Print mode has nobody to ask, so it maps the same policy onto tool risk and refuses what it
may not approve, turning a refusal into an error result the model can read; ACP mode asks the
editor's own permission request for everything the policy does not already allow, and has no
`--yolo`.

**The workspace boundary.** The file tools resolve paths canonically, following symlinks, and refuse
anything outside the working directory, so a model mistake cannot read or write next to your home
directory by accident ([ADR-0016](adr/0016-workspace-boundary.md)). This is a guard rail, not an
isolation boundary: `bash` is intentionally not boundary-checked — the approval policy is its guard
— and time-of-check/time-of-use races are accepted.

**Repository-provided code.** A project extension, a project `mcp.json` file, each server it defines
and a project skill are code from the repository, so each asks once per repository before it runs
and asks again when its content changes. Answers live in `~/.lunate/trust.json`, per user and
machine; `/trust` lists them and clears them, and `lunate trust` inspects and records them for
scripted runs. Denial refuses the load, and untrusted input is never auto-approved — an ephemeral CI
runner starts untrusted every time. In print and ACP mode an untrusted project item is refused with
the approval paths named.

**Extensions.** In-process extensions are fully trusted: the manifest and the loader isolate
dependencies, not permissions, and a malicious in-process extension is as powerful as Lunate itself
([ADR-0017](adr/0017-extensibility-architecture.md)). An out-of-process extension is a separate
process but runs with your user privileges — the process boundary provides dependency, language and
crash isolation, and it must never be described or treated as a permission boundary. Global
extensions in `~/.lunate/extensions/` are not gated, because installing one is already a decision you
made; project extensions always ask.

## Secrets

API keys come from the environment (`OPENAI_API_KEY`, `ANTHROPIC_API_KEY`) or from
`~/.lunate/auth.json`, which Lunate writes owner-only (`rw-------`) on POSIX. Extension secrets live
in `~/.lunate/extensions-secrets/<extension-id>.json`, are never written to session files, and are
never printed by the `/extensions` view; errors name the missing key, never its value. Provider keys
are read by Lunate and sent only to the provider you configured. Configured stdio MCP servers run as
children that inherit your environment — existing MCP setups keep working — except for the values
above: the credentials Lunate itself knows (provider keys, `auth.json` keys, extension secrets and
the configured `env` values) are removed from that child's environment unless the server's own
`env` map names the variable explicitly. The trust prompt for a project server states the same.

## What is out of scope

- Anything that requires an attacker to already run code as you on the machine.
- A malicious model, provider, extension or MCP server you chose to trust — the trust prompt is the
  decision point, and the record of it is yours to clear.
- `bash` doing what you approved it to do, and time-of-check/time-of-use races inside the workspace
  boundary.
- Exhausting the model's own step and token budgets — those are bounded, reported and yours to
  change, not a vulnerability.

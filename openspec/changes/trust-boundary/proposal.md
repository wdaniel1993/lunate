# The approval panel is the boundary

## Why

The shipped trust model claimed more than it enforced. `--yolo` aside, the panel was described as the boundary, but three code paths quietly bypassed it: unannotated or destructive extension tools derived the `Write` risk and were auto-approved under `auto-edit`; the ACP approver auto-approved writes; a project trust handler could return `Allow` and record trust without a prompt. Meanwhile the docs said the session marker and `trust.json` made a bash command unable to rewrite the state root, which was never true — a child process with the user's shell can write either.

## What changes

- Risk derivation stops treating "not read-only" as "safe to auto-approve": no annotations, `Destructive` or `OpenWorld` derive `Execute`; only a plain `ReadOnly` tool is `ReadOnly`.
- The ACP approver auto-approves only `ReadOnly` calls; every write and every command asks the editor under every policy.
- A project trust handler is a veto: `Deny` refuses, and anything else still goes through the prompt.
- High-risk calls — `lunate trust`/`install`/`uninstall` through bash, and any bash command or write targeting the state root — always ask under ask and auto-edit, are never covered by an allowlist or a remembered grant, and are announced as unpoliced under `--yolo`.
- `lunate trust --yes` requires an interactive terminal; `--yes --digest <sha>` remains the non-interactive path because it names the exact content.
- The documents say what is true: the panel is the boundary, `--yolo` removes it, a process with the user's shell can write the state root, and Windows has no sandbox.

## Capabilities

- `agent-tools` (modified, added): annotation-derived risk; a new high-risk requirement.
- `protocols` (modified): ACP approval composition.
- `extensions` (modified): the `lunate trust` command's terminal requirement.

## Impact

No new dependencies. `ToolRiskResolver`, `AcpApprover`, `NonInteractiveApprover`, the interactive approver, `ExtensionLoader.EnsureTrustedAsync`, `TrustCommand`, `Cli.Run` and print mode change; the ADR and the security sections of the guide and extension docs change with them. Tests that encoded the old behaviour are updated with the reasoning stated in the PR.

# Proposal: ACP permissions, editor file system and resource links (T-28)

## Why

T-27 made Lunate drivable from an editor, but approvals run through the local policy without prompting and file tools touch the disk directly. The guide pins the rest: approvals map to the client's permission request, and file reads/writes use the editor's file system when offered, so unsaved buffers are respected. ACP v1 also makes resource-link prompt blocks mandatory — T-27 logs and skips them; the mapping is carried here.

## What changes

- **Permissions**: an ACP approver composes the existing policy ladder (`Ask`/`AutoEdit`) with the client's `session/request_permission`: calls the policy already allows run unprompted; everything else asks the editor, with `allow_once`/`allow_always`/`reject_once`/`reject_always` mapped (always = session-scoped per tool). Cancel during a request resolves as a decline. No `--yolo` in ACP mode (unchanged conflicts).
- **Editor file system**: a text-file seam (`ITextFileAccess`) routes `ReadTool`/`WriteTool`/`EditTool` file access through the client's `fs/read_text_file`/`fs/write_text_file` when the client advertises the capability; otherwise local disk. BOM preservation in ACP mode is the client's concern (documented).
- **Resource links**: prompt `ResourceLinkContent` blocks map to model-readable text (workspace-local file URIs → `@<path>`; other URIs → `name (uri)`), replacing the log-and-skip.
- **Factory context**: `IAcpServer`'s factory widens from `Func<string, AgentHarness>` to `Func<AcpSessionContext, AgentHarness>` carrying the session cwd plus the adapter-built client approver/file access (the widening T-27's review anticipated). `additionalDirectories`/`mcpServers` from `session/new` stay logged-and-ignored (documented seam).

## Done when

The approval round trip, the fs routing (buffer content visible, writes client-side) and resource-link mapping are covered by in-process pipe tests; `scripts/verify.sh` green. One real Zed session before Phase 5 closes is the card's post-merge item.

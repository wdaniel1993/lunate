# Configuration reference

Every key the settings parser accepts, and only those keys. The table below is generated from
[`src/Lunate.Coding/settings-schema.json`](../src/Lunate.Coding/settings-schema.json) — the same
file the store's `SettingsSchema` table reads and tests pin to the parser's own literals — so it
cannot list a key the store refuses. Edit the schema, then run `scripts/docs-sync` to regenerate;
`--check` fails a stale page.

The store reads user settings from `~/.lunate/settings.json`. A project may carry settings in
`.lunate/settings.json` at the workspace root, where a different scope applies: user settings
accept every key, while a project file may carry only the keys scoped `user, project` below —
model profiles, their usage assignments and the limits it may *lower*, never raise. Project
settings may reference user providers and define profiles on them, but they can never define
providers, endpoints or credential references: credentials and endpoints are user-owned, so a
repository cannot redirect them. Decision consumers are enabled by the user only.

`*` in a key stands for a chosen id — a provider id, a profile name or a binding id. Defaults
shown are the documented defaults the store applies when a key is missing.

<!-- docs-sync: settings -->
| Key | Type | Default | Scope | Description |
| --- | --- | --- | --- | --- |
| `approval` | string | ask | user | The approval policy: `ask` sends writes, edits and commands to the approval flow; `auto-edit` runs file writes and edits and still asks for commands. `LUNATE_APPROVAL` overrides it. Values: `ask`, `auto-edit`. |
| `bashGuard` | boolean | true | user | Guards bash commands against edits outside the trusted working directory. There is no environment-variable override. |
| `decisions.approvals` | string | off | user | The approval-gate consumer; a deny recommendation reaching `approvalsThreshold` escalates the ask when `enforce`. Decision consumers are enabled by the user only. Values: `off`, `shadow`, `enforce`. |
| `decisions.approvalsThreshold` | number | 0.5 | user | A finite probability from 0 through 1: what a deny recommendation must reach to escalate the approval ask. |
| `decisions.profile` | string | unset | user | The decision profile the layer runs on; without it the layer is unavailable and nothing is offered, never erroring. |
| `decisions.questions` | string | off | user | Adds a probability marker beside the top option of a question; always display-only, the user still chooses. Values: `off`, `suggest`. |
| `decisions.routing` | string | off | user | The model-routing consumer; applies the most probable profile only when its confidence meets routingMinConfidence, otherwise keeps the configured chat profile. Values: `off`, `shadow`, `enforce`. |
| `decisions.routingMinConfidence` | number | 0.5 | user | A finite confidence threshold from 0 through 1; routing changes the configured chat profile only at or above this value. |
| `decisions.routingProfiles` | string array | [] | user | Two to 255 unique configured profile names the router may select among; routing requires at least two. |
| `decisions.tool` | string | off | user | Offers the `decide` tool, only when `decisions.profile` resolves as well. Values: `off`, `on`. |
| `favorites` | string array | [] | user | The ordered favourite model ids, at most eight unique entries; schema 2 and later. |
| `model` | string | unset | user | The default model id. `LUNATE_MODEL` overrides it. |
| `output.spillThresholdTokens` | integer | 2000 | user, project | The estimated-token threshold (UTF-8 bytes / 4) above which a tool result is spilled. A project file may lower it, never raise it. `LUNATE_TOOL_SPILL_THRESHOLD` overrides it. |
| `output.toolResultLimit` | integer | 30000 | user, project | Characters of one tool result before truncation. A project file may lower it, never raise it. `LUNATE_TOOL_OUTPUT_LIMIT` overrides it. |
| `profiles.<id>.apiFamily` | string | unset | user | The API family the model must resolve to (`openai-chat`, `openai-responses`, `anthropic-messages` or `google`); it wins over discovery and the catalog and selects one of the provider's own URLs. User scope only. |
| `profiles.<id>.capabilities.apis` | string array | unset | user | API identifiers the profile declares itself able to speak; open strings, unknown facts stay allowed. User scope only. |
| `profiles.<id>.capabilities.features` | string array | unset | user | Feature names the profile declares supported; open strings, unknown facts stay allowed. User scope only. |
| `profiles.<id>.capabilities.inputModalities` | string array | unset | user | Input modalities the profile declares supported, for example `text` or `image`; open strings. User scope only. |
| `profiles.<id>.capabilities.outputModalities` | string array | unset | user | Output modalities the profile declares supported; open strings. User scope only. |
| `profiles.<id>.contextWindow` | integer | unset | user, project | The profile's context window in tokens; a project profile may lower an already-set value, never raise it. |
| `profiles.<id>.description` | string | unset | user, project | A short user-written description used as this routing option's criteria; missing descriptions are allowed but reduce routing quality. |
| `profiles.<id>.maxOutput` | integer | unset | user, project | The profile's maximum output tokens; a project profile may lower an already-set value, never raise it. |
| `profiles.<id>.model` | string | unset | user, project | The catalog model id the profile selects; required. |
| `profiles.<id>.provider` | string | unset | user, project | The provider entry the profile selects; a project profile may only reference a user provider, never define one. |
| `profiles.<id>.reasoningBudget` | integer | unset | user, project | The profile's reasoning budget in tokens. |
| `profiles.<id>.reasoningLevel` | string | unset | user, project | The profile's reasoning level. |
| `profiles.<id>.temperature` | number | unset | user, project | The profile's sampling temperature; a finite number. |
| `projection.batchTurns` | integer | 4 | user | Model turns between request-time projection rewrites. `LUNATE_PROJECTION_BATCH_TURNS` overrides it. |
| `projection.maskErrorTurns` | integer | 32 | user | The mask window for error tool results, in model turns; must be at least the success window. `LUNATE_PROJECTION_MASK_ERROR_TURNS` overrides it. |
| `projection.maskTurns` | integer | 8 | user | The mask window for successful tool results, in model turns. `LUNATE_PROJECTION_MASK_TURNS` overrides it. |
| `providers.<id>.baseUrl` | string | unset | user | The endpoint's base URL: absolute and https, or http only for a loopback host. The shorthand for `baseUrls` under the entry's default family; both together must name the same URL. Definitions live in user settings only. |
| `providers.<id>.baseUrls` | object | unset | user | One base URL per API family, keyed by family name (`openai-chat`, `openai-responses`, `anthropic-messages`, `google`); a custom entry declares exactly one. Definitions live in user settings only. |
| `providers.<id>.catalogName` | string | unset | user | The display name the model picker shows for the endpoint. |
| `providers.<id>.credentialPlacement` | string | bearer | user | How the credential is sent: `bearer`, `x-api-key`, `header:<name>` or `query:<name>` (or the equivalent object form). Definitions live in user settings only. |
| `providers.<id>.headers` | object | unset | user | Headers sent with every request: literal values, `auth:`/`env:`/`helper:` references, or templates over `{session.id}`, `{run.id}` and `{lunate.version}`. Definitions live in user settings only. |
| `providers.<id>.model` | string | unset | user | The model id requests use through the endpoint unless a selection overrides it. |
| `providers.<id>.modelsUrl` | string | unset | user | The models/discovery URL; absent means discovery derives `{base URL}/models`. Discovery never leaves the URL's host. Definitions live in user settings only. |
| `roslyn.maxLoadedWorkspaces` | integer | 2 | user | C# workspaces kept loaded across roots; a further root's load evicts the least-recently-used one. Never `unlimited`. |
| `schemaVersion` | integer | 2 | user, project | Required; 1 or 2 are readable, 2 is written. |
| `startupHeader` | boolean | true | user | Prints the welcome header when a session starts. `LUNATE_STARTUP_HEADER` overrides it. |
| `subagents.concurrency` | integer | 4 | user, project | Concurrent child runs in one tree, or `unlimited`. A project file may lower it, never raise it. `LUNATE_SUBAGENTS_CONCURRENCY` overrides it. |
| `subagents.tokenBudget` | integer | 100000 | user, project | Tokens per child run, or `unlimited`. A project file may lower it, never raise it. `LUNATE_SUBAGENTS_TOKEN_BUDGET` overrides it. |
| `subagents.treeTokenBudget` | integer | 500000 | user, project | Tokens per top-level run tree, or `unlimited`. A project file may lower it, never raise it. `LUNATE_SUBAGENTS_TREE_TOKEN_BUDGET` overrides it. |
| `thinking` | string | off | user | The session thinking level; `/thinking` changes it per session. Values: `off`, `low`, `medium`, `high`. |
| `thinkingFull` | boolean | false | user | Commits the full reasoning text instead of the collapsed thought line. |
| `toolDetails` | string | collapsed | user | How much of a tool call the scrollback commits: the header line only, or the header with the bounded output excerpt and diff panel. `LUNATE_TOOL_DETAILS` overrides it. Values: `collapsed`, `expanded`. |
| `tools.extra` | string array | [] | user | Optional search tools to register beside the core set; empty means none. `LUNATE_EXTRA_TOOLS` overrides it. Values: `grep`, `find`, `ls`. |
| `tui.fullscreen` | boolean | false | user | Starts the interactive session in the full-screen workspace. |
| `tui.keys.<id>` | array of key specs | unset | user | A binding id (see the guide's key table) remapped to its declared keys, persisted so it survives a restart. |
| `tui.theme` | string | default | user | The colour-depth policy applied to the transcript and status line; a theme can only lower the depth, so `NO_COLOR` still wins. Values: `default`, `ansi16`, `mono`. |
| `updateCheck` | boolean | true | user | Checks GitHub Releases at most once a day and links a newer release's changelog. `LUNATE_UPDATE_CHECK` overrides it. |
| `usages.chat` | string | unset | user, project | The chat profile; the usage every other usage falls back to when unassigned. |
| `usages.compaction` | string | unset | user, project | The profile used to compact a session; used only when assigned. |
| `usages.fast` | string | unset | user, project | The profile used where a fast model is wanted. |
| `usages.subagents` | string | unset | user, project | The profile child runs use. |
| `verify` | string | block | user | How staged writes behave when a write verifier refuses the content: refuse, write and report unverified, or skip verifier selection. File-only; no environment override. Values: `block`, `warn`, `off`. |
<!-- /docs-sync: settings -->

## Decision confidence and approval thresholds

For a binary decision with probability `p`, confidence is `|2p - 1|`. An existing approval
probability threshold `t` of at least `0.5` maps to `2t - 1` in confidence, so the default threshold
`0.5` remains `0`; the approval gate keeps the same behavior while using confidence consistently.

## Custom provider entry v2

The `providers.<id>` settings block describes one user-owned custom endpoint and serves exactly one
API family. New entries use `baseUrls` keyed by family; the accepted names are `openai-chat`,
`openai-responses`, `anthropic-messages` and `google`. The older `baseUrl` spelling remains readable
as the `openai-chat` family and loading it does not rewrite the file. New writes use `baseUrls`.

```json settings
{
  "schemaVersion": 2,
  "providers": {
    "my-gemini": {
      "baseUrls": { "google": "https://gemini.example/v1beta" },
      "modelsUrl": "https://gemini.example/v1beta/models",
      "credentialPlacement": "query:key",
      "model": "gemini-example"
    }
  },
  "profiles": {
    "chat": { "provider": "my-gemini", "model": "my-gemini/gemini-example" }
  }
}
```

For built-in provider presets, each model's discovery `api` object selects its family and its
`base_url` supplies that model's endpoint. The discovery family keys map as follows: `chat-completions`
to `openai-chat`, `responses` to `openai-responses`, `messages` to `anthropic-messages`, and
`generate-content` to `google`. Discovery also records input/output modalities, tool and reasoning
capabilities, reasoning efforts, context limits, and output limits. A profile's `apiFamily` override
wins over discovery metadata and the catalog. A custom provider's discovered endpoint and redirects
must stay on its configured host; a preset may use its first-party per-family endpoints. See
[ADR-0029](../adr/0029-provider-api-families.md) for the accepted provider-family decision.

Headers may be literal values or templates over `{session.id}`, `{run.id}`, and `{lunate.version}`.
The credential is separate: `credentialPlacement` selects the default bearer header, a named header
such as `x-api-key`, or a query parameter such as Google's `key`. Templates cannot read credentials;
invalid placeholders and placements are rejected when the provider loads.

## Where thinking resolves from

The session's thinking level resolves once at start, from the most specific fact available: the
`--thinking` launch flag wins over the chat profile's `profiles.<chat>.reasoningLevel`, which wins
over the file's global `thinking` key, and without any of them the level is `off` — a profile
without a reasoning setting falls back to `thinking` exactly as before. `/thinking <level>` changes
the running session only: the settings file and the profile stay as they are. `/settings thinking
<level>` is what persists — it writes the chat profile's `reasoningLevel` (`off` removes the key,
because a profile has no `off`), and a chat selection that is not a configured profile in the user
settings file is refused. A model whose catalog entry declares no reasoning style refuses a level by
name in both commands, naming the model and the setting. A profile `reasoningBudget` is a validated
configuration fact, not a session default. The setup wizard edits the same profile key (#475).

## Where the rest of configuration is written

- Provider entries, credentials and the first-run path: [first-run.md](first-run.md).
- Environment-variable overrides, `/settings`, and the key remaps the `tui.keys` entries
  persist: [guide.md](guide.md).
- Why the parser is deny-by-default and every problem fails the load as one error:
  [`openspec/specs/agent-config/`](../openspec/specs/agent-config/spec.md) owns that behaviour.

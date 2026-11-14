# Settings reference

Every key the settings parser accepts, and only those keys. The table below is generated from
[`src/Lunate.Coding/settings-schema.json`](../src/Lunate.Coding/settings-schema.json) — the same
file the store's `SettingsSchema` table reads and tests pin to the parser's own literals — so it
cannot list a key the store refuses. Edit the schema, then run `scripts/docs-sync` to regenerate;
`--check` fails a stale page. How to configure a session — decision profiles, provider entries and
the rest of the file — is in [configuration](configuration.md).

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
| `decisions.approvalsThreshold` | number | 0 | user | A finite confidence from 0 through 1: what a risk signal's chosen answer must reach to escalate the approval ask. Confidence, not probability, is the scale; 0 is the shipped 0.5 probability gate (confidence = 2 x probability - 1). |
| `decisions.profile` | string | unset | user | The decision profile the layer runs on; without it the layer is unavailable and nothing is offered, never erroring. |
| `decisions.questions` | string | off | user | Adds a probability marker beside the top option of a question; always display-only, the user still chooses. Values: `off`, `suggest`. |
| `decisions.routing` | string | off | user | The model-routing consumer; applies the most probable profile only when its confidence meets routingMinConfidence, otherwise keeps the configured chat profile. Values: `off`, `shadow`, `enforce`. |
| `decisions.routingMinConfidence` | number | 0.5 | user | A finite confidence threshold from 0 through 1; routing changes the configured chat profile only at or above this value. |
| `decisions.routingProfiles` | string array | [] | user | Two to 255 unique configured profile names the router may select among; routing requires at least two. |
| `decisions.tool` | string | off | user | Offers the `decide` tool, only when `decisions.profile` resolves as well. Values: `off`, `on`. |
| `environment` | string | unset | user | The container environment process-backed tool commands run in: `docker:<image>` or `podman:<image>`; unset keeps host execution. The workspace is mounted at its host absolute path, while the state root, install prefix and `auth.json` are never mounted. `--env` overrides it for the run. There is no environment-variable override. |
| `favorites` | string array | [] | user | The ordered favourite model ids, at most eight unique entries; schema 2 and later. |
| `maxCost` | number | unset | user | The cumulative cost ceiling of a session in US dollars; off when unset. A resumed session seeds its spend from its recorded usages. A cost limit over a model the catalog cannot price refuses to start unless `--allow-unpriced` is passed. Print mode can override it with `--max-cost`. |
| `maxTime` | integer | unset | user | The wall-clock ceiling of one run in seconds; off when unset. Print mode can override it with `--max-time`. |
| `model` | string | unset | user | The default model id. `LUNATE_MODEL` overrides it. |
| `output.spillThresholdTokens` | integer | 2000 | user, project | The estimated-token threshold (UTF-8 bytes / 4) above which a tool result is spilled. A project file may lower it, never raise it. `LUNATE_TOOL_SPILL_THRESHOLD` overrides it. |
| `output.toolResultLimit` | integer | 30000 | user, project | Characters of one tool result before truncation. A project file may lower it, never raise it. `LUNATE_TOOL_OUTPUT_LIMIT` overrides it. |
| `profiles.<id>.apiFamily` | string | unset | user | The API family the model must resolve to (`openai-chat`, `openai-responses`, `anthropic-messages`, `google` or `systemone`); it wins over discovery and the catalog and selects one of the provider's own URLs. User scope only. |
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
| `providers.<id>.baseUrls` | object | unset | user | One base URL per API family, keyed by family name (`openai-chat`, `openai-responses`, `anthropic-messages`, `google`, `systemone`); a custom entry declares exactly one. Definitions live in user settings only. |
| `providers.<id>.catalogName` | string | unset | user | The display name the model picker shows for the endpoint. |
| `providers.<id>.credentialPlacement` | string | bearer | user | How the credential is sent: `bearer`, `x-api-key`, `header:<name>`, `header:<name>;scheme=<scheme>` (for example `header:Authorization;scheme=Token`) or `query:<name>` (or the equivalent object form). Definitions live in user settings only. |
| `providers.<id>.headers` | object | unset | user | Headers sent with every request: literal values, `auth:`/`env:`/`helper:` references, or templates over `{session.id}`, `{run.id}` and `{lunate.version}`. Definitions live in user settings only. |
| `providers.<id>.model` | string | unset | user | The model id requests use through the endpoint unless a selection overrides it. |
| `providers.<id>.modelsUrl` | string | unset | user | The models/discovery URL; absent means discovery derives `{base URL}/models`. Discovery never leaves the URL's host. Definitions live in user settings only. |
| `providers.<id>.region` | string | unset | user | The provider's region metadata: `eu`, `us` or `local`. Preserved as metadata; it never routes data or enforces a region policy. Definitions live in user settings only. |
| `roslyn.maxLoadedWorkspaces` | integer | 2 | user | C# workspaces kept loaded across roots; a further root's load evicts the least-recently-used one. Never `unlimited`. |
| `schemaVersion` | integer | 2 | user, project | Required; 1 or 2 are readable, 2 is written. |
| `skills.indexBudget` | number or 'unlimited' | unset | user | The skills index budget in estimated tokens: a positive integer overrides the computed thresholds (soft 2%, hard ceiling 5% of the model's context window) with one budget whose overflow follows the hard-ceiling behaviour; the literal `unlimited` indexes every skill with no notice and no warning at any size. Unset keeps the computed thresholds, which derive from a known context window; when the window is unknown both thresholds are disabled (the complete index, no notice, no warning) unless this key applies. There is no environment-variable override. |
| `startupHeader` | boolean | true | user | Prints the welcome header when a session starts. `LUNATE_STARTUP_HEADER` overrides it. |
| `subagents.concurrency` | integer | 4 | user, project | Concurrent child runs in one tree, or `unlimited`. A project file may lower it, never raise it. `LUNATE_SUBAGENTS_CONCURRENCY` overrides it. |
| `subagents.tokenBudget` | integer | 100000 | user, project | Tokens per child run, or `unlimited`. A project file may lower it, never raise it. `LUNATE_SUBAGENTS_TOKEN_BUDGET` overrides it. |
| `subagents.treeTokenBudget` | integer | 500000 | user, project | Tokens per top-level run tree, or `unlimited`. A project file may lower it, never raise it. `LUNATE_SUBAGENTS_TREE_TOKEN_BUDGET` overrides it. |
| `thinking` | string | off | user | The session thinking level; `/thinking` changes it per session. Values: `off`, `low`, `medium`, `high`. |
| `thinkingFull` | boolean | false | user | Commits the full reasoning text instead of the collapsed thought line. |
| `toolDetails` | string | collapsed | user | How much of a tool call the scrollback commits: the header line only, or the header with the bounded output excerpt and diff panel. `LUNATE_TOOL_DETAILS` overrides it. Values: `collapsed`, `expanded`. |
| `toolEnvironment.passthrough` | string array | [] | user | Exact environment variable names to admit into tool processes after the inherited environment's credential denies (the provider keys Lunate resolved and every name shaped like a credential); a listed name the parent does not hold stays absent. An admitted variable is reachable by the model through tool processes: the model can read it from a command it runs. The admitted names are listed at session start. No wildcards, prefixes or patterns. A name that is also a Lunate credential name is admitted with one startup warning; a name only the credential-shape rule withholds is admitted with the same notice and no warning. `Scrubbed` extension hosts are unaffected. User scope only: a project settings file may not carry the key. There is no environment-variable override. |
| `tools.extra` | string array | [] | user | Optional search tools to register beside the core set; empty means none. `LUNATE_EXTRA_TOOLS` overrides it. Values: `grep`, `find`, `ls`. |
| `tui.fullscreen` | boolean | false | user | Starts the interactive session in the full-screen workspace. |
| `tui.keys.<id>` | array of key specs | unset | user | A binding id (see the keybindings reference) remapped to its declared keys, persisted so it survives a restart. |
| `tui.theme` | string | default | user | The colour-depth policy applied to the transcript and status line; a theme can only lower the depth, so `NO_COLOR` still wins. Values: `default`, `ansi16`, `mono`. |
| `updateCheck` | boolean | true | user | Checks GitHub Releases at most once a day and links a newer release's changelog. `LUNATE_UPDATE_CHECK` overrides it. |
| `usages.chat` | string | unset | user, project | The chat profile; the usage every other usage falls back to when unassigned. |
| `usages.compaction` | string | unset | user, project | The profile used to compact a session; used only when assigned. |
| `usages.fast` | string | unset | user, project | The profile used where a fast model is wanted. |
| `usages.subagents` | string | unset | user, project | The profile child runs use. |
| `verify` | string | block | user | How staged writes behave when a write verifier refuses the content: refuse, write and report unverified, or skip verifier selection. File-only; no environment override. Values: `block`, `warn`, `off`. |
<!-- /docs-sync: settings -->

## Related

- [Configuration](configuration.md) — how to configure a session
- [Environment variables](environment-variables.md) — which variable overrides which key
- [Keybindings reference](keybindings.md) — the `tui.keys` entries
- [Models and providers](models-and-providers.md) — profiles and provider entries in practice

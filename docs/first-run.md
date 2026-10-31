# First run

Lunate needs a model profile and a credential source for its provider before the first prompt.
Use the first-run picker for a built-in provider, or define a named provider instance and profile in
your user configuration before starting the TUI.

## The shortest path

From a terminal:

```bash
lunate auth login
```

With no provider argument the command prints the supported provider ids and names and exits —
0 on a terminal, and 2 with the same list on stderr when stdin is a pipe. It never waits for a key
nobody can type. Then choose one:

```bash
lunate auth login anthropic
```

On a terminal the key is read through a masked prompt. In a script, pipe one line to the command:

```bash
printf '%s\n' "$ANTHROPIC_API_KEY" | lunate auth login anthropic --key-stdin
```

Neither path echoes the value. A pipe without `--key-stdin` exits 2 with the hint instead of
guessing.

`lunate auth login` is one of four subcommands:

| Command | What it does |
| --- | --- |
| `lunate auth list` | One line per provider entry with credential presence and bounded reachability (`reachable`, `unreachable`, or `unknown`). Never prints a credential or header value. Exits 0 even when nothing is stored. |
| `lunate auth login <provider>` | Stores a key under the provider entry name. Use `--key-stdin` to read one line from a pipe. |
| `lunate auth logout <provider>` | Removes the stored entry, names a still-set environment variable without its value, and exits non-zero when nothing was stored. |
| `lunate auth status` | Reports the resolved default provider and whether a credential exists; exits 0 when it does and 1 when it does not, so a bootstrap script can branch on it. |

## Provider instances and credentials

The twelve built-in provider kinds are templates for adapters and defaults. A user may define
multiple named provider entries of one kind in `~/.lunate/providers.json`:

```json
{
  "schemaVersion": 1,
  "providers": [
    {
      "name": "work-openai",
      "kind": "openai",
      "baseUrl": "https://api.openai.com/v1",
      "credentialReference": "auth:work-openai",
      "region": "eu"
    }
  ]
}
```

`credentialReference` and optional header values are references, never secret values. Supported
sources are `auth:<key>`, `env:<VARIABLE>`, and `helper:<name>`. The credential itself stays in
`auth.json`, the environment, or the configured helper. `region` (`eu`, `us`, or `local`) is
descriptive metadata only: it does not choose an endpoint or guarantee data residency.

On load, Lunate idempotently migrates legacy stored keys and custom endpoints into matching provider
entries. An explicit provider entry wins, and migration never copies the secret out of `auth.json`.
Environment credentials continue to take precedence over stored keys, including in CI. Reachability
shown by `auth list` is a bounded unauthenticated probe; it sends neither credentials nor optional
headers.

Project settings may refer to user-defined providers and profiles or lower user limits. They cannot
define a provider, endpoint, credential reference, or header secret. This prevents a repository from
redirecting a user's credentials to its own server.

## Profiles and usages

`~/.lunate/settings.json` binds a profile name to a provider entry and catalog model, then assigns
profiles to existing usages. For example:

```json
{
  "schemaVersion": 2,
  "profiles": {
    "chat-main": { "provider": "work-openai", "model": "gpt-4o-mini" },
    "reviewer": { "provider": "work-openai", "model": "gpt-4o-mini" }
  },
  "usages": { "chat": "chat-main", "decision": "reviewer" }
}
```

Profiles may set supported reasoning, output, temperature, context-window, decision-threshold, and
capability-override fields. Unsupported settings are refused by field and profile name before a
request. `/model` changes only the chat profile; `provider/model` selects through a named provider,
and a bare model id must be unique. Subagents and compaction default to chat when unassigned;
compaction uses `fast` only when explicitly selected.

Model routing is separate from the approval decision. In user settings, set `routingMode` to `shadow`
to record a suggested profile without changing the configured chat model, or `enforce` to use the
selected profile; `off` is the default. `routingProfiles` names 2–255 configured profiles that the
router may choose. An unavailable decision profile or an invalid response leaves the configured chat
model in use. Routing decisions are charged to the `decision` usage and `/cost` distinguishes the
`native` and `adapter` methods.

Decision use is the user's choice, not a model-intelligence verdict. The consumer is off by default
and must be enabled in user settings. Any configured model may be used; known structured-output
support is checked as a technical requirement, while unknown capability facts are allowed. A
decision response must name a defined option and provide valid probabilities; a malformed response
is retried once and then fails with the model named. Provenance labels `native` or `adapter` and
records the model id. Decisions can only tighten approvals: they never override deny or approve
outside the user's allow envelope. See [`guide.md`](guide.md) for the full model and cost behavior.

## The in-session picker

Start `lunate` in a repository. When the session has no usable credential and stdin and stdout are
terminals, the first run opens the auth picker once. It lists the twelve presets in
[`providers.json`](../src/Lunate.Ai/providers.json) order, followed by
`Custom OpenAI-compatible endpoint…`:

- `↑`/`↓` or `j`/`k` move the selection.
- `Enter` selects.
- `Esc` dismisses the picker without writing anything.

Selecting a preset opens the same masked prompt as `lunate auth login`. After a successful save the
notice names the provider and points at `/model`; it never contains the key. Lunate then offers an
optional verification: one minimal request, asked first, and a failure reports the reason while
keeping the stored key. Skip it if the network is metered or the machine is air-gapped.

`/auth` opens the picker again at any time. `/login <provider>` remains an alias that skips the
picker and opens the masked prompt directly; `/logout <provider>` removes a stored key. While a
turn is running, `/auth` and `/login` refuse rather than prompt.

Print mode, ACP and runs with piped stdin never prompt. A missing credential stays the explicit
error that names the key and the environment variable, and the empty-session banner names `/auth`
as the next step whenever nothing is configured.

## A custom OpenAI-compatible endpoint

For Ollama, vLLM, LM Studio or any other OpenAI-compatible server, choose the custom row at the
bottom of the `/auth` picker, or run:

```bash
lunate auth login --custom
```

The interactive flow asks for four fields in order: provider id, base URL, model id and key. A
script supplies the first three as flags and the key on stdin:

```bash
printf '%s\n' "$LOCAL_KEY" |
  lunate auth login --custom \
    --id local --base-url http://localhost:11434/v1 --model llama3 --key-stdin
```

Saving validates the entry:

- The base URL must be absolute and `https`, or `http` only for a loopback host.
- The model id is required.
- The provider id must be non-empty, must not contain spaces or `/`, and must not shadow a built-in
  preset.

A refused entry is not echoed and writes nothing. `lunate auth login --custom` remains available for
legacy single-endpoint setup. Its endpoint is migrated into the named provider store while the key
remains in `auth.json`; new multi-provider setups should use `providers.json` and a profile in
`settings.json`. The model can then be selected in `/model` by its qualified provider/model id.

## Where the credential lives

Stored keys live in `~/.lunate/auth.json` under `keys`, schema 1, with owner-only permissions where
the platform has file modes. `providers.json` and `settings.json` contain only provider/profile
configuration and credential references, never resolved secret values.

Every preset also has a provider environment variable — `OPENAI_API_KEY`, `ANTHROPIC_API_KEY`,
`GROQ_API_KEY` and so on; Google also accepts `GEMINI_API_KEY`. When that variable is set it wins
over the key stored in the file. `lunate auth list` shows which source is in use, and
`lunate auth status` answers whether the resolved default provider has a credential at all.

## If the first run refuses

- `set 'model' in ~/.lunate/settings.json` — no default model is configured. Set `model` or pass
  `--model`.
- `run /auth to configure a provider` — the model is known but no preset credential resolves. Run
  `/auth` in the session, or `lunate auth login <provider>` before starting it.
- `set <VARIABLE> to a valid key` — the variable is present but empty or unusable. Set a non-empty
  value or store the key with `lunate auth login`.

Those are the interactive header's fixes. Print mode has no picker: a run that cannot resolve a
credential exits 1 with the client factory's message, which names the variable and the missing key
(for example `The OPENAI_API_KEY environment variable is not set and no 'openai' key is stored. Set
OPENAI_API_KEY or add the key to auth.json.`).

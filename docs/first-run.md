# First run

Lunate needs two things before the first prompt: a model and a credential for that model's
provider. Both can be set up before the TUI starts, or from the first-run picker inside it.

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
| `lunate auth list` | One line per provider: `stored`, `environment (<VARIABLE>)` or `not set`. Never prints a value. Exits 0 even when nothing is stored. |
| `lunate auth login <provider>` | Stores a key under the provider id. Use `--key-stdin` to read one line from a pipe. |
| `lunate auth logout <provider>` | Removes the stored entry, names a still-set environment variable without its value, and exits non-zero when nothing was stored. |
| `lunate auth status` | Reports the resolved default provider and whether a credential exists; exits 0 when it does and 1 when it does not, so a bootstrap script can branch on it. |

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

A refused entry is not echoed and writes nothing. A stored custom provider keeps its key in
`auth.json` under the provider id and its endpoint in `settings.json` as a `providers` entry — one
secret store, not two. Its catalog name is `<id>/<model>` (for example `local/llama3`), so it
appears in `/model` beside the presets.

## Where the credential lives

The key is stored in `~/.lunate/auth.json` under `keys`, schema 1, owner-only permissions where the
platform has file modes. That is the only place Lunate stores a provider secret.

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

The same messages appear in print mode, where there is no picker to open.

## MODIFIED Requirements

### Requirement: Model catalog merge
`ModelCatalog` SHALL merge the embedded `models.json` with `~/.lunate/models.json`; user entries SHALL override built-ins by id; adding a model SHALL never require a code change. A model MAY declare its reasoning style with `reasoning`: `"effort"` (OpenAI-compatible reasoning effort), `"budget"` (Anthropic extended thinking) or `"inline"` (the model embeds ` thinking…</think>` in its text stream); a model without the field declares no reasoning support and behaves exactly as before.

Schema 2 SHALL add optional catalog metadata: `pricing` with `input`, `output`, `cacheRead` and `cacheWrite` rates per million tokens, `maxOutput`, `supportsVision` and `wireId`. Schema 1 files SHALL remain readable: a schema 1 entry has no pricing, `maxOutput` or vision support and uses its own id as the wire id; a schema 1 entry that overrides an existing id SHALL keep the existing entry's `pricing`, `maxOutput`, `supportsVision` and `wireId`, because schema 1 cannot express them. The loader SHALL merge, in increasing precedence, the embedded snapshot, the optional managed cache at `~/.lunate/models.catalog.json` and the user file at `~/.lunate/models.json`; the managed cache SHALL be read only when no explicit user path is injected. A duplicate id within one file SHALL fail and name the id; a duplicate generated wire id SHALL fail generation.

#### Scenario: User override wins
- **GIVEN** a built-in model and a user file redefining the same id
- **WHEN** the catalog is loaded
- **THEN** the user definition is used

#### Scenario: New model without code changes
- **GIVEN** a user `models.json` with a new id
- **WHEN** the catalog is loaded
- **THEN** the model is available

#### Scenario: Duplicate id within one file fails
- **GIVEN** a `models.json` containing the same id twice
- **WHEN** the catalog is loaded
- **THEN** loading fails with an error naming the id

#### Scenario: A model declaring its reasoning style

- **GIVEN** a `models.json` entry with `"reasoning": "effort"`
- **WHEN** the catalog is loaded
- **THEN** the model's reasoning style is available to the factory, and a model without the field reports none

#### Scenario: A schema 1 catalog still loads

- **GIVEN** a user `models.json` with `schemaVersion: 1` and a model entry without pricing
- **WHEN** the catalog is loaded
- **THEN** the model is available with no pricing, no maximum output, vision off, and its own id as the wire id

#### Scenario: The managed cache merges below the user file

- **GIVEN** an embedded entry, a managed cache entry with the same id, and a user entry with that id
- **WHEN** the catalog is loaded
- **THEN** the user entry wins, and an id present only in the managed cache is available

## ADDED Requirements

### Requirement: Generated catalog snapshot

The built-in catalog SHALL be a deterministic, committed snapshot of models.dev for the preset providers, generated with catalog schema 2. It SHALL carry `source` metadata naming models.dev, its URL and the source SHA-256, and SHALL NOT carry a generation timestamp. Entries SHALL map `limit.context` to `contextWindow`, `limit.output` to `maxOutput`, `tool_call` to `supportsTools`, `attachment` to `supportsVision`, the four `cost` rates to `pricing`, and a true `reasoning` flag to `budget` for Anthropic and `effort` for OpenAI-compatible presets. `endpoint` SHALL be absent from generated entries; a preset supplies the base URL. Ordering SHALL be preset-config order and then model id ordinal.

A repo script SHALL regenerate `src/Lunate.Ai/models.json` through the same C# mapper the runtime uses; there SHALL NOT be a second mapping implementation. `lunate --update-catalog` SHALL fetch models.dev explicitly, validate the mapping, write the managed cache atomically, and leave `~/.lunate/models.json` untouched. It SHALL make no model call, and a failure SHALL leave the existing cache and catalog unchanged. With an output path (used by the script) it SHALL write the embedded-shaped file instead. No catalog download SHALL happen at startup.

#### Scenario: The snapshot is deterministic

- **GIVEN** the same models.dev response and the same preset configuration
- **WHEN** the generator runs twice
- **THEN** the two `models.json` files are byte-identical and carry the same source SHA-256

#### Scenario: Generated capabilities are mapped

- **GIVEN** a models.dev entry with `limit.context = 200000`, `limit.output = 64000`, `attachment = true`, `tool_call = true` and four costs
- **WHEN** it is mapped
- **THEN** the catalog entry carries that context window, maximum output, vision support, tool support and the four prices

#### Scenario: An explicit refresh writes the managed cache

- **GIVEN** no managed cache
- **WHEN** `lunate --update-catalog` succeeds
- **THEN** `~/.lunate/models.catalog.json` contains the schema-2 snapshot and `~/.lunate/models.json` is unchanged

#### Scenario: A failed refresh changes nothing

- **GIVEN** an existing managed cache and an unreachable models.dev
- **WHEN** `lunate --update-catalog` runs
- **THEN** it exits non-zero with an actionable message and the existing cache bytes are unchanged

#### Scenario: Startup never downloads

- **GIVEN** a machine with no network
- **WHEN** a session or print run starts
- **THEN** no catalog download is attempted and the embedded snapshot serves the catalog

### Requirement: Provider presets and routing

Lunate SHALL provide presets for `openai`, `anthropic`, `openrouter`, `deepseek`, `moonshotai`, `zai`, `opencode`, `opencode-go`, `groq`, `togetherai`, `xai` and `google`, defined in one embedded `providers.json` with an adapter kind, a base URL and a key variable. `openai` and `anthropic` SHALL keep their native adapters; every other preset SHALL use the OpenAI-compatible adapter with the preset base URL when the model declares no endpoint. The provider call SHALL use `wireId` when present and the catalog id otherwise. A model with an explicit endpoint SHALL keep that endpoint, and a user-declared endpoint SHALL keep the placeholder-credential rule. An unknown provider SHALL fail with an actionable error listing the preset ids.

For a preset whose base URL is the provider's own endpoint, the credential SHALL resolve from the preset's environment variable first, then the auth store under the provider name, unless the model declares an `auth` reference, which wins. The Google preset SHALL accept `GOOGLE_API_KEY` and `GEMINI_API_KEY`.

#### Scenario: A preset routes through the OpenAI adapter

- **GIVEN** a catalog entry with provider `groq` and no endpoint
- **WHEN** the factory builds the pipeline
- **THEN** the client targets the Groq preset base URL and sends the model's wire id

#### Scenario: A wire id differs from the catalog id

- **GIVEN** a catalog entry `openrouter/anthropic/claude-sonnet-5-5` with `wireId` `anthropic/claude-sonnet-5-5`
- **WHEN** the request is built
- **THEN** the provider receives `anthropic/claude-sonnet-5-5` and the catalog lookup still uses the qualified id

#### Scenario: Anthropic stays native

- **GIVEN** a catalog entry with provider `anthropic` and no endpoint
- **WHEN** the factory builds the pipeline
- **THEN** the Anthropic adapter serves it with the SDK default endpoint

#### Scenario: A user endpoint keeps the placeholder

- **GIVEN** a preset model overridden with a user-declared endpoint and no `auth` reference
- **WHEN** the factory builds the pipeline
- **THEN** the client uses the placeholder credential, not the preset environment key

#### Scenario: The preset key resolves from the environment then the store

- **GIVEN** a `groq` model with no `GROQ_API_KEY` and a stored `groq` key
- **WHEN** the factory builds the pipeline
- **THEN** the stored key authenticates the client; with `GROQ_API_KEY` set, the environment value wins

#### Scenario: An unknown provider names the presets

- **GIVEN** a catalog entry with provider `not-a-provider`
- **WHEN** the factory builds the pipeline
- **THEN** it fails with an error naming the supported preset ids

### Requirement: Model id namespace

The four existing ids `gpt-4o-mini`, `gpt-4o`, `claude-sonnet-5-5` and `claude-opus-5-5` SHALL stay bare. Other native OpenAI and Anthropic ids SHALL be bare. Every non-native model id SHALL be `<provider>/<model>`, and the upstream id SHALL travel in `wireId` rather than be parsed from the catalog id. Lookup SHALL be exact on the catalog id. Discovery against a preset SHALL emit qualified ids with `wireId`; discovery against a URL SHALL keep the existing bare OpenAI-provider draft.

#### Scenario: The four existing ids resolve unchanged

- **GIVEN** a catalog generated from the current models.dev snapshot
- **WHEN** the four starter ids are looked up
- **THEN** each resolves to its native provider and keeps its bare id

#### Scenario: A non-native id is qualified

- **GIVEN** an OpenRouter model `anthropic/claude-sonnet-5-5`
- **WHEN** the snapshot is generated
- **THEN** its catalog id is `openrouter/anthropic/claude-sonnet-5-5` and its wire id is `anthropic/claude-sonnet-5-5`

#### Scenario: Discovery of a preset is qualified

- **GIVEN** `lunate --discover openrouter`
- **WHEN** the endpoint lists `anthropic/claude-sonnet-5-5`
- **THEN** the draft entry's id is `openrouter/anthropic/claude-sonnet-5-5`, its provider is `openrouter`, and it carries the wire id

### Requirement: Model pricing and cost computation

`ModelInfo` SHALL expose optional pricing rates per million tokens, a maximum output limit and vision support. A cost SHALL be computed as the sum of each lane's token count multiplied by its rate, divided by one million, with cached input removed from the uncached input lane. A record SHALL be fully priced only when every lane with a positive token count has a known rate; a missing rate SHALL make the record unpriced rather than zero. A model without pricing SHALL be unpriced. The computation SHALL use `decimal` and `CultureInfo.InvariantCulture` everywhere it formats a number.

#### Scenario: Cache lanes use their own rates

- **GIVEN** 1,000,000 input tokens of which 200,000 are cache reads and 100,000 are cache writes, plus 10,000 output tokens, with all four rates known
- **WHEN** the cost is computed
- **THEN** the uncached input, cache read, cache write and output lanes are each priced at their own rate

#### Scenario: An unknown rate for a used lane is unpriced

- **GIVEN** a positive cache-read count and no cache-read rate
- **WHEN** the cost is computed
- **THEN** the result is unpriced and no currency value is produced

#### Scenario: An unused lane needs no rate

- **GIVEN** input and output rates, a null cache rate and zero cache tokens
- **WHEN** the cost is computed
- **THEN** the record is fully priced

#### Scenario: A model without pricing is unpriced

- **GIVEN** a catalog entry with no `pricing`
- **WHEN** a cost is requested
- **THEN** the result is unpriced and the display shows tokens only

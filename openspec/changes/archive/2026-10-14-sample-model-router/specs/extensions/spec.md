## MODIFIED Requirements

### Requirement: Model provider registry

Extensions SHALL declare model providers in the manifest (`id`, `displayName`, `endpoint`, `secretName`, `modelIds`, `contextWindow` — a positive integer — and optional `supportsTools`, default true). Declarations SHALL be validated (namespaced id, absolute http(s) endpoint, non-empty model ids, a present `contextWindow` — local models are often small, so a wrong window would mis-time compaction — `secretName` referencing the secrets store by name, never a value); duplicate provider ids SHALL be refused. The registry SHALL list declared providers. Declared providers SHALL be instantiated lazily into OpenAI-compatible clients on first use: the host reads the secret value from the extension's secrets store and the value SHALL only ever reach the client's request headers — this is hygiene for declared providers, not a security boundary (in-process extensions are fully trusted, ADR-0017). A missing secret SHALL be a teaching error naming the provider and `secretName`; a provider error reporting that tools are unsupported SHALL surface as a teaching error naming the provider and its `supportsTools` declaration.

#### Scenario: A declared provider is listed
- **GIVEN** a manifest declaring a valid model provider
- **WHEN** the extension loads
- **THEN** the registry lists it with its display name, endpoint and model ids

#### Scenario: Secret values never appear
- **GIVEN** a provider declaration with a `secretName`
- **WHEN** the provider is listed or logged
- **THEN** only the name appears, never the secret value

#### Scenario: Duplicate provider ids are refused
- **GIVEN** two extensions declaring the same provider id
- **WHEN** the second loads
- **THEN** loading fails naming both extensions

#### Scenario: A declared provider is instantiated on first use
- **GIVEN** a declared provider whose `secretName` resolves in the extension's secrets store
- **WHEN** one of its model ids is first used
- **THEN** the request reaches the declared endpoint with the secret as its authorization header, and no extension-visible surface carries the value

#### Scenario: A declaration without a context window is refused
- **GIVEN** a manifest provider entry without `contextWindow`
- **WHEN** the extension loads
- **THEN** loading fails naming the field

## ADDED Requirements

### Requirement: Model registry: middleware and virtual models

Extensions SHALL register model providers, middleware and virtual models at load time (`IExtensionContext.RegisterModelProvider`, `RegisterModelMiddleware`, `RegisterVirtualModel` — registration only, no processes, sockets or timers), all ids namespaced `ext/<extension-id>/<id>`; duplicates SHALL be refused naming both extensions, and per-extension caps SHALL apply (8 providers, 8 virtual models, 16 middlewares). Middleware SHALL be `DelegatingChatClient` wrappers at defined slots (`BeforeRouting` — sees every call, including calls to virtual models; `AroundBackend` — sees only calls that reach a backend), composed deterministically: extension load order, then middleware priority, then registration order; a middleware exception SHALL fail the request closed with an error naming the owning extension, never a silent skip, and `--no-extensions` SHALL bypass all extension models and middleware. A virtual model SHALL route **per request**: each call evaluates its selector over the request's messages and resolves to one of its backends, and one client instance MAY route consecutive calls to different backends. Backends MAY themselves be virtual models; the chain SHALL be checked at first resolve for cycles and a bounded depth, and an unknown backend SHALL be a teaching error naming the virtual model and the backend. Virtual-model capabilities SHALL default to the weakest backend (minimum context window; tools only when every backend supports them); a declaration may only narrow, and an over-wide declaration SHALL be refused at first resolve. Model id resolution SHALL follow: virtual model → programmatic provider → declared provider → session catalog → teaching error listing known ids; extension-registered ids SHALL never shadow catalog model ids.

#### Scenario: Middleware wraps at its slot
- **GIVEN** a middleware registered at `AroundBackend` and one at `BeforeRouting`
- **WHEN** a call reaches a backend and another goes to a virtual model
- **THEN** the backend wrapper saw only the backend call, and the routing wrapper saw both

#### Scenario: Middleware order is deterministic
- **GIVEN** middlewares registered across two extensions and within one, with priorities
- **WHEN** a request is composed
- **THEN** wrappers apply in load order, then priority, then registration order

#### Scenario: A throwing middleware fails closed
- **GIVEN** a middleware that throws
- **WHEN** a request goes through it
- **THEN** the request fails with an error naming the owning extension and nothing reaches the provider

#### Scenario: A virtual model routes per request
- **GIVEN** a virtual model with two backends and a request-size selector
- **WHEN** two requests of different sizes are sent through one client instance
- **THEN** each request reaches its selected backend

#### Scenario: Unknown backends, cycles and unknown ids are teaching errors
- **GIVEN** a virtual model whose backend id is not registered, a virtual-model cycle, and an unknown model id
- **WHEN** each is first resolved
- **THEN** each is refused with a teaching error naming the id and the known candidates or the chain

### Requirement: Nested model calls charge the owning run

A run's tool context SHALL expose run-bound model access (`ToolContext.Models`, stamped by the harness like the other per-run seams; null outside a run). Every nested model call through the returned client SHALL emit `UsageUpdated` for the owning run — carrying the resolved model id — count into the run's token budget, and feed the inherited tree accounting when the run is a child run: the same accounting path as the run's own model calls. Inside a subagent, a tool's nested call SHALL charge the child run and roll up to the tree.

#### Scenario: Nested usage rolls up to the owning run
- **GIVEN** a tool that makes a model call through `ToolContext.Models`
- **WHEN** the provider reports usage for that call
- **THEN** the owning run's event stream carries `UsageUpdated` with those tokens and the resolved model id, and the run's token budget counts them

#### Scenario: A subagent tool's nested call charges the child
- **GIVEN** a tool running inside a subagent child run
- **WHEN** it makes a nested model call
- **THEN** the child run's usage carries the tokens and the tree accounting rolls them up to the parent

#### Scenario: Outside a run there is no model access
- **GIVEN** a `ToolContext` built outside a run
- **WHEN** `Models` is read
- **THEN** it is null

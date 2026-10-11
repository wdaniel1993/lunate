## ADDED Requirements

### Requirement: Extension UI contributions

The contract assembly SHALL define a JSON-serializable text-segment model (`TextSegment` with a style of plain, dim, accent, warning or error) and the handler interfaces `IToolResultRenderer` (rendering a named tool's result into segments) and `IStatusWidget` (extra footer segments), registered through `IExtensionContext` at creation time only. Every UI DTO SHALL round-trip through `System.Text.Json`. Renderers SHALL match by tool name (ordinal); several renderers for one name SHALL resolve to the last registration with the conflict logged. A widget SHALL be invoked on each status update; a throwing widget or renderer SHALL be omitted for that update and reported through the host log, then retried on the next update — the hook failure policy applied to UI. The API version SHALL move as a semver-minor addition.

#### Scenario: UI DTOs round-trip through JSON

- **GIVEN** a text segment, a tool-result view and a status view
- **WHEN** each is serialized and deserialized
- **THEN** the values are identical

#### Scenario: Registration is registration only

- **GIVEN** an extension registering a renderer and a widget
- **WHEN** its factory runs
- **THEN** no rendering is invoked and no processes, sockets or timers are started

#### Scenario: Renderer conflicts resolve to the last registration

- **GIVEN** two renderers declaring the same tool name
- **WHEN** a result of that tool arrives
- **THEN** the last registered renderer runs and the conflict is logged

#### Scenario: A throwing contribution is omitted, reported and retried

- **GIVEN** a widget that throws on its first invocation
- **WHEN** two status updates occur
- **THEN** the first omits it and logs the failure, the run continues, and the second invokes it again

# Settings and secrets

Settings and secrets are per-extension JSON files in the user store. The extension reads them
through `IExtensionContext`; the host never writes them and never prints their values.

| Kind | Path | Read with |
| --- | --- | --- |
| Settings | `~/.lunate/extensions-settings/<id>.json` | `context.Settings.TryGet(path, out JsonElement value)` |
| Secrets | `~/.lunate/extensions-secrets/<id>.json` | `context.Secrets.TryGet(name, out string? value)` |

The path is always the user store, including for a project extension. There is no project-scoped
settings file.

## Settings

`TryGet` takes a dotted path into the settings object: `TryGet("mode", …)` for a top-level key,
`TryGet("server.timeout", …)` for a nested one. It returns false when the key is absent or an
intermediate value is not an object.

```csharp
if (settings.TryGet("mode", out JsonElement value) && value.ValueKind == JsonValueKind.String)
{
    string? mode = value.GetString();
}
```

The settings file must be a JSON object. Invalid JSON or an unreadable file refuses the load:

```text
extension 'hello': settings file '/home/me/.lunate/extensions-settings/hello.json' is not valid JSON: …
extension 'hello': settings file '/home/me/.lunate/extensions-settings/hello.json' must be a JSON object.
```

If the manifest declares a `settingsSchema`, the host validates it when it loads the settings:

- every `required` name must be present;
- every declared top-level `properties.<name>.type` must match the value's JSON kind.

The failure refuses the load:

```text
extension 'hello': setting 'token' is required.
extension 'hello': setting 'port' must be integer.
```

Everything else in the schema — `default`, `enum`, `minimum`, `maximum`, nested `properties` — is
documentation, not enforcement. The host does not apply defaults; the extension code does, as the
samples do. Extra keys are allowed.

## Secrets

The secrets file is a JSON object whose values are strings:

```json
{ "router-key": "sk-…" }
```

`TryGet` returns false for an absent name or a non-string value. Invalid JSON refuses the load with
the same `secrets file '<path>' …` wording as settings. There is no write API: the user creates and
edits the file.

Secrets are not written to session files and `/extensions` never prints them. A manifest-declared
model provider names a secret in `secretName`; the host reads the value and passes it to the
client's request headers, so the value never has to reach extension code. In-process extensions are
fully trusted, so that is hygiene, not a security boundary.

## Example

`permission-gate` reads `mode` and falls back to `block` when it is absent or unknown:

```json
{ "mode": "confirm" }
```

```csharp
if (
    settings.TryGet("mode", out JsonElement value)
    && value.ValueKind == JsonValueKind.String
)
{
    string? mode = value.GetString();
    if (string.Equals(mode, "confirm", StringComparison.Ordinal))
    {
        // … proceed to the approval prompt
    }
}
```

Full file:
[`PermissionGateToolCallingHandler.cs`](../../../extensions/permission-gate/PermissionGateExtension/PermissionGateToolCallingHandler.cs).

A declared provider uses a secret by name, not by value:

```json
{
    "modelProviders": [
        {
            "id": "router-local",
            "displayName": "Router Local",
            "endpoint": "http://127.0.0.1:11434/v1",
            "secretName": "router-key",
            "modelIds": ["fast", "deep"],
            "contextWindow": 32768
        }
    ]
}
```

Full file:
[`extension.json`](../../../samples/extensions/model-router/ModelRouterExtension/extension.json).

## See also

- Schema enforcement in the manifest: [manifest.md](../manifest.md#settingsschema)
- Model providers and the secret read: [model-providers.md](model-providers.md)
- Testing with a temp store: [testing.md](../testing.md)

# The manifest

Every extension directory contains an `extension.json`. The host parses it during discovery,
before any extension code loads; a manifest that does not parse is reported and the extension is
not listed. The parser is `ExtensionManifest.Parse`; the same record drives discovery, loading and
the `/extensions` view.

Unknown fields are ignored at every level, so a manifest written for a later version still parses.
Known fields with the wrong type or shape fail the parse.

## Fields

| Field | Required | Type | Enforced by the host |
| --- | --- | --- | --- |
| `id` | yes | string | Yes. `[a-z0-9-]+`; the extension identity, the tool namespace, and the model-provider namespace (`ext/<id>/…`). |
| `version` | yes | string | No format check. Displayed by `/extensions`. |
| `apiVersion` | yes | string | Yes. `x.y.z` or `^x.y.z`; see [API version](#api-version). |
| `entryAssembly` | one of two | string | Yes. A file name inside the extension directory. |
| `runtime` | one of two | object | Yes. The out-of-process host; see [remote extensions](../extensions-remote.md). |
| `tools` | no | string array | Yes. Must list every registered tool's local name. |
| `commands` | no | string array | Parsed and displayed only. No command registration API exists; see [commands and keybindings](contributions/README.md#commands-and-keybindings). |
| `hooks` | no | string array | Declaration. `/extensions` compares registered count with declared count; in-process registrations are not cross-checked. Remote extensions enforce it at the handshake. |
| `services` | no | string array | Declaration, same as `hooks`. |
| `capabilities` | no | string array | No. Free-form strings; no surface renders them and nothing is gated by them. |
| `settingsSchema` | no | object | Partly. See [settings and secrets](contributions/settings-and-secrets.md). |
| `modelProviders` | no | object array | Yes. The descriptors are registered and instantiated lazily; see [model providers](contributions/model-providers.md). |

Exactly one of `entryAssembly` and `runtime` must be present. A missing pair and a double pair are
both refused with a message that names the conflict.

`tools`, `commands`, `hooks` and `services` must be arrays of strings and may not repeat a value.
`capabilities` may repeat values; it is informational.

### `id`

The id is lowercase letters, digits and hyphens. It names the extension everywhere: the tool
namespace (`<id>__<local name>`), model ids (`ext/<id>/…`) and the settings and secrets files
(`<id>.json`). The directory that contains the manifest may have any name.

Two discovered extensions may not share an id. Global scope is scanned before project scope, so the
global extension wins and the project duplicate is a discovery error naming both directories.

### `version`

Any non-empty string. The host does not parse it as SemVer and does not compare it; it is shown in
`/extensions` and in trust prompts. Use SemVer by convention.

### `entryAssembly`

The file name of the assembly to load, inside the extension directory. A rooted path or a path
containing `/` or `\` is refused: the assembly must sit directly inside the extension directory. The file must exist at load time. The assembly must expose exactly one public,
non-abstract class implementing `IExtensionFactory`, and that class needs a public parameterless
constructor.

### `runtime`

The out-of-process alternative to `entryAssembly`: `command`, `args`, optional `workingDirectory`,
`env`, `envPassthrough`, `callTimeout` and `startup`. The wire protocol, environment rules and
conformance runner are documented in [remote extensions](../extensions-remote.md).

## API version

`ExtensionApi.Current` is `1.10.0` in this version. The manifest may name:

| `apiVersion` | Loads on 1.10.0 | Meaning |
| --- | --- | --- |
| `"1.10.0"` | yes | Exactly this contract version. |
| `"^1.10.0"` | yes | This version or any later `1.x`. |
| `"^1.9.0"` | yes | Any `1.x` at or above `1.9.0`. |
| `"^2.0.0"` | no | A different major version is refused. |
| `"2.0.0"` | no | A different major version is refused. |

The grammar is exactly `x.y.z` or `^x.y.z`. A partial version (`1.0`), a comparison operator
(`>=1.0.0`) or a tilde range (`~1.0.0`) is a parse error:

```text
extension.json: field 'apiVersion': unsupported apiVersion range '>=1.0.0'
```

A syntactically valid but incompatible range parses and is then refused at discovery:

```text
extension 'future' requires apiVersion '2.0.0' but the current API version is 1.10.0.
```

The extension is not listed by `/extensions`; the message is in the session log. A new session is
needed after updating either side.

## `settingsSchema`

A JSON object. The host enforces only two things when it reads
`~/.lunate/extensions-settings/<id>.json`:

- every name in the schema's `required` array must be present in the settings file;
- every declared `properties.<name>.type` must match the value's JSON kind (`string`, `number`,
  `integer`, `boolean`, `object` or `array`; an unknown type name is not checked).

`default`, `enum`, `minimum`, `maximum` and nested schemas are not applied or validated. Defaults
live in the extension code. Extra settings keys are allowed. Every other rule is documentation for
the person editing the file.

A violation refuses the load with a message naming the setting:

```text
extension 'hello': setting 'token' is required.
extension 'hello': setting 'port' must be integer.
```

The path is always the user store, even for a project extension. There is no project-scoped
settings file.

## `modelProviders`

Each entry declares an OpenAI-compatible provider:

| Field | Required | Rule |
| --- | --- | --- |
| `id` | yes | `[a-z0-9-]+`; published as `ext/<extension-id>/<id>`. |
| `displayName` | yes | Non-empty string. |
| `endpoint` | yes | Absolute `http` or `https` URL. |
| `secretName` | yes | Non-empty name of an entry in the extension's secrets file. The host reads the value. |
| `modelIds` | yes | Non-empty array of non-empty strings; the ids the provider serves. |
| `contextWindow` | yes | Positive integer. |
| `supportsTools` | no | Boolean; default `true`. |

Provider ids must be unique within the manifest and across extensions. A served model id may not
duplicate another extension's model id or shadow a catalog model id. At most eight providers
(declared plus programmatic) are allowed per extension. Every rule is enforced at load or first
resolve; the messages are in [model providers](contributions/model-providers.md).

## What fails at which stage

| Stage | What happens | What the user sees |
| --- | --- | --- |
| Discovery | The manifest is missing, is not valid JSON, has a bad field or declares an incompatible `apiVersion`. | A log line; the extension is absent from `/extensions`. Other extensions are unaffected. |
| Load | The entry assembly is missing, a dependency is missing, the factory is missing or ambiguous, a registered tool is undeclared, a service or model id collides, or the settings file is invalid. | `failed: <first sentence>` in `/extensions`; the full message is in the log. |
| Registration | A contribution violates a cap or a name rule. | The extension fails to load; the message names the extension and the field. |

A discovery error is not a load error: there is no descriptor to load, so the extension never
appears as `failed`. For a duplicate id, the first directory is listed and the later one is left
out.

## Refusal messages

The manifest path starts each parse message, so a user can find the file. These are the messages
you are most likely to see:

```text
extension 'hello': entry assembly 'Missing.dll' was not found in '/…/.lunate/extensions/hello'.
extension 'hello': failed to load 'HelloExtension.dll': missing dependency 'HelloExtension.Support'. Place the dependency in '/…/.lunate/extensions/hello'.
extension 'hello': entry assembly 'e.dll' has 0 public IExtensionFactory implementations; declare exactly one public non-abstract type implementing IExtensionFactory.
extension 'hello': IExtensionFactory 'HelloExtensionFactory' needs a public parameterless constructor.
extension 'hello': tool 'greet' is registered by code but not declared in the manifest 'tools' list; the manifest must cover everything the code contributes. Declare it or remove the registration.
```

And the manifest-shape messages, in the form the parser produces (the prefix is the manifest path):

```text
extension.json: manifest is not valid JSON: …
extension.json: manifest must be a JSON object.
extension.json: field 'id' is required.
extension.json: field 'id' must match [a-z0-9-]+ but is 'Hello'.
extension.json: field 'version' must be a non-empty string.
extension.json: field 'entryAssembly' must be a file name inside the extension directory.
extension.json: the manifest declares neither 'entryAssembly' nor 'runtime'; declare exactly one runtime kind.
extension.json: the manifest declares both 'entryAssembly' and 'runtime'; declare exactly one runtime kind.
extension.json: field 'tools' must be an array of strings.
extension.json: field 'tools' declares 'greet' more than once.
extension.json: field 'settingsSchema' must be an object.
extension.json: field 'modelProviders[0].endpoint' must be an absolute http(s) URL but is 'ftp://x/v1'.
extension.json: field 'modelProviders[0].contextWindow' must be a positive integer.
extension.json: field 'modelProviders' declares id 'acme' more than once.
extension 'hello' is declared more than once: '/…/extensions/hello' and '/repo/.lunate/extensions/hello'.
extension directory '/…/extensions/hello' has no extension.json manifest.
```

A declared-but-unregistered tool is not a refusal: it is logged and dropped from the offered set.

```text
extension 'hello': manifest declares tool 'greet' but no registration matched; the entry is dropped from the offered set.
```

`/extensions` then shows the registered count and the declared count side by side, for example
`tools 0 (1 declared)`.

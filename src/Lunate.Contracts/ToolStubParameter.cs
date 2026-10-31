namespace Lunate.Agent;

/// <summary>
/// One typed parameter of a <see cref="ToolStub"/>, derived from the tool's JSON schema.
/// </summary>
/// <param name="Name">The property name as the schema declares it.</param>
/// <param name="Type">
/// The mapped type name: <c>string</c>, <c>number</c>, <c>integer</c>, <c>boolean</c>, <c>array</c>,
/// <c>object</c>, <c>enum</c> or <c>any</c>.
/// </param>
/// <param name="Required">True when the schema's <c>required</c> array lists the property.</param>
/// <param name="Description">The schema's description, when present.</param>
/// <param name="Default">The raw JSON text of the schema's default, when present; null otherwise.</param>
/// <param name="Enum">The enum values as strings, when the property declares an enum; null otherwise.</param>
/// <param name="Note">
/// Set when the mapping degraded (for example <c>unsupported schema construct: oneOf</c>); null for
/// a clean mapping.
/// </param>
public sealed record ToolStubParameter(
    string Name,
    string Type,
    bool Required,
    string? Description,
    string? Default,
    IReadOnlyList<string>? Enum,
    string? Note
);

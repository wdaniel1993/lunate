namespace Lunate.Roslyn;

/// <summary>
/// One declaration in a file outline: <see cref="Kind"/> uses the <see cref="SymbolMatch"/> kind
/// vocabulary, <see cref="Container"/> is the dotted namespace/type chain (null at the root),
/// <see cref="Signature"/> is the body-free declaration header and <see cref="Line"/> is 1-based.
/// </summary>
public sealed record OutlineItem(
    string Kind,
    string? Container,
    string Name,
    string Signature,
    int Line
);

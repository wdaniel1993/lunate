namespace Lunate.Roslyn;

/// <summary>
/// One definition site of a symbol: <see cref="File"/> is relative to the worktree root when under
/// it (absolute otherwise), <see cref="Line"/> and <see cref="Column"/> are 1-based.
/// </summary>
/// <param name="Kind">
/// Lowercase symbol kind: <c>class</c> | <c>struct</c> | <c>interface</c> | <c>enum</c> |
/// <c>delegate</c> | <c>method</c> | <c>property</c> | <c>field</c> | <c>event</c> |
/// <c>namespace</c> | <c>constructor</c>. Records report <c>class</c> and constructors
/// <c>constructor</c>; this mapping is pinned by tests.
/// </param>
/// <param name="Name">The simple (unqualified) symbol name.</param>
/// <param name="Container">The dotted namespace/type chain a symbol sits in; null at the root.</param>
/// <param name="File">The source file of the definition; null for metadata-only symbols.</param>
/// <param name="Line">The 1-based line of the declaration name; 0 without a source location.</param>
/// <param name="Column">The 1-based column of the declaration name; 0 without a source location.</param>
/// <param name="Signature">
/// The display string in <c>SymbolDisplayFormat.MinimallyQualifiedFormat</c>; the format is pinned
/// by snapshot tests so drift is visible.
/// </param>
/// <param name="FromMetadata">
/// True when the symbol has no source location (a referenced-assembly symbol, or a source
/// declaration without a file path).
/// </param>
public sealed record SymbolMatch(
    string Kind,
    string Name,
    string? Container,
    string? File,
    int Line,
    int Column,
    string Signature,
    bool FromMetadata
);

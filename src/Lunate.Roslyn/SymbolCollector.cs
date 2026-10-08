using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lunate.Roslyn;

/// <summary>The bounded outcome of a symbol search (see <see cref="SymbolCollector"/>).</summary>
internal sealed record CollectedSymbols(
    IReadOnlyList<SymbolMatch> Matches,
    int TotalMatchCount,
    bool Truncated,
    int MetadataMatchCount,
    string? CaseInsensitiveCandidate
);

/// <summary>
/// Resolves a name to definition sites across the loaded compilations: deterministic ordering,
/// bounded, source definitions first. <see cref="Compilation.GetSymbolsWithName(string, SymbolFilter, System.Threading.CancellationToken)"/>
/// covers source declarations; metadata-only types and namespaces come from walking the referenced
/// assemblies (see <see cref="MetadataSearch"/>), because Roslyn's search never returns metadata
/// symbols. Every project's compilation sees the same referenced assemblies, so metadata matches
/// are deduplicated globally (see <see cref="DeduplicateMetadata"/>). The flat match list is a
/// projection of <see cref="SymbolResolver"/>; the resolver keeps distinct symbols separate.
/// </summary>
internal static class SymbolCollector
{
    internal const int MaxMatches = 50;

    internal static readonly SymbolFilter Filter =
        SymbolFilter.Type | SymbolFilter.Member | SymbolFilter.Namespace;

    public static CollectedSymbols Collect(
        IEnumerable<Compilation> compilations,
        string query,
        string root,
        CancellationToken ct
    )
    {
        var resolved = SymbolResolver.Resolve(compilations, query, root, ct);

        var capped = new CappedList<SymbolMatch>(MaxMatches);
        foreach (var match in resolved.OrderedMatches)
        {
            capped.Add(match);
        }

        return new CollectedSymbols(
            capped.Items,
            capped.Total,
            capped.Truncated,
            resolved.OrderedMatches.Count(match => match.FromMetadata),
            capped.Total == 0 ? resolved.CaseInsensitiveCandidate : null
        );
    }

    /// <summary>
    /// The source declaration sites of a symbol (one per declaring syntax reference with a file
    /// path), ordered by file, line and column; empty when the symbol has no file-backed
    /// declaration.
    /// </summary>
    internal static List<SymbolMatch> SourceDeclarations(ISymbol symbol, string root)
    {
        var kind = KindOf(symbol);
        var container = ContainerOf(symbol);
        var signature = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        List<SymbolMatch> matches = [];

        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            var node = reference.GetSyntax();
            var filePath = node.SyntaxTree.FilePath;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            var position = NameLocation(node).GetLineSpan().StartLinePosition;
            matches.Add(
                new SymbolMatch(
                    kind,
                    symbol.Name,
                    container,
                    PathIdentity.RelativeOrAbsolute(root, filePath),
                    position.Line + 1,
                    position.Character + 1,
                    signature,
                    false
                )
            );
        }

        return matches;
    }

    /// <summary>
    /// Deduplicates metadata matches globally: every project's compilation sees the same
    /// referenced assemblies, so the same metadata symbol arrives once per project. The key is
    /// (assembly identity display name, fully-qualified dotted name, kind); candidates are ordered
    /// by that key (then signature) and the first of each key is kept, so the surviving set does
    /// not depend on project order. The deduplicated set counts toward the cap.
    /// </summary>
    internal static IEnumerable<SymbolMatch> DeduplicateMetadata(
        List<MetadataCandidate> metadata
    ) =>
        metadata
            .OrderBy(candidate => candidate.Identity, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.QualifiedName, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Match.Kind, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Match.Signature, StringComparer.Ordinal)
            .DistinctBy(candidate =>
                (candidate.Identity, candidate.QualifiedName, candidate.Match.Kind)
            )
            .Select(candidate => candidate.Match);

    internal static MetadataCandidate ToMetadataCandidate(
        IAssemblySymbol assembly,
        ISymbol symbol
    ) =>
        new(
            symbol.ContainingAssembly?.Identity.GetDisplayName()
                ?? assembly.Identity.GetDisplayName(),
            QualifiedName(symbol),
            ToMetadataMatch(symbol)
        );

    internal readonly record struct MetadataCandidate(
        string Identity,
        string QualifiedName,
        SymbolMatch Match
    );

    internal static SymbolMatch ToMetadataMatch(ISymbol symbol) =>
        new(
            KindOf(symbol),
            symbol.Name,
            ContainerOf(symbol),
            null,
            0,
            0,
            symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            true
        );

    internal static string? CaseInsensitiveCandidate(
        IEnumerable<Compilation> compilations,
        string query,
        string lastSegment,
        string? prefix,
        CancellationToken ct
    )
    {
        foreach (var compilation in compilations)
        {
            foreach (
                var symbol in compilation.GetSymbolsWithName(
                    candidate =>
                        string.Equals(candidate, lastSegment, StringComparison.OrdinalIgnoreCase),
                    Filter,
                    ct
                )
            )
            {
                if (
                    IsSearchable(symbol)
                    && MatchesPrefix(symbol, prefix, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return QualifiedName(symbol);
                }
            }

            foreach (
                var candidate in MetadataSearch.Candidates(
                    compilation,
                    query,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return QualifiedName(candidate.Symbol);
            }
        }

        return null;
    }

    internal static bool IsSearchable(ISymbol symbol) =>
        symbol
            is INamespaceSymbol
                or INamedTypeSymbol { TypeKind: not TypeKind.Error }
                or IMethodSymbol
                or IPropertySymbol
                or IFieldSymbol
                or IEventSymbol;

    internal static bool MatchesPrefix(ISymbol symbol, string? prefix, StringComparison comparison)
    {
        if (prefix is null)
        {
            return true;
        }

        var chain = ContainerOf(symbol);
        return chain is not null && string.Equals(chain, prefix, comparison);
    }

    private static string QualifiedName(ISymbol symbol) =>
        ContainerOf(symbol) is { } container ? $"{container}.{symbol.Name}" : symbol.Name;

    private static string? ContainerOf(ISymbol symbol)
    {
        var parts = new List<string>();
        for (var type = symbol.ContainingType; type is not null; type = type.ContainingType)
        {
            parts.Insert(0, type.Name);
        }

        for (
            var ns = symbol.ContainingNamespace;
            ns is { IsGlobalNamespace: false };
            ns = ns.ContainingNamespace
        )
        {
            parts.Insert(0, ns.Name);
        }

        return parts.Count == 0 ? null : string.Join('.', parts);
    }

    private static string KindOf(ISymbol symbol) =>
        symbol switch
        {
            INamespaceSymbol => "namespace",
            INamedTypeSymbol { TypeKind: TypeKind.Delegate } => "delegate",
            INamedTypeSymbol { TypeKind: TypeKind.Class } => "class",
            INamedTypeSymbol { TypeKind: TypeKind.Struct } => "struct",
            INamedTypeSymbol { TypeKind: TypeKind.Interface } => "interface",
            INamedTypeSymbol { TypeKind: TypeKind.Enum } => "enum",
            IMethodSymbol { MethodKind: MethodKind.Constructor } => "constructor",
            IMethodSymbol => "method",
            IPropertySymbol => "property",
            IFieldSymbol => "field",
            IEventSymbol => "event",
            _ => symbol.Kind.ToString().ToLowerInvariant(),
        };

    private static Location NameLocation(SyntaxNode node) =>
        node switch
        {
            BaseTypeDeclarationSyntax type => type.Identifier.GetLocation(),
            DelegateDeclarationSyntax declaration => declaration.Identifier.GetLocation(),
            MethodDeclarationSyntax method => method.Identifier.GetLocation(),
            ConstructorDeclarationSyntax constructor => constructor.Identifier.GetLocation(),
            DestructorDeclarationSyntax destructor => destructor.Identifier.GetLocation(),
            PropertyDeclarationSyntax property => property.Identifier.GetLocation(),
            EventDeclarationSyntax @event => @event.Identifier.GetLocation(),
            IndexerDeclarationSyntax indexer => indexer.ThisKeyword.GetLocation(),
            VariableDeclaratorSyntax variable => variable.Identifier.GetLocation(),
            NamespaceDeclarationSyntax declaration => declaration.Name.GetLocation(),
            FileScopedNamespaceDeclarationSyntax declaration => declaration.Name.GetLocation(),
            _ => node.GetLocation(),
        };
}

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
/// are deduplicated globally (see <see cref="DeduplicateMetadata"/>).
/// </summary>
internal static class SymbolCollector
{
    internal const int MaxMatches = 50;

    private static readonly SymbolFilter Filter =
        SymbolFilter.Type | SymbolFilter.Member | SymbolFilter.Namespace;

    public static CollectedSymbols Collect(
        IEnumerable<Compilation> compilations,
        string query,
        string root,
        CancellationToken ct
    )
    {
        var lastDot = query.LastIndexOf('.');
        var lastSegment = lastDot < 0 ? query : query[(lastDot + 1)..];
        var prefix = lastDot < 0 ? null : query[..lastDot];

        List<SymbolMatch> source = [];
        List<MetadataCandidate> metadata = [];
        foreach (var compilation in compilations)
        {
            foreach (var symbol in compilation.GetSymbolsWithName(lastSegment, Filter, ct))
            {
                if (IsSearchable(symbol) && MatchesPrefix(symbol, prefix, StringComparison.Ordinal))
                {
                    AddDeclarations(source, symbol, root);
                }
            }

            metadata.AddRange(
                MetadataSearch
                    .Candidates(compilation, query, StringComparison.Ordinal)
                    .Select(candidate => ToCandidate(candidate.Assembly, candidate.Symbol))
            );
        }

        var ordered = source
            .OrderBy(match => match.File, StringComparer.Ordinal)
            .ThenBy(match => match.Line)
            .ThenBy(match => match.Column)
            .ThenBy(match => match.Kind, StringComparer.Ordinal)
            .ThenBy(match => match.Name, StringComparer.Ordinal)
            .ThenBy(match => match.Signature, StringComparer.Ordinal)
            .Concat(
                DeduplicateMetadata(metadata)
                    .OrderBy(match => match.Container ?? string.Empty, StringComparer.Ordinal)
                    .ThenBy(match => match.Kind, StringComparer.Ordinal)
                    .ThenBy(match => match.Name, StringComparer.Ordinal)
                    .ThenBy(match => match.Signature, StringComparer.Ordinal)
            )
            .ToList();

        var capped = new CappedList<SymbolMatch>(MaxMatches);
        foreach (var match in ordered)
        {
            capped.Add(match);
        }

        return new CollectedSymbols(
            capped.Items,
            capped.Total,
            capped.Truncated,
            ordered.Count(match => match.FromMetadata),
            capped.Total == 0
                ? CaseInsensitiveCandidate(compilations, query, lastSegment, prefix, ct)
                : null
        );
    }

    /// <summary>
    /// Deduplicates metadata matches globally: every project's compilation sees the same
    /// referenced assemblies, so the same metadata symbol arrives once per project. The key is
    /// (assembly identity display name, fully-qualified dotted name, kind); candidates are ordered
    /// by that key (then signature) and the first of each key is kept, so the surviving set does
    /// not depend on project order. The deduplicated set counts toward the cap.
    /// </summary>
    private static IEnumerable<SymbolMatch> DeduplicateMetadata(List<MetadataCandidate> metadata) =>
        metadata
            .OrderBy(candidate => candidate.Identity, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.QualifiedName, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Match.Kind, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Match.Signature, StringComparer.Ordinal)
            .DistinctBy(candidate =>
                (candidate.Identity, candidate.QualifiedName, candidate.Match.Kind)
            )
            .Select(candidate => candidate.Match);

    private static MetadataCandidate ToCandidate(IAssemblySymbol assembly, ISymbol symbol) =>
        new(
            symbol.ContainingAssembly?.Identity.GetDisplayName()
                ?? assembly.Identity.GetDisplayName(),
            QualifiedName(symbol),
            ToMetadataMatch(symbol)
        );

    private readonly record struct MetadataCandidate(
        string Identity,
        string QualifiedName,
        SymbolMatch Match
    );

    private static void AddDeclarations(List<SymbolMatch> matches, ISymbol symbol, string root)
    {
        var kind = KindOf(symbol);
        var container = ContainerOf(symbol);
        var signature = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var anySource = false;

        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            var node = reference.GetSyntax();
            var filePath = node.SyntaxTree.FilePath;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            anySource = true;
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

        if (!anySource)
        {
            matches.Add(ToMetadataMatch(symbol));
        }
    }

    private static SymbolMatch ToMetadataMatch(ISymbol symbol) =>
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

    private static string? CaseInsensitiveCandidate(
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

    private static bool IsSearchable(ISymbol symbol) =>
        symbol
            is INamespaceSymbol
                or INamedTypeSymbol { TypeKind: not TypeKind.Error }
                or IMethodSymbol
                or IPropertySymbol
                or IFieldSymbol
                or IEventSymbol;

    private static bool MatchesPrefix(ISymbol symbol, string? prefix, StringComparison comparison)
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

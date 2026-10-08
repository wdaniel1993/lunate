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
/// assemblies, because Roslyn's search never returns metadata symbols.
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
        List<SymbolMatch> metadata = [];
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
                MetadataCandidates(compilation, lastSegment, StringComparison.Ordinal)
                    .Where(symbol => MatchesPrefix(symbol, prefix, StringComparison.Ordinal))
                    .Select(ToMetadataMatch)
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
                metadata
                    .OrderBy(match => match.Container ?? string.Empty, StringComparer.Ordinal)
                    .ThenBy(match => match.Kind, StringComparer.Ordinal)
                    .ThenBy(match => match.Name, StringComparer.Ordinal)
                    .ThenBy(match => match.Signature, StringComparer.Ordinal)
            );

        var capped = new CappedList<SymbolMatch>(MaxMatches);
        foreach (var match in ordered)
        {
            capped.Add(match);
        }

        return new CollectedSymbols(
            capped.Items,
            capped.Total,
            capped.Truncated,
            capped.Items.Count(match => match.FromMetadata),
            capped.Total == 0
                ? CaseInsensitiveCandidate(compilations, lastSegment, prefix, ct)
                : null
        );
    }

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

    private static IEnumerable<ISymbol> MetadataCandidates(
        Compilation compilation,
        string lastSegment,
        StringComparison comparison
    )
    {
        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
            {
                continue;
            }

            if (assembly.Locations.Any(location => location.Kind == LocationKind.SourceFile))
            {
                // A project reference: covered by the owning project's own source search.
                continue;
            }

            foreach (
                var symbol in SearchNamespace(assembly.GlobalNamespace, lastSegment, comparison)
            )
            {
                yield return symbol;
            }
        }
    }

    private static IEnumerable<ISymbol> SearchNamespace(
        INamespaceSymbol containing,
        string name,
        StringComparison comparison
    )
    {
        foreach (var child in containing.GetNamespaceMembers())
        {
            if (string.Equals(child.Name, name, comparison))
            {
                yield return child;
            }

            foreach (var nested in SearchNamespace(child, name, comparison))
            {
                yield return nested;
            }
        }

        var types =
            comparison == StringComparison.Ordinal
                ? containing.GetTypeMembers(name)
                : containing
                    .GetTypeMembers()
                    .Where(type => string.Equals(type.Name, name, comparison));

        foreach (var type in types)
        {
            yield return type;
        }
    }

    private static string? CaseInsensitiveCandidate(
        IEnumerable<Compilation> compilations,
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
                var symbol in MetadataCandidates(
                    compilation,
                    lastSegment,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                if (MatchesPrefix(symbol, prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return QualifiedName(symbol);
                }
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

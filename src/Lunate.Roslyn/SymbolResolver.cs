using Microsoft.CodeAnalysis;

namespace Lunate.Roslyn;

/// <summary>One matching source symbol with every declaration site found for it.</summary>
internal sealed record ResolvedSourceSymbol(
    ISymbol Symbol,
    IReadOnlyList<SymbolMatch> Declarations
);

/// <summary>
/// The full outcome of a T-26-style name resolution: the distinct matching source symbols, the
/// metadata matches, the ordered match list and the case-insensitive fallback candidate.
/// </summary>
internal sealed record ResolvedSymbols(
    IReadOnlyList<ResolvedSourceSymbol> Sources,
    IReadOnlyList<SymbolMatch> MetadataMatches,
    IReadOnlyList<SymbolMatch> OrderedMatches,
    string? CaseInsensitiveCandidate
);

/// <summary>
/// Resolves a name to its matching symbols across the loaded compilations with the exact rules of
/// <c>cs_find_symbol</c>: a simple name matches every symbol with that name, a dotted path keeps
/// only the matching container, and metadata matches are deduplicated globally. Unlike the flat
/// match list, the result keeps distinct symbols separate so callers can require a unique match.
/// </summary>
internal static class SymbolResolver
{
    public static ResolvedSymbols Resolve(
        IEnumerable<Compilation> compilations,
        string query,
        string root,
        CancellationToken ct
    )
    {
        var list = compilations as IReadOnlyCollection<Compilation> ?? compilations.ToList();
        var lastDot = query.LastIndexOf('.');
        var lastSegment = lastDot < 0 ? query : query[(lastDot + 1)..];
        var prefix = lastDot < 0 ? null : query[..lastDot];

        Dictionary<ISymbol, List<SymbolMatch>> bySymbol = new(SymbolEqualityComparer.Default);
        List<SymbolMatch> sourceMetadata = [];
        List<SymbolCollector.MetadataCandidate> metadata = [];
        foreach (var compilation in list)
        {
            foreach (
                var symbol in compilation.GetSymbolsWithName(
                    lastSegment,
                    SymbolCollector.Filter,
                    ct
                )
            )
            {
                if (
                    !SymbolCollector.IsSearchable(symbol)
                    || !SymbolCollector.MatchesPrefix(symbol, prefix, StringComparison.Ordinal)
                )
                {
                    continue;
                }

                var declarations = SymbolCollector.SourceDeclarations(symbol, root);
                if (declarations.Count > 0)
                {
                    if (bySymbol.TryGetValue(symbol, out var existing))
                    {
                        existing.AddRange(declarations);
                    }
                    else
                    {
                        bySymbol[symbol] = declarations;
                    }
                }
                else
                {
                    sourceMetadata.Add(SymbolCollector.ToMetadataMatch(symbol));
                }
            }

            foreach (
                var candidate in MetadataSearch.Candidates(
                    compilation,
                    query,
                    StringComparison.Ordinal
                )
            )
            {
                metadata.Add(
                    SymbolCollector.ToMetadataCandidate(candidate.Assembly, candidate.Symbol)
                );
            }
        }

        var sources = bySymbol
            .Select(pair => new ResolvedSourceSymbol(pair.Key, Order(pair.Value)))
            .OrderBy(source => source.Declarations[0].File, StringComparer.Ordinal)
            .ThenBy(source => source.Declarations[0].Line)
            .ThenBy(source => source.Declarations[0].Column)
            .ThenBy(source => source.Declarations[0].Kind, StringComparer.Ordinal)
            .ThenBy(source => source.Declarations[0].Name, StringComparer.Ordinal)
            .ThenBy(source => source.Declarations[0].Signature, StringComparer.Ordinal)
            .ToList();

        var deduplicatedMetadata = SymbolCollector
            .DeduplicateMetadata(metadata)
            .OrderBy(match => match.Container ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(match => match.Kind, StringComparer.Ordinal)
            .ThenBy(match => match.Name, StringComparer.Ordinal)
            .ThenBy(match => match.Signature, StringComparer.Ordinal)
            .ToList();

        var metadataMatches = sourceMetadata.Concat(deduplicatedMetadata).ToList();
        var ordered = Order(
                bySymbol.Values.SelectMany(declarations => declarations).Concat(sourceMetadata)
            )
            .Concat(deduplicatedMetadata)
            .ToList();

        return new ResolvedSymbols(
            sources,
            metadataMatches,
            ordered,
            ordered.Count == 0
                ? SymbolCollector.CaseInsensitiveCandidate(list, query, lastSegment, prefix, ct)
                : null
        );
    }

    private static IReadOnlyList<SymbolMatch> Order(IEnumerable<SymbolMatch> matches) =>
        matches
            .OrderBy(match => match.File, StringComparer.Ordinal)
            .ThenBy(match => match.Line)
            .ThenBy(match => match.Column)
            .ThenBy(match => match.Kind, StringComparer.Ordinal)
            .ThenBy(match => match.Name, StringComparer.Ordinal)
            .ThenBy(match => match.Signature, StringComparer.Ordinal)
            .ToList();
}

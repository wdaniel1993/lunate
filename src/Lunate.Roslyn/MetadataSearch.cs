using Microsoft.CodeAnalysis;

namespace Lunate.Roslyn;

/// <summary>
/// Finds metadata symbols for a query in the referenced assemblies of a compilation. A simple name
/// matches namespaces and top-level types anywhere in the namespace tree; a dotted query resolves
/// stepwise from the first segment (leading container segments may be omitted, so
/// <c>Environment.SpecialFolder</c> finds <c>System.Environment.SpecialFolder</c>) and looks the
/// final segment up on the resolved container, including nested types. The walk is bounded by name
/// lookups (<c>GetTypeMembers(name)</c>, <c>GetNamespaceMembers()</c>), never by enumerating type
/// members; nested types are therefore not found by a single-segment simple name.
/// </summary>
internal static class MetadataSearch
{
    /// <summary>
    /// Yields (referenced assembly, symbol) pairs for the query; project references are skipped
    /// because the owning project's source search covers them.
    /// </summary>
    public static IEnumerable<(IAssemblySymbol Assembly, ISymbol Symbol)> Candidates(
        Compilation compilation,
        string query,
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

            foreach (var symbol in Resolve(assembly.GlobalNamespace, query, comparison))
            {
                yield return (assembly, symbol);
            }
        }
    }

    private static IEnumerable<ISymbol> Resolve(
        INamespaceSymbol globalNamespace,
        string query,
        StringComparison comparison
    )
    {
        var segments = query.Split('.');
        if (segments.Length == 1)
        {
            return SimpleName(globalNamespace, segments[0], comparison);
        }

        var containers = SimpleName(globalNamespace, segments[0], comparison);
        for (var index = 1; index < segments.Length - 1; index++)
        {
            var segment = segments[index];
            containers = containers.SelectMany(container =>
                Children(container, segment, comparison)
            );
        }

        var name = segments[^1];
        return containers.SelectMany(container => Children(container, name, comparison));
    }

    private static IEnumerable<ISymbol> SimpleName(
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

            foreach (var nested in SimpleName(child, name, comparison))
            {
                yield return nested;
            }
        }

        foreach (var type in Types(containing, name, comparison))
        {
            yield return type;
        }
    }

    private static IEnumerable<ISymbol> Children(
        ISymbol container,
        string name,
        StringComparison comparison
    )
    {
        switch (container)
        {
            case INamespaceSymbol ns:
                foreach (var child in ns.GetNamespaceMembers())
                {
                    if (string.Equals(child.Name, name, comparison))
                    {
                        yield return child;
                    }
                }

                foreach (var type in Types(ns, name, comparison))
                {
                    yield return type;
                }

                break;
            case INamedTypeSymbol type:
                foreach (var nested in Types(type, name, comparison))
                {
                    yield return nested;
                }

                break;
        }
    }

    private static IEnumerable<INamedTypeSymbol> Types(
        INamespaceOrTypeSymbol container,
        string name,
        StringComparison comparison
    ) =>
        comparison == StringComparison.Ordinal
            ? container.GetTypeMembers(name)
            : container.GetTypeMembers().Where(type => string.Equals(type.Name, name, comparison));
}

namespace Lunate.Coding.Tests;

internal static class LayeringChecker
{
    public static IReadOnlySet<(string From, string To)> AllowedReferences { get; } =
        new HashSet<(string From, string To)>
        {
            ("Lunate.Agent", "Lunate.Ai"),
            ("Lunate.Extensibility", "Lunate.Agent"),
            ("Lunate.Extensibility", "Lunate.Extensibility.Abstractions"),
            ("Lunate.Extensibility.Testing", "Lunate.Agent"),
            ("Lunate.Extensibility.Testing", "Lunate.Ai"),
            ("Lunate.Extensibility.Testing", "Lunate.Extensibility"),
            ("Lunate.Extensibility.Testing", "Lunate.Extensibility.Abstractions"),
            ("Lunate.Protocols", "Lunate.Agent"),
            ("Lunate.Coding", "Lunate.Agent"),
            ("Lunate.Coding", "Lunate.Protocols"),
            ("Lunate.Coding", "Lunate.Tui"),
        };

    public static IReadOnlySet<string> AllowedAgentRuntimePackages { get; } =
        new HashSet<string> { "Microsoft.Extensions.AI.Abstractions" };

    public static IReadOnlyList<string> FindViolations(
        IEnumerable<(string From, string To)> references
    )
    {
        var violations = new List<string>();

        foreach (var reference in references)
        {
            if (!AllowedReferences.Contains(reference))
            {
                violations.Add($"{reference.From} -> {reference.To} is not an allowed reference");
            }
        }

        return violations;
    }

    public static IReadOnlyList<string> FindPackageViolations(
        IEnumerable<(string Package, bool IsBuildOnly)> packages
    )
    {
        var violations = new List<string>();

        foreach (var package in packages)
        {
            if (package.IsBuildOnly || AllowedAgentRuntimePackages.Contains(package.Package))
            {
                continue;
            }

            violations.Add($"{package.Package} is not an allowed runtime package for Lunate.Agent");
        }

        return violations;
    }
}

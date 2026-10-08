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
            ("Lunate.Roslyn", "Lunate.Agent"),
            ("Lunate.Roslyn.Tests", "Lunate.Roslyn"),
            ("Lunate.Roslyn.Tests", "Lunate.Agent"),
            ("Lunate.Coding", "Lunate.Agent"),
            ("Lunate.Coding", "Lunate.Protocols"),
            ("Lunate.Coding", "Lunate.Tui"),
            ("Lunate.Agent.Tests", "Lunate.Agent"),
            ("Lunate.Ai.Tests", "Lunate.Ai"),
            ("Lunate.Coding.Tests", "Lunate.Coding"),
            ("Lunate.Protocols.Tests", "Lunate.Protocols"),
            ("Lunate.Protocols.Tests", "TestMcpServer"),
            ("Lunate.Tui.Tests", "Lunate.Tui"),
            ("Lunate.Extensibility.Tests", "Lunate.Agent"),
            ("Lunate.Extensibility.Tests", "Lunate.Coding"),
            ("Lunate.Extensibility.Tests", "Lunate.Extensibility"),
            ("Lunate.Extensibility.Tests", "Lunate.Extensibility.Abstractions"),
            ("Lunate.Extensibility.Tests", "FakeLsp"),
            ("Lunate.Extensibility.Tests", "HelloExtension"),
            ("Lunate.Extensibility.Testing.Tests", "Lunate.Extensibility.Testing"),
            ("Lunate.Extensibility.Testing.Tests", "Lunate.Extensibility.Abstractions"),
            ("Lunate.Extensibility.Testing.Tests", "HelloExtension"),
            ("HelloExtension", "HelloExtension.Support"),
            ("HelloExtension", "Lunate.Extensibility.Abstractions"),
            ("HelloExtension.Support", "Lunate.Extensibility.Abstractions"),
            ("FakeLsp", "Lunate.Extensibility.Abstractions"),
            ("TemplateExtension", "Lunate.Extensibility.Abstractions"),
            ("TemplateExtension.Tests", "Lunate.Ai"),
            ("TemplateExtension.Tests", "Lunate.Extensibility.Abstractions"),
            ("TemplateExtension.Tests", "Lunate.Extensibility.Testing"),
            ("PermissionGateExtension", "Lunate.Extensibility.Abstractions"),
            ("PermissionGateExtension.Tests", "Lunate.Ai"),
            ("PermissionGateExtension.Tests", "Lunate.Extensibility.Abstractions"),
            ("PermissionGateExtension.Tests", "Lunate.Extensibility.Testing"),
            ("MemoryProviderExtension", "Lunate.Extensibility.Abstractions"),
            ("MemoryProviderExtension.Tests", "Lunate.Ai"),
            ("MemoryProviderExtension.Tests", "Lunate.Extensibility.Abstractions"),
            ("MemoryProviderExtension.Tests", "Lunate.Extensibility.Testing"),
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

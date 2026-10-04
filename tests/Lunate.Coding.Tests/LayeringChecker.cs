namespace Lunate.Coding.Tests;

internal static class LayeringChecker
{
    public static IReadOnlySet<(string From, string To)> AllowedReferences { get; } =
        new HashSet<(string From, string To)>
        {
            ("Lunate.Agent", "Lunate.Ai"),
            ("Lunate.Protocols", "Lunate.Agent"),
            ("Lunate.Coding", "Lunate.Agent"),
            ("Lunate.Coding", "Lunate.Protocols"),
            ("Lunate.Coding", "Lunate.Tui"),
        };

    public static IReadOnlyList<string> FindViolations(IEnumerable<(string From, string To)> references)
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
}

using System.Globalization;
using System.Xml.Linq;

namespace Lunate.Roslyn;

internal static class RestoreCheck
{
    private const int ListedProjects = 5;

    /// <summary>
    /// The project directories among <paramref name="projectPaths"/> whose restore output is
    /// missing or stale: <c>obj/project.assets.json</c> is absent, or a package reference a project
    /// declares is not covered by it (an assets file restored before the reference was added
    /// silently ignores the package). Deduplicated and in input order.
    /// </summary>
    public static IReadOnlyList<string> FindMissing(IEnumerable<string> projectPaths)
    {
        var missing = new List<string>();
        var seen = new HashSet<string>(PathIdentity.Comparer);

        foreach (var projectPath in projectPaths)
        {
            var directory = Path.GetDirectoryName(projectPath);
            if (directory is null || !seen.Add(directory))
            {
                continue;
            }

            var assets = Path.Combine(directory, "obj", "project.assets.json");
            if (!File.Exists(assets) || !CoversPackageReferences(projectPath, assets))
            {
                missing.Add(directory);
            }
        }

        return missing;
    }

    /// <summary>
    /// Whether the assets file covers every literal package reference the project declares; a
    /// project that cannot be read is covered (the workspace load reports its own failure), a
    /// dynamic id (MSBuild property syntax) cannot be checked and is left to the restore itself,
    /// and a conditioned reference (own or inherited <c>Condition</c> attribute) may legitimately
    /// be absent from the assets file (multi-targeting), so it is skipped.
    /// </summary>
    private static bool CoversPackageReferences(string projectPath, string assetsPath)
    {
        string[] ids;
        try
        {
            ids =
            [
                .. XDocument
                    .Load(projectPath)
                    .Descendants()
                    .Where(element => element.Name.LocalName == "PackageReference")
                    .Where(element => !IsConditioned(element))
                    .Select(element => element.Attribute("Include")?.Value)
                    .OfType<string>()
                    .Where(id => id.Length > 0 && !id.Contains('$'))
                    .Distinct(StringComparer.OrdinalIgnoreCase),
            ];
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException
            )
        {
            return true;
        }

        if (ids.Length == 0)
        {
            return true;
        }

        string assets;
        try
        {
            assets = File.ReadAllText(assetsPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }

        return ids.All(id => assets.Contains($"\"{id}/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether the element or any ancestor carries a <c>Condition</c> attribute.</summary>
    private static bool IsConditioned(XElement element) =>
        element.AncestorsAndSelf().Any(ancestor => ancestor.Attribute("Condition") is not null);

    /// <summary>The actionable restore message: the command, the first few project directories and the total.</summary>
    public static string BuildMessage(IReadOnlyList<string> missingDirectories)
    {
        if (missingDirectories.Count == 0)
        {
            return string.Empty;
        }

        var total = missingDirectories.Count;
        var noun = total == 1 ? "project" : "projects";
        var listed = string.Join(", ", missingDirectories.Take(ListedProjects));
        var message = string.Create(
            CultureInfo.InvariantCulture,
            $"restore required: {total} {noun} need `dotnet restore` (in {listed})"
        );

        if (total > ListedProjects)
        {
            message += string.Create(
                CultureInfo.InvariantCulture,
                $"; only the first {ListedProjects} are listed"
            );
        }

        return message;
    }
}

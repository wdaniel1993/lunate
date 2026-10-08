using System.Globalization;

namespace Lunate.Roslyn;

internal static class RestoreCheck
{
    private const int ListedProjects = 5;

    /// <summary>
    /// The project directories among <paramref name="projectPaths"/> whose
    /// <c>obj/project.assets.json</c> is missing, deduplicated and in input order.
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

            if (!File.Exists(Path.Combine(directory, "obj", "project.assets.json")))
            {
                missing.Add(directory);
            }
        }

        return missing;
    }

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

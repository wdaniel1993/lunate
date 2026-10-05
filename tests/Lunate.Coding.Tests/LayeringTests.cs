using System.Xml.Linq;

namespace Lunate.Coding.Tests;

public sealed class LayeringTests
{
    [Fact]
    public void Source_project_references_have_no_layering_violations()
    {
        var references = ReadSourceReferences();

        Assert.Empty(LayeringChecker.FindViolations(references));
    }

    [Fact]
    public void Source_project_reference_graph_matches_allowed_edges()
    {
        var references = ReadSourceReferences().ToHashSet();

        Assert.True(
            LayeringChecker.AllowedReferences.SetEquals(references),
            $"Actual edges: {string.Join(", ", references.Order())}"
        );
    }

    private static IReadOnlyList<(string From, string To)> ReadSourceReferences()
    {
        var references = new List<(string From, string To)>();
        var sourceDirectory = Path.Combine(FindRepositoryRoot(), "src");

        foreach (
            var projectFile in Directory.EnumerateFiles(
                sourceDirectory,
                "*.csproj",
                SearchOption.AllDirectories
            )
        )
        {
            var from = Path.GetFileNameWithoutExtension(projectFile);
            var document = XDocument.Load(projectFile);

            foreach (
                var include in document
                    .Descendants("ProjectReference")
                    .Select(element => element.Attribute("Include")?.Value)
            )
            {
                if (include is null)
                {
                    continue;
                }

                references.Add((from, Path.GetFileNameWithoutExtension(include)));
            }
        }

        return references;
    }

    private static string FindRepositoryRoot()
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (File.Exists(Path.Combine(directory.FullName, "lunate.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not find lunate.sln above {AppContext.BaseDirectory}."
        );
    }
}

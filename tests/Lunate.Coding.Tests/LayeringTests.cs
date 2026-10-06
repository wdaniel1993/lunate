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

    [Fact]
    public void Agent_runtime_packages_have_no_layering_violations()
    {
        var packages = ReadAgentPackages();

        Assert.Empty(LayeringChecker.FindPackageViolations(packages));
    }

    [Fact]
    public void Agent_runtime_package_set_matches_allowed_set()
    {
        var runtimePackages = ReadAgentPackages()
            .Where(package => !package.IsBuildOnly)
            .Select(package => package.Package)
            .ToHashSet();

        Assert.True(
            LayeringChecker.AllowedAgentRuntimePackages.SetEquals(runtimePackages),
            $"Actual packages: {string.Join(", ", runtimePackages.Order())}"
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

    private static IReadOnlyList<(string Package, bool IsBuildOnly)> ReadAgentPackages()
    {
        var projectFile = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Lunate.Agent",
            "Lunate.Agent.csproj"
        );
        var document = XDocument.Load(projectFile);

        return document
            .Descendants("PackageReference")
            .Select(element =>
                (
                    Package: element.Attribute("Include")?.Value,
                    PrivateAssets: element.Attribute("PrivateAssets")?.Value
                )
            )
            .Where(reference => reference.Package is not null)
            .Select(reference =>
                (
                    Package: reference.Package!,
                    IsBuildOnly: reference.PrivateAssets?.Contains(
                        "all",
                        StringComparison.OrdinalIgnoreCase
                    ) == true
                )
            )
            .ToList();
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

using System.Xml.Linq;
using Lunate.TestSupport;

namespace Lunate.Coding.Tests;

/// <summary>
/// The compile-only half of the layering rule: an extension that references a host assembly must not
/// copy it. <c>Private="false"</c> keeps the DLL out of the output directory and
/// <c>ExcludeAssets="runtime"</c> keeps it out of the deps file; both still compile against, and the
/// loader binds the extension to the host's copy.
/// </summary>
public sealed class LayeringExtensionReferencesTests
{
    [Fact]
    public void Extension_references_to_host_assemblies_are_compile_only()
    {
        var problems = new List<string>();

        foreach (string projectFile in EnumerateExtensionProjects())
        {
            foreach (var reference in XDocument.Load(projectFile).Descendants("ProjectReference"))
            {
                string? include = reference.Attribute("Include")?.Value;
                if (include is null)
                {
                    continue;
                }

                string target = Path.GetFileNameWithoutExtension(include);
                if (!LayeringChecker.ExtensionHostAssemblies.Contains(target))
                {
                    continue;
                }

                string from = Path.GetFileNameWithoutExtension(projectFile);
                if (
                    !string.Equals(
                        "false",
                        reference.Attribute("Private")?.Value,
                        StringComparison.Ordinal
                    )
                )
                {
                    problems.Add($"{from} copies {target}: Private must be false");
                }

                if (
                    !string.Equals(
                        "runtime",
                        reference.Attribute("ExcludeAssets")?.Value,
                        StringComparison.Ordinal
                    )
                )
                {
                    problems.Add($"{from} copies {target}: ExcludeAssets must be runtime");
                }

                if (reference.Attribute("PrivateAssets") is not null)
                {
                    problems.Add(
                        $"{from} -> {target} carries PrivateAssets, which also hides the host "
                            + "types an extension's signatures use"
                    );
                }
            }
        }

        Assert.Empty(problems);
    }

    private static IEnumerable<string> EnumerateExtensionProjects()
    {
        foreach (var directoryName in new[] { "templates", "samples", "tests" })
        {
            var projectRoot = Path.Combine(RepositoryRoot.Find(), directoryName);
            if (!Directory.Exists(projectRoot))
            {
                continue;
            }

            foreach (
                string projectFile in Directory.EnumerateFiles(
                    projectRoot,
                    "*.csproj",
                    SearchOption.AllDirectories
                )
            )
            {
                if (
                    projectFile.Contains(
                        $"{Path.DirectorySeparatorChar}fixtures{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                if (
                    LayeringChecker.IsExtensionProject(
                        Path.GetFileNameWithoutExtension(projectFile)
                    )
                )
                {
                    yield return projectFile;
                }
            }
        }
    }
}

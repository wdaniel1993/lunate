using System.Xml.Linq;

namespace Lunate.Coding.Tests;

/// <summary>
/// The template and the authoring page are the two places an extension author copies from, and the
/// rule they carry is not cosmetic: the host owns <c>Lunate.Extensibility.Abstractions</c>, so a
/// reference without <c>ExcludeAssets="runtime"</c> copies the DLL into the extension's output
/// (where a load would ignore it and warn) and one without <c>PrivateAssets="all"</c> hands the
/// contract to every consumer of the extension. A template that loses either attribute is a template
/// that teaches the wrong build.
/// </summary>
public sealed class ExtensionTemplateTests
{
    [Fact]
    public void The_template_marks_the_contract_reference_as_host_owned()
    {
        string path = Path.Combine(
            FindRepositoryRoot(),
            "templates",
            "extension",
            "TemplateExtension",
            "TemplateExtension.csproj"
        );
        Assert.True(File.Exists(path), $"the template project is missing: {path}");

        XElement reference = Assert.Single(
            XDocument.Load(path).Descendants("ProjectReference"),
            element =>
                ((string?)element.Attribute("Include"))?.Contains(
                    "Lunate.Extensibility.Abstractions",
                    StringComparison.Ordinal
                ) == true
        );

        Assert.True(
            string.Equals(
                "all",
                (string?)reference.Attribute("PrivateAssets"),
                StringComparison.Ordinal
            ),
            "the template's contract reference must carry PrivateAssets=\"all\": the host owns "
                + "Lunate.Extensibility.Abstractions, so it must not flow to the extension's consumers"
        );
        Assert.True(
            string.Equals(
                "runtime",
                (string?)reference.Attribute("ExcludeAssets"),
                StringComparison.Ordinal
            ),
            "the template's contract reference must carry ExcludeAssets=\"runtime\": the contract is "
                + "not a runtime asset of the extension, the host loads it"
        );
        Assert.True(
            string.Equals(
                "false",
                (string?)reference.Attribute("Private"),
                StringComparison.Ordinal
            ),
            "the template's contract reference must carry Private=\"false\": a copy in the output "
                + "directory is ignored at load, so shipping one is a warning for nothing"
        );
    }

    [Fact]
    public void The_authoring_page_shows_the_same_two_attributes()
    {
        string path = Path.Combine(FindRepositoryRoot(), "docs", "extensions", "README.md");
        string text = File.ReadAllText(path).ReplaceLineEndings("\n");

        Assert.Contains("PrivateAssets=\"all\"", text, StringComparison.Ordinal);
        Assert.Contains("ExcludeAssets=\"runtime\"", text, StringComparison.Ordinal);
    }

    /// <summary>The repository root, found from the test binary: the folder that holds the templates.</summary>
    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory, "templates", "extension")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new DirectoryNotFoundException(
            $"no templates/extension above '{AppContext.BaseDirectory}'"
        );
    }
}

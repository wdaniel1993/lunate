using System.Xml.Linq;

namespace Lunate.Coding.Tests;

/// <summary>
/// The template and the authoring page are the two places an extension author copies from, and the
/// rule they carry is not cosmetic: the host owns <c>Lunate.Extensibility.Abstractions</c>,
/// <c>Lunate.Agent</c> and <c>Lunate.Ai</c>, so a reference without <c>Private="false"</c> drops the
/// DLL into the extension's output (where a load would ignore it and warn) and one without
/// <c>ExcludeAssets="runtime"</c> keeps it in the deps file as a runtime asset. A template that loses
/// either attribute is a template that teaches the wrong build — and one that adds
/// <c>PrivateAssets="all"</c> teaches a worse one: it hides the Agent types an extension's signatures
/// use, so the extension stops compiling.
/// </summary>
public sealed class ExtensionTemplateTests
{
    private static readonly string[] HostOwned =
    [
        "Lunate.Extensibility.Abstractions",
        "Lunate.Agent",
        "Lunate.Ai",
    ];

    [Fact]
    public void The_template_references_each_host_assembly_compile_only()
    {
        string path = Path.Combine(
            FindRepositoryRoot(),
            "templates",
            "extension",
            "TemplateExtension",
            "TemplateExtension.csproj"
        );
        Assert.True(File.Exists(path), $"the template project is missing: {path}");

        XElement[] references = [.. XDocument.Load(path).Descendants("ProjectReference")];
        foreach (string assembly in HostOwned)
        {
            XElement reference = Assert.Single(
                references,
                element =>
                    ((string?)element.Attribute("Include"))?.Contains(
                        assembly,
                        StringComparison.Ordinal
                    ) == true
            );

            Assert.True(
                string.Equals(
                    "false",
                    (string?)reference.Attribute("Private"),
                    StringComparison.Ordinal
                ),
                $"the template's {assembly} reference must carry Private=\"false\": a copy in the "
                    + "output directory is ignored at load, so shipping one is a warning for nothing"
            );
            Assert.True(
                string.Equals(
                    "runtime",
                    (string?)reference.Attribute("ExcludeAssets"),
                    StringComparison.Ordinal
                ),
                $"the template's {assembly} reference must carry ExcludeAssets=\"runtime\": the host "
                    + "loads it, so it is not a runtime asset of the extension"
            );
            Assert.Null(reference.Attribute("PrivateAssets"));
        }
    }

    [Fact]
    public void The_authoring_page_shows_the_same_shape()
    {
        string path = Path.Combine(FindRepositoryRoot(), "docs", "extensions", "README.md");
        string text = File.ReadAllText(path).ReplaceLineEndings("\n");

        Assert.Contains("Private=\"false\"", text, StringComparison.Ordinal);
        Assert.Contains("ExcludeAssets=\"runtime\"", text, StringComparison.Ordinal);
        foreach (string assembly in HostOwned)
        {
            Assert.Contains(assembly, text, StringComparison.Ordinal);
        }

        Assert.Contains("ignored at load", text, StringComparison.Ordinal);
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

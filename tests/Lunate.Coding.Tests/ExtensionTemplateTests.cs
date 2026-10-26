using System.Xml.Linq;
using Lunate.TestSupport;

namespace Lunate.Coding.Tests;

/// <summary>
/// The template is the place an extension author copies from, and the rule it carries is not
/// cosmetic: the host owns <c>Lunate.Extensibility.Abstractions</c>, <c>Lunate.Agent</c> and
/// <c>Lunate.Ai</c>, so a reference without <c>Private="false"</c> drops the DLL into the
/// extension's output (where a load would ignore it and warn) and one without
/// <c>ExcludeAssets="runtime"</c> keeps it in the deps file as a runtime asset. A template that loses
/// either attribute is a template that teaches the wrong build — and one that adds
/// <c>PrivateAssets="all"</c> teaches a worse one: it hides the Agent types an extension's signatures
/// use, so the extension stops compiling.
/// </summary>
/// <remarks>
/// The authoring page shows this file's references through <c>scripts/docs-sync</c>
/// (<c>&lt;!-- snippet: … --&gt;</c>), so the page cannot teach a shape the template does not have and
/// this test has one subject: the template itself.
/// </remarks>
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
        string path = TemplateProject();
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

    /// <summary>
    /// Every reference resolves to a project that exists. The template lives outside the solution,
    /// so nothing else builds it: a moved or renamed project would otherwise leave a copy-pasted
    /// template failing to restore, with the page showing the same broken paths.
    /// </summary>
    [Fact]
    public void Every_template_reference_resolves_to_a_project()
    {
        string path = TemplateProject();
        string directory = Path.GetDirectoryName(path)!;
        XElement[] references = [.. XDocument.Load(path).Descendants("ProjectReference")];
        Assert.NotEmpty(references);

        foreach (XElement reference in references)
        {
            string include = (string?)reference.Attribute("Include") ?? string.Empty;
            string target = Path.GetFullPath(
                Path.Combine(
                    directory,
                    include
                        .Replace('/', Path.DirectorySeparatorChar)
                        .Replace('\\', Path.DirectorySeparatorChar)
                )
            );

            Assert.True(
                File.Exists(target),
                $"the template references '{include}', which resolves to '{target}' and does not exist"
            );
        }
    }

    /// <summary>
    /// The template must be compiled by something, or it rots exactly where it is most expensive:
    /// the file every extension author copies. No test builds it (that would restore and write build
    /// output from a test process), so the solution does — <c>scripts/verify.sh</c> builds
    /// <c>lunate.slnx</c> on every leg — and this test keeps both projects listed, because removing
    /// them is a silent edit that leaves every other test green.
    /// </summary>
    [Fact]
    public void The_solution_lists_the_template_so_the_gate_compiles_it()
    {
        XDocument solution = XDocument.Load(Path.Combine(RepositoryRoot.Find(), "lunate.slnx"));
        string[] projects =
        [
            .. solution
                .Descendants("Project")
                .Select(element => (string?)element.Attribute("Path"))
                .OfType<string>(),
        ];

        Assert.Contains(
            "templates/extension/TemplateExtension/TemplateExtension.csproj",
            projects,
            StringComparer.Ordinal
        );
        Assert.Contains(
            "templates/extension/TemplateExtension.Tests/TemplateExtension.Tests.csproj",
            projects,
            StringComparer.Ordinal
        );
    }

    private static string TemplateProject()
    {
        string path = Path.Combine(
            RepositoryRoot.Find(),
            "templates",
            "extension",
            "TemplateExtension",
            "TemplateExtension.csproj"
        );
        Assert.True(File.Exists(path), $"the template project is missing: {path}");
        return path;
    }
}

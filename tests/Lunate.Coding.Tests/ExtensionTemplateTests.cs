using System.Xml.Linq;
using Lunate.TestSupport;

namespace Lunate.Coding.Tests;

/// <summary>
/// The template is the place an extension author copies from, and the rule it carries is not
/// cosmetic: the host owns <c>Lunate.Contracts</c> and
/// <c>Lunate.Extensibility.Abstractions</c>, so references use <c>PrivateAssets="all"</c>,
/// <c>Private="false"</c> and <c>ExcludeAssets="runtime"</c> to keep the host assemblies out of the
/// extension's runtime assets while allowing it to compile against them.
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
        "Lunate.Contracts",
        "Lunate.Extensibility.Abstractions",
    ];

    /// <summary>
    /// The stable contract version §the template pins when it is outside this repository: the
    /// packages are versioned with <see cref="ExtensionApi"/> (Lunate.Extensibility.Tests pins the
    /// csprojs to the same number), so a bump cannot leave the template behind.
    /// </summary>
    [Fact]
    public void The_template_pins_the_stable_contract_version()
    {
        string path = TemplateProject();
        foreach (string assembly in HostOwned)
        {
            XElement reference = Assert.Single(
                XDocument.Load(path).Descendants("PackageReference"),
                element =>
                    ((string?)element.Attribute("Include"))?.Contains(
                        assembly,
                        StringComparison.Ordinal
                    ) == true
            );
            Assert.Equal(
                Lunate.Extensibility.Abstractions.ExtensionApi.Current,
                (string?)reference.Attribute("Version")
            );
        }
    }

    [Fact]
    public void The_template_references_each_host_assembly_compile_only()
    {
        string path = TemplateProject();
        XDocument document = XDocument.Load(path);
        // The package shape is the packaged-extension story; the project shape keeps this
        // repository building before the packages are on the machine's feed. Both must carry the
        // host-owned attribute set or their own shape's equivalents.
        foreach (string assembly in HostOwned)
        {
            XElement package = Assert.Single(
                document.Descendants("PackageReference"),
                element =>
                    ((string?)element.Attribute("Include"))?.Contains(
                        assembly,
                        StringComparison.Ordinal
                    ) == true
            );
            Assert.True(
                string.Equals(
                    "all",
                    (string?)package.Attribute("PrivateAssets"),
                    StringComparison.Ordinal
                ),
                $"the template's {assembly} package reference must carry PrivateAssets=\"all\": the "
                    + "references must not flow to consumers of a packaged extension"
            );
            Assert.True(
                string.Equals(
                    "runtime",
                    (string?)package.Attribute("ExcludeAssets"),
                    StringComparison.Ordinal
                ),
                $"the template's {assembly} package reference must carry ExcludeAssets=\"runtime\": "
                    + "the host loads it, so it is not a runtime asset of the extension"
            );

            XElement project = Assert.Single(
                document.Descendants("ProjectReference"),
                element =>
                    ((string?)element.Attribute("Include"))?.Contains(
                        assembly,
                        StringComparison.Ordinal
                    ) == true
            );
            Assert.True(
                string.Equals(
                    "false",
                    (string?)project.Attribute("Private"),
                    StringComparison.Ordinal
                ),
                $"the template's {assembly} reference must carry Private=\"false\": a copy in the "
                    + "output directory is ignored at load, so shipping one is a warning for nothing"
            );
            Assert.True(
                string.Equals(
                    "runtime",
                    (string?)project.Attribute("ExcludeAssets"),
                    StringComparison.Ordinal
                ),
                $"the template's {assembly} reference must carry ExcludeAssets=\"runtime\": the host "
                    + "loads it, so it is not a runtime asset of the extension"
            );
            Assert.Equal("all", (string?)project.Attribute("PrivateAssets"));
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

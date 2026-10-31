using System.Xml.Linq;
using Lunate.Extensibility.Abstractions;
using Lunate.TestSupport;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// The packaging contract of #237: the two stable contract packages and the two preview packages
/// have to keep the shape the release publishes — stable versions pinned to
/// <see cref="ExtensionApi.Current"/>, the preview pair promising no stability below the READMEs,
/// dependencies only on published packages (a nuspec that names an unpublished package installs
/// nowhere), and the contract assemblies riding the dependency chain instead of a package's own
/// <c>lib</c>.
/// </summary>
/// <remarks>
/// This is the fast half of the proof; the packed nupkg itself is proven by the verify gate's
/// package roundtrip, which ports <c>scripts/check-package-metadata.sh</c> across the real
/// packages no test builds.
/// </remarks>
public sealed class ExtensionContractPackages
{
    private const string PreviewVersion = "0.2.0-preview.1";

    private const string StabilityPromise = "no API stability promise before 1.0";

    /// <summary>The contract assemblies a shipped preview package must never carry in its own lib.</summary>
    private static readonly string[] PublishedPackageAssemblies =
    [
        "Lunate.Contracts.dll",
        "Lunate.Extensibility.Abstractions.dll",
        "Lunate.Agent.dll",
    ];

    [Fact]
    public void The_stable_packages_version_with_the_api()
    {
        string[] stable = ["Lunate.Contracts", "Lunate.Extensibility.Abstractions"];
        foreach (string id in stable)
        {
            Assert.Equal(ExtensionApi.Current, VersionOf(id));
        }
    }

    [Fact]
    public void The_preview_pair_says_what_it_is()
    {
        string[] preview = ["Lunate.Agent", "Lunate.Extensibility.Testing"];
        foreach (string id in preview)
        {
            string version = VersionOf(id);
            Assert.StartsWith("0.2.0-preview.", version, StringComparison.Ordinal);
            Assert.EndsWith(".1", version, StringComparison.Ordinal);
            Assert.Equal(PreviewVersion, version);
            Assert.Contains(
                StabilityPromise,
                Metadata(id, "Description"),
                StringComparison.OrdinalIgnoreCase
            );
            Assert.Contains(
                StabilityPromise,
                PackageReadmeOf(id),
                StringComparison.OrdinalIgnoreCase
            );
        }
    }

    [Fact]
    public void Every_packable_carries_the_page_metadata()
    {
        foreach (string id in AllIds)
        {
            Assert.Equal("MIT", Metadata(id, "PackageLicenseExpression"));
            Assert.Equal("README.md", Metadata(id, "PackageReadmeFile"));
            Assert.Equal("lunate-logo-dark.png", Metadata(id, "PackageIcon"));
            Assert.Equal("https://github.com/wdaniel1993/lunate", Metadata(id, "PackageProjectUrl"));
            Assert.True(File.Exists(ReadmePath(id)), $"{id} packs a README.md that is missing.");
            Assert.True(
                File.Exists(IconPath()),
                "docs/media/brand/lunate-logo-dark.png is missing: every package packs it as its icon."
            );
        }
    }

    /// <summary>
    /// NuGet names every project reference as a package dependency. A dependency on a package this
    /// repository does not publish installs nowhere — so the unpublished edges are private, and the
    /// assemblies they point at are named into the package's lib by the pack target instead.
    /// </summary>
    [Fact]
    public void Dependencies_name_only_published_packages()
    {
        XDocument agent = Load("Lunate.Agent");
        XElement ai = SingleReference(agent, "ProjectReference", "Lunate.Ai");
        Assert.Equal("All", (string?)ai.Attribute("PrivateAssets"));

        XDocument testing = Load("Lunate.Extensibility.Testing");
        string[] unpublishedEdges = ["Lunate.Ai", "Lunate.Coding", "Lunate.Extensibility.Remote"];
        foreach (string unpublished in unpublishedEdges)
        {
            XElement reference = SingleReference(testing, "ProjectReference", unpublished);
            Assert.Equal("All", (string?)reference.Attribute("PrivateAssets"));
        }

        // The four unpublished edges are private; the two published ones (the kit's declared
        // dependencies) must not be.
        foreach (XElement reference in testing.Descendants("ProjectReference"))
        {
            string include = (string?)reference.Attribute("Include") ?? string.Empty;
            bool published =
                include.Contains("Lunate.Agent.csproj", StringComparison.Ordinal)
                || include.Contains("Lunate.Extensibility.Abstractions.csproj", StringComparison.Ordinal);
            Assert.True(
                published || string.Equals("All", (string?)reference.Attribute("PrivateAssets")),
                $"a project reference to '{include}' has no PrivateAssets: pack would name a "
                    + "dependency on an unpublished package"
            );
            Assert.True(
                !published || reference.Attribute("PrivateAssets") is null,
                $"the published project reference '{include}' carries PrivateAssets: the package "
                    + "dependency it stands for would then be missing"
            );
        }
    }

    /// <summary>
    /// The lib inventory is an explicit name list: the target exists, carries the assemblies the
    /// package needs, and never carries a contract assembly that rides the dependency chain —
    /// a second copy would blur which copy types bind to at load.
    /// </summary>
    [Theory]
    [InlineData("Lunate.Agent", new[] { "Lunate.Ai.dll" })]
    [InlineData(
        "Lunate.Extensibility.Testing",
        new[]
        {
            "Lunate.Ai.dll",
            "Lunate.Extensibility.dll",
            "Lunate.Extensibility.Remote.dll",
            "lunate.dll",
            "Lunate.Protocols.dll",
            "Lunate.Tui.dll",
            "Lunate.Extensibility.Compile.dll",
            "Lunate.Extensibility.Restore.dll",
            "Lunate.Roslyn.BuiltIn.dll",
            "Lunate.Roslyn.dll",
        }
    )]
    public void The_lib_inventory_is_explicit_and_contract_free(string id, string[] expected)
    {
        XDocument document = Load(id);
        XElement target = Assert.Single(
            document.Descendants("Target"),
            element => (string?)element.Attribute("Name") == "PackOwnAssemblies"
        );

        string include = Assert.Single(target.Descendants("TfmSpecificPackageFile")).Attribute("Include")!.Value!;
        foreach (string file in expected)
        {
            Assert.Contains(file, include, StringComparison.Ordinal);
        }

        foreach (string published in PublishedPackageAssemblies)
        {
            Assert.DoesNotContain(published, include, StringComparison.Ordinal);
        }
    }

    private static readonly string[] AllIds =
    [
        "Lunate.Contracts",
        "Lunate.Extensibility.Abstractions",
        "Lunate.Agent",
        "Lunate.Extensibility.Testing",
    ];

    private static string VersionOf(string id) => Load(id).Root!.Element("PropertyGroup")!.Element("Version")!.Value!;

    private static string Metadata(string id, string elementName)
    {
        XElement? element = Load(id)
            .Root!
            .Element("PropertyGroup")!
            .Element(elementName);
        Assert.NotNull(element);
        return element.Value.Trim();
    }

    private static string PackageReadmeOf(string id) =>
        File.ReadAllText(Path.Combine(ProjectDirectory(id), "README.md"));

    private static string ReadmePath(string id) => Path.Combine(ProjectDirectory(id), "README.md");

    private static string IconPath() =>
        Path.Combine(RepositoryRoot.Find(), "docs", "media", "brand", "lunate-logo-dark.png");

    private static XElement SingleReference(XDocument document, string name, string includeMatch) =>
        Assert.Single(
            document.Descendants(name),
            element => ((string?)element.Attribute("Include"))?.Contains(
                includeMatch,
                StringComparison.Ordinal
            ) == true
        );

    private static XDocument Load(string id) => XDocument.Load(Path.Combine(ProjectDirectory(id), $"{id}.csproj"));

    private static string ProjectDirectory(string id) =>
        Path.Combine(RepositoryRoot.Find(), "src", id);
}

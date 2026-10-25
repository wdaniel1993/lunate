namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// The package manifest (spec: "A package's layout", design D2): schema 1, one folder per content
/// kind in the order it is declared, unknown fields ignored, and every refusal naming the field or
/// the kind a package author has to fix.
/// </summary>
public sealed class PackageManifestTests
{
    [Fact]
    public void A_nupkg_parses_into_a_model_that_names_both_folders_in_declared_order()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest("""{"schema":1,"skills":"skills","extension":"extension"}""")
            .Extension()
            .Skill("greek")
            .Save();

        PackageInfo info = PackageReader.Read(nupkg);

        Assert.Equal(["skills", "extension"], [.. info.Manifest.Content.Select(item => item.Kind)]);
        Assert.Equal("skills", info.Manifest.Skills);
        Assert.Equal("extension", info.Manifest.Extension);
        Assert.Equal("acme-tools", info.Extension!.Manifest.Id);
        Assert.Equal(["greek"], [.. info.Skills.Select(skill => skill.Name)]);
    }

    [Fact]
    public void A_manifest_names_its_content_folders_in_the_order_it_declares_them()
    {
        PackageManifest manifest = PackageManifest.Parse(
            """{"schema":1,"skills":"content/skills","extension":"extension","future":{"x":1}}""",
            "lunate.package.json"
        );

        Assert.Equal(1, manifest.Schema);
        Assert.Equal("extension", manifest.Extension);
        Assert.Equal("content/skills", manifest.Skills);
        Assert.Equal(["skills", "extension"], [.. manifest.Content.Select(item => item.Kind)]);
        Assert.Equal(
            ["content/skills", "extension"],
            [.. manifest.Content.Select(item => item.Directory)]
        );
    }

    [Fact]
    public void A_known_field_of_the_wrong_type_is_refused_naming_the_field()
    {
        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            PackageManifest.Parse("""{"schema":1,"extension":42}""", "lunate.package.json")
        );

        Assert.Contains("'extension'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("string", failure.Message, StringComparison.Ordinal);
        Assert.Contains("lunate.package.json", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"schema":"1","extension":"extension"}""", "schema")]
    [InlineData("""{"schema":2,"extension":"extension"}""", "schema")]
    public void A_schema_this_version_does_not_read_is_refused_naming_the_field(
        string json,
        string field
    )
    {
        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            PackageManifest.Parse(json, "lunate.package.json")
        );

        Assert.Contains($"'{field}'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("1", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_nupkg_without_the_package_manifest_is_refused_naming_the_package()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root).Extension().Save();

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            PackageReader.Read(nupkg)
        );

        Assert.Contains("Acme.Toolkit", failure.Message, StringComparison.Ordinal);
        Assert.Contains("1.4.2", failure.Message, StringComparison.Ordinal);
        Assert.Contains("lunate.package.json", failure.Message, StringComparison.Ordinal);
        Assert.Contains("extension", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_manifest_with_no_content_kind_is_refused_naming_both()
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root).Manifest("""{"schema":1}""").Save();

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            PackageReader.Read(nupkg)
        );

        Assert.Contains("'extension'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("'skills'", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("templates")]
    [InlineData("themes")]
    public void A_kind_this_version_does_not_install_is_refused_by_name(string kind)
    {
        using var temp = new TempDirectory();
        string nupkg = new TestPackage(temp.Root)
            .Manifest($$"""{"schema":1,"extension":"extension","{{kind}}":"content"}""")
            .Extension()
            .Save();

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            PackageReader.Read(nupkg)
        );

        Assert.Contains($"'{kind}'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("does not install", failure.Message, StringComparison.Ordinal);
    }
}

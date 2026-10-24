namespace Lunate.Extensibility.Tests;

/// <summary>
/// The accepted grammar, pinned by tests taken from the documented .NET 10 file-based-app
/// syntax: <c>#:package &lt;id&gt;@&lt;version or range&gt;</c> and
/// <c>#:extension &lt;id&gt;@&lt;exact version&gt;</c>.
/// </summary>
public sealed class ExtensionHeaderGrammarTests
{
    [Theory]
    [InlineData("Newtonsoft.Json@13.0.3", "Newtonsoft.Json", "13.0.3")]
    [InlineData("Octokit@[14.0.0,15.0.0)", "Octokit", "[14.0.0,15.0.0)")]
    [InlineData("Humanizer@[2.14.1]", "Humanizer", "[2.14.1]")]
    [InlineData("Polly@[8.0.0,)", "Polly", "[8.0.0,)")]
    [InlineData("Serilog@(3.0.0,4.0.0]", "Serilog", "(3.0.0,4.0.0]")]
    [InlineData("Refit@[8.0.0, 9.0.0)", "Refit", "[8.0.0, 9.0.0)")]
    [InlineData(
        "System.Text.Json@9.0.0-preview.1.24080.9",
        "System.Text.Json",
        "9.0.0-preview.1.24080.9"
    )]
    public void A_package_accepts_a_version_or_a_range(
        string argument,
        string expectedId,
        string expectedRange
    )
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(
            $"#:extension acme-tools@1.0.0\n#:package {argument}\n\npublic sealed class Acme;\n"
        );

        Assert.Empty(header.Errors);
        ExtensionPackageReference package = Assert.Single(header.Packages);
        Assert.Equal(expectedId, package.Id);
        Assert.Equal(expectedRange, package.VersionRange);
        Assert.Equal(argument, package.Written);
    }

    [Theory]
    [InlineData("Newtonsoft.Json")]
    [InlineData("Newtonsoft.Json@")]
    [InlineData("@13.0.3")]
    [InlineData("Newtonsoft.Json@1.*")]
    [InlineData("Newtonsoft.Json@[13.0.0")]
    [InlineData("Newtonsoft.Json@[13.0.0,14.0.0,15.0.0]")]
    [InlineData("Newtonsoft.Json@latest")]
    public void A_package_without_a_well_formed_version_is_refused(string argument)
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(
            $"#:extension acme-tools@1.0.0\n#:package {argument}\n\npublic sealed class Acme;\n"
        );

        ExtensionHeaderIssue error = Assert.Single(header.Errors);
        Assert.Equal(2, error.Line);
        Assert.Contains("#:package", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("acme-tools@1.0.0")]
    [InlineData("acme@1.0")]
    [InlineData("acme@1.0.0-beta.1+build.5")]
    [InlineData("acme-2@10.20.30.40")]
    public void The_extension_directive_accepts_one_exact_version(string argument)
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(
            $"#:extension {argument}\n\npublic sealed class Acme;\n"
        );

        Assert.Empty(header.Errors);
        Assert.True(header.IsValid);
    }

    [Theory]
    [InlineData("acme-tools")]
    [InlineData("acme-tools@")]
    [InlineData("@1.0.0")]
    [InlineData("acme-tools@[1.0.0,2.0.0)")]
    [InlineData("acme-tools@1.*")]
    [InlineData("acme-tools@1")]
    [InlineData("Acme.Tools@1.0.0")]
    [InlineData("acme.tools@1.0.0")]
    [InlineData("acme:tools@1.0.0")]
    public void The_extension_directive_refuses_another_shape(string argument)
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(
            $"#:extension {argument}\n\npublic sealed class Acme;\n"
        );

        Assert.False(header.IsValid);
        Assert.Null(header.Id);
        ExtensionHeaderIssue error = Assert.Single(header.Errors);
        Assert.Contains("#:extension", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_provider_id_must_match_the_manifest_id_rule()
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(
            "#:extension acme-tools@1.0.0\n#:provider Acme-Models\n\npublic sealed class Acme;\n"
        );

        ExtensionHeaderIssue error = Assert.Single(header.Errors);
        Assert.Contains("#:provider", error.Message, StringComparison.Ordinal);
        Assert.Empty(header.Providers);
    }
}

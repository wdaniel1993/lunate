namespace Lunate.Extensibility.Tests;

/// <summary>
/// The declaration rule for a single file (spec: "Header directives declare what the file
/// provides"): a registration the header does not declare refuses the load, naming the
/// registration and the missing directive; a declared name nothing registered is a warning shown as
/// <c>declared, not provided</c>.
/// </summary>
public sealed class ExtensionDeclarationCheckTests
{
    [Fact]
    public void A_registered_tool_the_header_does_not_declare_is_refused_naming_both()
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(
            "#:extension acme-tools@1.0.0\n#:tool fetch_issue\n"
        );

        ExtensionDeclarationCheckResult result = ExtensionDeclarationCheck.Check(
            header,
            [
                new ExtensionDeclaration(ExtensionDeclarationKind.Tool, "fetch_issue"),
                new(ExtensionDeclarationKind.Tool, "close_issue"),
            ]
        );

        string refusal = Assert.Single(result.Refusals);
        Assert.Contains("close_issue", refusal, StringComparison.Ordinal);
        Assert.Contains("#:tool close_issue", refusal, StringComparison.Ordinal);
        Assert.Empty(result.DeclaredNotProvided);
    }

    [Fact]
    public void A_declared_name_nothing_registered_is_a_warning_not_a_refusal()
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(
            "#:extension acme-tools@1.0.0\n#:tool fetch_issue\n"
        );

        ExtensionDeclarationCheckResult result = ExtensionDeclarationCheck.Check(header, []);

        Assert.Empty(result.Refusals);
        ExtensionDeclaration missing = Assert.Single(result.DeclaredNotProvided);
        Assert.Equal(ExtensionDeclarationKind.Tool, missing.Kind);
        Assert.Equal("fetch_issue", missing.Name);
    }

    [Theory]
    [InlineData(ExtensionDeclarationKind.Tool, "a-name")]
    [InlineData(ExtensionDeclarationKind.Command, "a-name")]
    [InlineData(ExtensionDeclarationKind.Hook, "a-name")]
    [InlineData(ExtensionDeclarationKind.Service, "a-name")]
    [InlineData(ExtensionDeclarationKind.Capability, "a-name")]
    [InlineData(ExtensionDeclarationKind.Provider, "a-name")]
    public void Every_declaration_kind_is_cross_checked(ExtensionDeclarationKind kind, string name)
    {
        ExtensionHeader declared = ExtensionHeaderScanner.Scan(
            "#:extension acme-tools@1.0.0\n"
                + ExtensionDeclarationCheck.DirectiveFor(kind)
                + " "
                + name
                + "\n"
        );
        ExtensionHeader empty = ExtensionHeaderScanner.Scan("#:extension acme-tools@1.0.0\n");

        Assert.Empty(ExtensionDeclarationCheck.Check(declared, [new(kind, name)]).Refusals);
        string refusal = Assert.Single(
            ExtensionDeclarationCheck.Check(empty, [new(kind, name)]).Refusals
        );
        Assert.Contains(ExtensionDeclarationCheck.DirectiveFor(kind) + " " + name, refusal);
    }

    [Fact]
    public void Matching_declarations_and_registrations_produce_no_findings()
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(
            """
            #:extension acme-tools@1.0.0
            #:tool fetch_issue
            #:command ping
            #:hook tool-calling
            #:service greeter
            #:capability net
            #:provider acme-models
            """
        );

        ExtensionDeclarationCheckResult result = ExtensionDeclarationCheck.Check(
            header,
            [
                new(ExtensionDeclarationKind.Tool, "fetch_issue"),
                new(ExtensionDeclarationKind.Command, "ping"),
                new(ExtensionDeclarationKind.Hook, "tool-calling"),
                new(ExtensionDeclarationKind.Service, "greeter"),
                new(ExtensionDeclarationKind.Capability, "net"),
                new(ExtensionDeclarationKind.Provider, "acme-models"),
            ]
        );

        Assert.Empty(result.Refusals);
        Assert.Empty(result.DeclaredNotProvided);
    }
}

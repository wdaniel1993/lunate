namespace Lunate.Extensibility.Tests;

/// <summary>
/// The header scanner (spec: "Directive grammar and position"): the leading block only, a
/// <c>#:</c> line after code is an error naming its line, <c>#:sdk</c>/<c>#:property</c>/
/// <c>#:project</c> are refused by name, and an unknown directive warns instead of refusing.
/// </summary>
public sealed class ExtensionHeaderScannerTests
{
    [Fact]
    public void Reads_the_extension_and_every_declaration_kind()
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(
            """
            #:extension acme-tools@1.2.3
            #:tool fetch_issue
            #:tool close_issue
            #:command ping
            #:hook tool-calling
            #:service greeter
            #:capability net
            #:provider acme-models
            #:verifier csharp *.cs *.csx
            #:package Octokit@14.0.0
            #:generators on

            public sealed class Acme;
            """
        );

        Assert.Empty(header.Errors);
        Assert.Empty(header.Warnings);
        Assert.Equal("acme-tools", header.Id);
        Assert.Equal("1.2.3", header.Version);
        Assert.Equal(["fetch_issue", "close_issue"], header.Tools);
        Assert.Equal(["ping"], header.Commands);
        Assert.Equal(["tool-calling"], header.Hooks);
        Assert.Equal(["greeter"], header.Services);
        Assert.Equal(["net"], header.Capabilities);
        Assert.Equal(["acme-models"], header.Providers);
        Abstractions.WriteVerifierDeclaration verifier = Assert.Single(header.WriteVerifiers);
        Assert.Equal("csharp", verifier.Name);
        Assert.Equal(["*.cs", "*.csx"], verifier.Patterns);
        Assert.Equal("Octokit@14.0.0", Assert.Single(header.Packages).Written);
        Assert.True(header.Generators);
        Assert.True(header.IsValid);
    }

    [Fact]
    public void A_header_of_only_comments_and_blank_lines_is_an_empty_header()
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(
            """
            // nothing declared yet

            /* not even this */
            """
        );

        Assert.Empty(header.Errors);
        Assert.Empty(header.Warnings);
        Assert.Null(header.Id);
        Assert.False(header.IsValid);
    }

    [Fact]
    public void Comments_blank_lines_and_block_comments_before_the_directives_keep_them_leading()
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(
            """
            // acme tools
            /* a licence header
               that spans lines */
            #:extension acme-tools@1.0.0

            #:tool fetch_issue // trailing note

            public sealed class Acme;
            """
        );

        Assert.Empty(header.Errors);
        Assert.Equal("acme-tools", header.Id);
        Assert.Equal(["fetch_issue"], header.Tools);
    }

    [Fact]
    public void A_directive_inside_a_block_comment_is_not_a_directive()
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(
            """
            /*
            #:sdk Microsoft.NET.Sdk
            #:project ../other/other.csproj
            */
            #:extension acme-tools@1.0.0

            public sealed class Acme;
            """
        );

        Assert.Empty(header.Errors);
        Assert.Equal("acme-tools", header.Id);
    }

    [Fact]
    public void A_directive_after_the_first_token_is_an_error_naming_its_line()
    {
        string[] lines =
        [
            "#:extension late-tool@1.0.0",
            string.Empty,
            "public static class Late",
            "{",
            "}",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            "#:tool late",
        ];

        ExtensionHeader header = ExtensionHeaderScanner.Scan(string.Join('\n', lines));

        ExtensionHeaderIssue error = Assert.Single(header.Errors);
        Assert.Equal(12, error.Line);
        Assert.Contains("#:tool", error.Message, StringComparison.Ordinal);
        Assert.StartsWith("line 12:", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_directive_after_code_inside_a_block_comment_is_not_an_error()
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(
            """
            #:extension acme-tools@1.0.0
            public sealed class Acme;
            /*
            #:tool late
            */
            """
        );

        Assert.Empty(header.Errors);
    }

    [Theory]
    [InlineData("sdk Microsoft.NET.Sdk", "#:sdk")]
    [InlineData("property LangVersion=preview", "#:property")]
    [InlineData("project ../other/other.csproj", "#:project")]
    public void Sdk_property_and_project_are_refused_naming_the_directive(
        string line,
        string directive
    )
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(
            $"#:extension acme-tools@1.0.0\n#:{line}\n\npublic sealed class Acme;\n"
        );

        ExtensionHeaderIssue error = Assert.Single(header.Errors);
        Assert.Contains(directive, error.Message, StringComparison.Ordinal);
        Assert.False(header.IsValid);
    }

    [Fact]
    public void An_unknown_directive_in_the_leading_block_warns_and_is_ignored()
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(
            """
            #:extension acme-tools@1.0.0
            #:whatever acme
            #:tool fetch_issue

            public sealed class Acme;
            """
        );

        Assert.Empty(header.Errors);
        Assert.Equal("acme-tools", header.Id);
        Assert.Equal(["fetch_issue"], header.Tools);
        ExtensionHeaderIssue warning = Assert.Single(header.Warnings);
        Assert.Equal(2, warning.Line);
        Assert.Contains("#:whatever", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_extension_directive_is_refused()
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(
            """
            #:extension acme-tools@1.0.0
            #:extension other-tools@2.0.0

            public sealed class Acme;
            """
        );

        ExtensionHeaderIssue error = Assert.Single(header.Errors);
        Assert.Contains("#:extension", error.Message, StringComparison.Ordinal);
        Assert.Equal("acme-tools", header.Id);
    }

    [Fact]
    public void A_duplicate_declaration_is_refused_like_the_manifest_refuses_it()
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(
            """
            #:extension acme-tools@1.0.0
            #:tool fetch_issue
            #:tool fetch_issue

            public sealed class Acme;
            """
        );

        ExtensionHeaderIssue error = Assert.Single(header.Errors);
        Assert.Equal(3, error.Line);
        Assert.Contains("fetch_issue", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("#:verifier csharp", "#:verifier")]
    [InlineData("#:verifier csharp nested/file.cs", "path separator")]
    [InlineData("#:verifier csharp *.cs *.CS", "duplicated")]
    public void A_verifier_directive_refuses_what_the_manifest_refuses(string line, string expected)
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(
            $"#:extension acme-tools@1.0.0\n{line}\n\npublic sealed class Acme;\n"
        );

        ExtensionHeaderIssue error = Assert.Single(header.Errors);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_duplicate_verifier_name_is_refused()
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(
            """
            #:extension acme-tools@1.0.0
            #:verifier csharp *.cs
            #:verifier csharp *.csx

            public sealed class Acme;
            """
        );

        ExtensionHeaderIssue error = Assert.Single(header.Errors);
        Assert.Equal(3, error.Line);
        Assert.Contains("csharp", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_directive_takes_exactly_one_name()
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(
            """
            #:extension acme-tools@1.0.0
            #:tool fetch issue

            public sealed class Acme;
            """
        );

        ExtensionHeaderIssue error = Assert.Single(header.Errors);
        Assert.Contains("#:tool", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generators_takes_on_and_refuses_another_value()
    {
        Assert.True(
            ExtensionHeaderScanner.Scan("#:extension acme@1.0.0\n#:generators on\n").Generators
        );

        ExtensionHeader refused = ExtensionHeaderScanner.Scan(
            "#:extension acme@1.0.0\n#:generators off\n"
        );
        ExtensionHeaderIssue error = Assert.Single(refused.Errors);
        Assert.Contains("#:generators", error.Message, StringComparison.Ordinal);
        Assert.Contains("on", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_crlf_header_scans_the_same_as_lf()
    {
        string[] lines =
        [
            "#:extension acme-tools@1.0.0",
            "#:tool fetch_issue",
            string.Empty,
            "public sealed class Acme;",
            "#:tool late",
        ];

        ExtensionHeader header = ExtensionHeaderScanner.Scan(string.Join("\r\n", lines));

        Assert.Equal("acme-tools", header.Id);
        Assert.Equal(["fetch_issue"], header.Tools);
        ExtensionHeaderIssue error = Assert.Single(header.Errors);
        Assert.Equal(5, error.Line);
    }

    [Fact]
    public void A_tool_name_outside_the_provider_grammar_is_refused()
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(
            "#:extension acme-tools@1.0.0\n#:tool fetch.issue\n\npublic sealed class Acme;\n"
        );

        ExtensionHeaderIssue error = Assert.Single(header.Errors);
        Assert.Equal(2, error.Line);
        Assert.Contains("fetch.issue", error.Message, StringComparison.Ordinal);
        Assert.Contains("[A-Za-z0-9_-]", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_longest_fitting_tool_name_is_accepted_and_the_next_character_is_refused()
    {
        // "<id>__<tool>" must fit 64 characters: a 40-character id leaves 22 for the tool name.
        string id = new('a', 40);
        string fits = new('b', 22);
        string overflows = new('b', 23);

        ExtensionHeader accepted = ExtensionHeaderScanner.Scan(
            $"#:extension {id}@1.0.0\n#:tool {fits}\n\npublic sealed class Acme;\n"
        );
        Assert.Empty(accepted.Errors);
        Assert.Equal([fits], accepted.Tools);

        ExtensionHeader refused = ExtensionHeaderScanner.Scan(
            $"#:extension {id}@1.0.0\n#:tool {overflows}\n\npublic sealed class Acme;\n"
        );
        ExtensionHeaderIssue error = Assert.Single(refused.Errors);
        Assert.Equal(2, error.Line);
        Assert.Contains($"{id}__{overflows}", error.Message, StringComparison.Ordinal);
        Assert.Contains("64", error.Message, StringComparison.Ordinal);
        Assert.Contains(id, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Directives_are_case_sensitive()
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(
            "#:Extension acme@1.0.0\n\npublic sealed class Acme;\n"
        );

        Assert.Null(header.Id);
        Assert.Empty(header.Errors);
        Assert.Contains(
            "#:Extension",
            Assert.Single(header.Warnings).Message,
            StringComparison.Ordinal
        );
    }
}

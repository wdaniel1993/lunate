using Lunate.Extensibility;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// Task 2.14: <c>#:generators on</c> is a decision, so toggling it has to be visible twice — the
/// trust digest changes (the header is part of the bytes the user approved), and the question grows
/// the line that says compiling runs build-time code from those packages. Both halves are asserted
/// from the file's own text, through the same scanner and the same formatter the prompt uses, so the
/// pair cannot drift apart: a directive that stops being hashed or a line that stops being asked
/// fails here.
/// </summary>
public sealed class SingleFileGeneratorsOptInTests
{
    private const string Declared = """
        #:extension acme-tools@1.0.0
        #:tool fetch_issue
        #:package Refit@8.0.0

        public sealed class Acme
        {
        }
        """;

    private const string OptedIn = """
        #:extension acme-tools@1.0.0
        #:tool fetch_issue
        #:package Refit@8.0.0
        #:generators on

        public sealed class Acme
        {
        }
        """;

    [Fact]
    public void Toggling_the_directive_changes_the_trust_digest()
    {
        ExtensionHeader declared = ExtensionHeaderScanner.Scan(Declared);
        ExtensionHeader optedIn = ExtensionHeaderScanner.Scan(OptedIn);

        Assert.True(declared.IsValid, "the fixture's header must be valid");
        Assert.True(optedIn.IsValid, "the fixture's header must be valid");
        Assert.False(declared.Generators);
        Assert.True(optedIn.Generators);

        // The first-trust form and the source-level form a lock records both move: the header line is
        // in the bytes, and that is what makes toggling it a decision rather than a setting.
        Assert.NotEqual(Digest(declared, Declared), Digest(optedIn, OptedIn));
        Assert.NotEqual(
            SingleFileExtensionDigest.ComputeSource(
                optedIn.Id!,
                optedIn.Packages,
                System.Text.Encoding.UTF8.GetBytes(Declared)
            ),
            SingleFileExtensionDigest.ComputeSource(
                optedIn.Id!,
                optedIn.Packages,
                System.Text.Encoding.UTF8.GetBytes(OptedIn)
            )
        );
    }

    [Fact]
    public void The_prompt_states_build_time_code_only_for_an_opted_in_file()
    {
        string declared = string.Join(
            "\n",
            SingleFileTrustQuestion.Details(Question(ExtensionHeaderScanner.Scan(Declared)))
        );
        string optedIn = string.Join(
            "\n",
            SingleFileTrustQuestion.Details(Question(ExtensionHeaderScanner.Scan(OptedIn)))
        );

        Assert.DoesNotContain("build-time code", declared, StringComparison.Ordinal);
        Assert.Contains("build-time code", optedIn, StringComparison.Ordinal);

        // The packages and the dependency caveat are on both: the opt-in adds the line, it does not
        // replace the rest of the question.
        Assert.Contains("Refit@8.0.0", declared, StringComparison.Ordinal);
        Assert.Contains("and their dependencies", declared, StringComparison.Ordinal);
        Assert.Contains("Refit@8.0.0", optedIn, StringComparison.Ordinal);
    }

    /// <summary>
    /// The question the product builds for a scanned header: the file, the declarations as written
    /// and the opt-in. Kept in one place so a test cannot pass on a request the product never makes.
    /// </summary>
    private static SingleFileTrustRequest Question(ExtensionHeader header) =>
        new()
        {
            Path = "/home/you/.lunate/extensions/acme.cs",
            Id = header.Id!,
            Version = header.Version!,
            Packages = [.. header.Packages.Select(package => package.Written)],
            Generators = header.Generators,
        };

    private static string Digest(ExtensionHeader header, string source) =>
        SingleFileExtensionDigest.ComputeUnrestored(
            header.Id!,
            header.Packages,
            System.Text.Encoding.UTF8.GetBytes(source)
        );
}

using System.Text;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// <c>extension.lock.json</c> as a document: two halves with two jobs. The writer is canonical, so
/// the same resolution always produces the same bytes; the graph's bytes are the digest's input and
/// the toolset's block answers for itself, so a hand edit to either half is caught by the half that
/// owns it — one re-asks the user, the other only rebuilds.
/// </summary>
public sealed class ExtensionLockJsonTests
{
    [Fact]
    public void A_written_lock_is_parsed_written_and_written_again_byte_identically()
    {
        ExtensionLock @lock = Locked();

        string first = ExtensionLockJson.Write(@lock);
        ExtensionLock parsed = ExtensionLockJson.TryParse(first, out string? error)!;
        string second = ExtensionLockJson.Write(parsed);

        Assert.Null(error);
        Assert.Equal(first, second);
        Assert.Equal(ExtensionLockJson.GraphBytes(@lock), ExtensionLockJson.GraphBytes(parsed));
    }

    [Fact]
    public void Packages_and_dependencies_are_ordered_in_the_written_lock()
    {
        ExtensionLock @lock = Locked() with
        {
            Packages =
            [
                Package(
                    "Octokit",
                    "14.0.0",
                    dependencies: [("Zeta", "[1.0.0, )"), ("Acme", "[2.0.0, )")]
                ),
                Package("Acme.Lib", "1.9.0"),
                Package("Acme.Lib", "1.10.0"),
            ],
        };

        string text = ExtensionLockJson.Write(@lock);

        int firstAcme = text.IndexOf("1.10.0", StringComparison.Ordinal);
        int secondAcme = text.IndexOf("1.9.0", StringComparison.Ordinal);
        int octokit = text.IndexOf("\"Octokit\"", StringComparison.Ordinal);
        Assert.True(firstAcme < secondAcme, "versions must be ordered ordinally: " + text);
        Assert.True(secondAcme < octokit, "ids must be ordered case-insensitively: " + text);
        Assert.True(
            text.IndexOf("\"Acme\"", StringComparison.Ordinal)
                < text.IndexOf("\"Zeta\"", StringComparison.Ordinal),
            "dependency ids must be ordered: " + text
        );
    }

    [Fact]
    public void The_toolset_half_is_the_only_part_of_the_document_the_graph_bytes_leave_out()
    {
        ExtensionLock @lock = Locked();
        string text = ExtensionLockJson.Write(@lock);
        string graph = System.Text.Encoding.UTF8.GetString(ExtensionLockJson.GraphBytes(@lock));

        Assert.Contains("\"graph\":", text, StringComparison.Ordinal);
        Assert.Contains("\"toolset\":", text, StringComparison.Ordinal);
        Assert.True(
            text.IndexOf("\"graph\":", StringComparison.Ordinal)
                < text.IndexOf("\"toolset\":", StringComparison.Ordinal),
            "the graph is written before the toolset"
        );
        Assert.Contains("\"graph\":", graph, StringComparison.Ordinal);
        Assert.DoesNotContain("\"toolset\":", graph, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hand_edited_toolset_block_is_refused_and_an_unknown_version_is_refused()
    {
        ExtensionLock @lock = Locked();
        string text = ExtensionLockJson.Write(@lock);
        string tampered = text.Replace(
            "\"runtime\": \"10.0.13\"",
            "\"runtime\": \"10.0.14\"",
            StringComparison.Ordinal
        );
        Assert.NotEqual(text, tampered);

        Assert.Null(ExtensionLockJson.TryParse(tampered, out string? toolset));
        Assert.Contains("toolset", toolset!, StringComparison.OrdinalIgnoreCase);

        Assert.Null(
            ExtensionLockJson.TryParse(
                text.Replace("\"lockVersion\": 2", "\"lockVersion\": 1", StringComparison.Ordinal),
                out string? version
            )
        );
        Assert.Contains("lockVersion", version!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_lock_bytes_carry_no_carriage_return_and_rewriting_them_is_byte_identical()
    {
        ExtensionLock @lock = Locked();

        byte[] first = ExtensionLockJson.ToBytes(@lock);

        Assert.DoesNotContain((byte)'\r', first);
        Assert.DoesNotContain((byte)'\r', ExtensionLockJson.GraphBytes(@lock));
        ExtensionLock parsed = ExtensionLockJson.TryParse(Encoding.UTF8.GetString(first), out _)!;
        Assert.Equal(first, ExtensionLockJson.ToBytes(parsed));
    }

    [Fact]
    public void The_whole_lock_hash_covers_both_halves_and_the_digest_input_covers_one()
    {
        ExtensionLock before = Locked();
        ExtensionLock patched = before with
        {
            Toolset = before.Toolset with
            {
                Runtime = "10.0.14",
                ReferencePack = before.Toolset.ReferencePack with { Version = "10.0.14" },
            },
        };

        Assert.NotEqual(
            ExtensionLockJson.Hash(ExtensionLockJson.ToBytes(before)),
            ExtensionLockJson.Hash(ExtensionLockJson.ToBytes(patched))
        );
        Assert.Equal(ExtensionLockJson.GraphBytes(before), ExtensionLockJson.GraphBytes(patched));
    }

    /// <summary>A whole lock; the tests vary one half of it at a time.</summary>
    internal static ExtensionLock Locked() =>
        new()
        {
            ExtensionId = "acme-tools",
            ExtensionVersion = "1.0.0",
            SourceDigest = "source-digest",
            PackagesWritten = ["Octokit@14.0.0"],
            Toolset = new ExtensionLockToolset
            {
                Tfm = "net10.0",
                Rid = "osx-arm64",
                Runtime = "10.0.13",
                ReferencePack = new ExtensionLockReferencePack
                {
                    Id = "Microsoft.NETCore.App.Ref",
                    Version = "10.0.13",
                    Rule = ReferencePackRules.Exact,
                    Sha512 = "cmVmLXBhY2s=",
                    Assets = ["ref/net10.0/System.Runtime.dll"],
                },
            },
            Sources = ["nuget.org"],
            Packages = [Package("Octokit", "14.0.0")],
        };

    private static ExtensionLockPackage Package(
        string id,
        string version,
        (string Id, string Range)[]? dependencies = null
    ) =>
        new()
        {
            Id = id,
            Version = version,
            Sha512 = "cGFja2FnZQ==",
            Requested = FormattableString.Invariant($"{id}@{version}"),
            Direct = true,
            Dependencies = dependencies is null
                ? []
                :
                [
                    .. dependencies.Select(entry => new ExtensionLockDependency(
                        entry.Id,
                        entry.Range
                    )),
                ],
            Files = [FormattableString.Invariant($"lib/net10.0/{id}.dll")],
            Compile = [FormattableString.Invariant($"lib/net10.0/{id}.dll")],
            Runtime = [FormattableString.Invariant($"lib/net10.0/{id}.dll")],
        };
}

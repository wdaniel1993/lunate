using System.Text;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// Digests over content the user owns (repo-quality spec: "digests over user-owned content
/// normalize line endings"): one source, two checkouts — the same text with CRLF and with LF — has
/// one digest, because the line ending is the checkout's, not the author's.
/// </summary>
public sealed class LineEndingDigestTests
{
    private const string Source =
        "#:extension acme-tools@1.0.0\n#:package Octokit@14.0.0\nreturn 1;\n";

    [Fact]
    public void The_single_file_source_digest_is_the_same_for_CRLF_and_LF()
    {
        ExtensionPackageReference octokit = new("Octokit", "14.0.0", "Octokit@14.0.0");

        string lf = SingleFileExtensionDigest.ComputeUnrestored(
            "acme-tools",
            [octokit],
            Encoding.UTF8.GetBytes(Source)
        );
        string crlf = SingleFileExtensionDigest.ComputeUnrestored(
            "acme-tools",
            [octokit],
            Encoding.UTF8.GetBytes(Source.ReplaceLineEndings("\r\n"))
        );

        Assert.Equal(lf, crlf);
        Assert.Equal(
            SingleFileExtensionDigest.ComputeSource(
                "acme-tools",
                [octokit],
                Encoding.UTF8.GetBytes(Source)
            ),
            SingleFileExtensionDigest.ComputeSource(
                "acme-tools",
                [octokit],
                Encoding.UTF8.GetBytes(Source.ReplaceLineEndings("\r\n"))
            )
        );
        // The source-only tag is a different layout from the graph-bound one, and stays so.
        Assert.NotEqual(
            lf,
            SingleFileExtensionDigest.ComputeSource(
                "acme-tools",
                [octokit],
                Encoding.UTF8.GetBytes(Source)
            )
        );
    }

    [Fact]
    public void The_graph_bound_digest_is_the_same_for_CRLF_and_LF()
    {
        ExtensionPackageReference octokit = new("Octokit", "14.0.0", "Octokit@14.0.0");
        byte[] graph = Encoding.UTF8.GetBytes("{\n  \"graph\": true\n}\n");

        string lf = SingleFileExtensionDigest.Compute(
            "acme-tools",
            [octokit],
            Encoding.UTF8.GetBytes(Source),
            graph
        );
        string crlf = SingleFileExtensionDigest.Compute(
            "acme-tools",
            [octokit],
            Encoding.UTF8.GetBytes(Source.ReplaceLineEndings("\r\n")),
            graph
        );

        Assert.Equal(lf, crlf);
    }

    [Fact]
    public void A_directory_content_hash_is_the_same_for_CRLF_and_LF()
    {
        using var temp = new TempDirectory();
        string directory = temp.Root;
        Directory.CreateDirectory(Path.Combine(directory, "nested"));
        File.WriteAllText(Path.Combine(directory, "one.cs"), Source);
        File.WriteAllText(Path.Combine(directory, "nested", "two.md"), "line\nline\n");

        string lf = ExtensionTrustStore.ComputeContentHash(directory);

        ConvertEveryFileToCrlf(directory);

        Assert.Equal(lf, ExtensionTrustStore.ComputeContentHash(directory));
    }

    [Fact]
    public void A_single_file_content_hash_is_the_same_for_CRLF_and_LF()
    {
        using var temp = new TempDirectory();
        string path = temp.File("acme.cs");
        File.WriteAllText(path, Source);

        string lf = ExtensionTrustStore.ComputeFileContentHash(path);

        File.WriteAllText(path, Source.ReplaceLineEndings("\r\n"));

        Assert.Equal(lf, ExtensionTrustStore.ComputeFileContentHash(path));
    }

    [Fact]
    public void A_content_change_is_still_a_different_hash()
    {
        using var temp = new TempDirectory();
        string path = temp.File("acme.cs");
        File.WriteAllText(path, Source);

        string lf = ExtensionTrustStore.ComputeFileContentHash(path);

        File.WriteAllText(path, Source.Replace("return 1;", "return 2;"));

        Assert.NotEqual(lf, ExtensionTrustStore.ComputeFileContentHash(path));
    }

    private static void ConvertEveryFileToCrlf(string directory)
    {
        foreach (
            string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
        )
        {
            File.WriteAllText(file, File.ReadAllText(file).ReplaceLineEndings("\r\n"));
        }
    }
}

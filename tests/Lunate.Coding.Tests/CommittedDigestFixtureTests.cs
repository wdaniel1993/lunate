using System.Text;

namespace Lunate.Coding.Tests;

/// <summary>
/// The committed configuration behind the MCP trust hash (repo-quality spec: "committed fixtures
/// keep their bytes"): one mcp.json written on POSIX, read on every CI leg, asserted against one
/// digest — so three operating systems verify the same bytes rather than three reconstructions.
/// </summary>
public sealed class CommittedDigestFixtureTests
{
    /// <summary>The digest of <c>tests/fixtures/digests/mcp.json</c> as committed, LF and all.</summary>
    private const string McpDigest =
        "651ebdb8554db3852b6067a9e89cee1ff23becd9ce6e4fd32eaa7c830003baca";

    [Fact]
    public void The_committed_config_carries_LF_and_hashes_to_the_pinned_digest()
    {
        string path = Fixture("mcp.json");

        byte[] bytes = File.ReadAllBytes(path);

        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.EndsWith("\n", Encoding.UTF8.GetString(bytes));
        Assert.Equal(McpDigest, McpConfigTrustHash.ComputeFile(path));
    }

    [Fact]
    public void The_same_config_checked_out_with_CRLF_hashes_the_same()
    {
        using var temp = new TempDirectory();
        string crlf = temp.File("mcp.json");
        File.WriteAllText(crlf, File.ReadAllText(Fixture("mcp.json")).ReplaceLineEndings("\r\n"));

        Assert.NotEqual(File.ReadAllText(Fixture("mcp.json")), File.ReadAllText(crlf));
        Assert.Equal(McpDigest, McpConfigTrustHash.ComputeFile(crlf));
    }

    private static string Fixture(string name)
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "lunate.slnx")))
        {
            root = Path.GetDirectoryName(root)!;
        }

        return Path.Combine(root, "tests", "fixtures", "digests", name);
    }
}

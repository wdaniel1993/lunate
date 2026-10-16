using Lunate.Extensibility;

namespace Lunate.Coding.Tests;

/// <summary>
/// The one trust-record rendering: each section reads with its own kind so <c>/trust</c> and the
/// trust command show what kind of record was listed or cleared.
/// </summary>
public sealed class TrustFormatTests
{
    [Fact]
    public void A_per_server_record_reads_as_an_mcp_server()
    {
        string text = TrustFormat.RecordText(
            new TrustRecord(TrustSection.McpServer, "/work", "0123456789abcdef")
        );

        Assert.Equal("mcp server — /work 01234567", text);
    }

    [Fact]
    public void The_records_text_lists_every_section()
    {
        string text = TrustFormat.RecordsText([
            new TrustRecord(TrustSection.Extensions, "/work", "aaaaaaaaaaaaaaaa"),
            new TrustRecord(TrustSection.McpConfig, "/work/.mcp.json", "bbbbbbbbbbbbbbbb"),
            new TrustRecord(TrustSection.McpServer, "/work", "cccccccccccccccc"),
        ]);

        Assert.Equal(
            "trust: extensions — /work aaaaaaaa; mcp.json — /work/.mcp.json bbbbbbbb; mcp server — /work cccccccc",
            text
        );
    }
}

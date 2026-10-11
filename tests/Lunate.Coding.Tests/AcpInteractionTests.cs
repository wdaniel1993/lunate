using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>
/// ACP mode's cells of the degradation matrix: everything goes to the host log, extension blocks
/// are logged as unsent, and nothing new can reach stdout (the protocol owns it).
/// </summary>
public sealed class AcpInteractionTests
{
    [Fact]
    public void Notices_and_blocks_go_to_the_host_log()
    {
        var lines = new List<string>();
        var interaction = new AcpUserInteraction(lines.Add);

        interaction.Notify(new UserNotice(NoticeLevel.Warning, "retrying (attempt 1)"));
        Assert.Equal(["retrying (attempt 1)"], lines);

        interaction.UpdateStatus(
            new UserStatus("model", 1, 2, "/repo", "main", [new TextSegment("w", TextStyle.Dim)])
        );
        Assert.Single(lines);

        interaction.RenderExtension(
            new ExtensionBlock("read", [new TextSegment("block", TextStyle.Accent)])
        );
        Assert.Equal(2, lines.Count);
        Assert.Contains("read", lines[1], StringComparison.Ordinal);
        Assert.Contains("not sent", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_acp_run_writes_nothing_to_stdout()
    {
        using var input = new MemoryStream();
        using var output = new MemoryStream();
        using var errors = new StringWriter();

        int exitCode = AcpMode.Run(errors, input, output, TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Empty(output.ToArray());
    }
}

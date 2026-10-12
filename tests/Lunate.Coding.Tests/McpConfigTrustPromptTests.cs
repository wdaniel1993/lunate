using Lunate.Protocols;

namespace Lunate.Coding.Tests;

public sealed class McpConfigTrustPromptTests
{
    private const string ConfigPath = "/work/.lunate/mcp.json";

    [Fact]
    public async Task Non_interactive_prompt_refuses_without_output()
    {
        var prompt = new NonInteractiveMcpConfigTrustPrompt();

        bool approved = await prompt.ApproveAsync(
            ConfigPath,
            [],
            TestContext.Current.CancellationToken
        );

        Assert.False(approved);
    }
}

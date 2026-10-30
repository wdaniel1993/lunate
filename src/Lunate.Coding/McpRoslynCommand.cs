using System.Globalization;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Protocols;

namespace Lunate.Coding;

/// <summary>
/// <c>lunate mcp roslyn</c>: the five <c>cs_*</c> tools as a stdio MCP server, for clients such as
/// Claude Code, OpenCode or any MCP host. The server is bound to the directory it starts in, loads
/// the solution on the first call, and exposes only tools whose contract is read-only —
/// <c>cs_rename</c> returns a plan and never writes, so verified edits stay in Lunate, behind the
/// approval panel.
/// </summary>
internal static class McpRoslynCommand
{
    internal const string ServerName = "lunate-roslyn";

    private const string Instructions =
        "Lunate's read-only C# tools for the directory this server was started in. "
        + "cs_rename returns a plan of per-file edits and never writes; apply verified edits in "
        + "Lunate, where they go through its approval panel.";

    internal static async Task<int> RunAsync(
        string workingDirectory,
        Stream input,
        Stream output,
        TextWriter errors,
        CancellationToken ct
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        Workspace workspace = new(workingDirectory);
        string root = workspace.WorktreeRoot;
        ExtensionSession? extensions = null;

        try
        {
            LunateHome.StoreOutsideWorkspace(root, workspace.RepoRoot);
            extensions = await ExtensionSession
                .StartAsync(workspace.Identity, new ExtensionSeams { NoExtensions = true }, ct)
                .ConfigureAwait(false);
            IReadOnlyList<ITool> tools = extensions.ComposeTools(workspace);
            errors.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"lunate mcp roslyn: serving {tools.Count} tools from '{root}' (protocol on stdout, diagnostics on stderr)"
                )
            );

            await McpToolServer
                .RunAsync(
                    tools,
                    ServerName,
                    ProductVersion.Display,
                    Instructions,
                    root,
                    input,
                    output,
                    ct
                )
                .ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return 130;
        }
        catch (Exception exception)
        {
            errors.WriteLine($"lunate mcp roslyn: {exception.Message}");
            return 1;
        }
        finally
        {
            if (extensions is not null)
            {
                await extensions.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}

using System.Globalization;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Protocols;

namespace Lunate.Coding;

/// <summary>
/// <c>lunate mcp roslyn</c>: the five <c>cs_*</c> tools as a stdio MCP server, for clients such as
/// Claude Code, OpenCode or any MCP host. The server uses client-declared roots when available and
/// otherwise its launch directory, loads the solution on the first call, and exposes only tools
/// whose contract is read-only —
/// <c>cs_rename</c> returns a plan and never writes, so verified edits stay in Lunate, behind the
/// approval panel.
/// </summary>
internal static class McpRoslynCommand
{
    internal const string ServerName = "lunate-roslyn";

    private const string Instructions =
        "Lunate's read-only C# tools for client-declared workspace roots, or the directory this server was started in when no roots are declared. "
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
                .StartAsync(
                    workspace.Identity,
                    // Built-ins only: the C# tools serve from this process, the store is not read.
                    new ExtensionSeams { BuiltInsOnly = true },
                    ct
                )
                .ConfigureAwait(false);
            errors.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"lunate mcp roslyn: serving tools from '{root}' (protocol on stdout, diagnostics on stderr)"
                )
            );

            await McpToolServer
                .RunAsync(
                    [],
                    ServerName,
                    ProductVersion.Display,
                    Instructions,
                    root,
                    input,
                    output,
                    ct,
                    roots =>
                    {
                        // MCP roots can name directories other than the process launch directory.
                        // Compose through the shared manifest activation filter for each declared
                        // root; the selected active root also becomes the tool call's workspace.
                        if (roots is not { Count: > 0 })
                        {
                            // No roots channel, or no roots declared: use the server's launch
                            // directory. This is the same manifest evaluator as every other
                            // composition, and it runs only after the selected root is known.
                            return new McpWorkspaceToolSet(
                                extensions!.ComposeTools(workspace),
                                root
                            );
                        }

                        foreach (string clientRoot in roots)
                        {
                            Workspace clientWorkspace = new(clientRoot);
                            IReadOnlyList<ITool> clientTools = extensions!.ComposeTools(
                                clientWorkspace
                            );
                            if (clientTools.Count > 0)
                            {
                                return new McpWorkspaceToolSet(
                                    clientTools,
                                    clientWorkspace.WorktreeRoot
                                );
                            }
                        }

                        return new McpWorkspaceToolSet([], root);
                    }
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

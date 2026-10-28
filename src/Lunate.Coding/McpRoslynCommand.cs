using System.Globalization;
using System.Runtime.CompilerServices;
using Lunate.Agent;
using Lunate.Protocols;
using Lunate.Roslyn;

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
        CancellationToken ct,
        // Test seam: the composition is the production one unless a test supplies a backend.
        Func<ICSharpBackend>? backend = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        string root = Path.GetFullPath(workingDirectory);
        var lazy = new Lazy<ICSharpBackend>(() => backend?.Invoke() ?? CreateBackend(root));
        Func<ICSharpBackend> factory = () => lazy.Value;
        ITool[] tools =
        [
            new CsDiagnosticsTool(factory),
            new CsFindSymbolTool(factory),
            new CsFindReferencesTool(factory),
            new CsOutlineTool(factory),
            new CsRenameTool(factory),
        ];

        try
        {
            errors.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"lunate mcp roslyn: serving {tools.Length} tools from '{root}' (protocol on stdout, diagnostics on stderr)"
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
            if (lazy.IsValueCreated && lazy.Value is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    /// <summary>
    /// Nothing Roslyn or MSBuild loads before the first tool call: the backend is created inside this
    /// isolated method only when the lazy value is first read.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ICSharpBackend CreateBackend(string root) => new RoslynBackend(root);
}

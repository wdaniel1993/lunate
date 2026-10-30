using Lunate.Agent;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using McpServer = ModelContextProtocol.Server.McpServer;
using SdkServerOptions = ModelContextProtocol.Server.McpServerOptions;

namespace Lunate.Protocols;

/// <summary>
/// A stdio MCP server over a list of <see cref="ITool"/>: <c>initialize</c> and <c>tools/list</c>
/// answer from the tools' own declarations, <c>tools/call</c> runs the tool in-process. Stdout
/// carries JSON-RPC only; callers own their diagnostics and write them to stderr, matching the ACP
/// mode's purity contract. A tool that does not declare itself read-only is refused, because there
/// is no approval panel behind this server.
/// </summary>
internal static class McpToolServer
{
    /// <summary>The stable protocol revision this server negotiates.</summary>
    internal const string ProtocolVersion = "2025-11-25";

    internal static async Task RunAsync(
        IReadOnlyList<ITool> tools,
        string serverName,
        string serverVersion,
        string instructions,
        string workingDirectory,
        Stream input,
        Stream output,
        CancellationToken ct,
        Func<IReadOnlyList<string>?, McpWorkspaceToolSet>? resolveToolSet = null
    )
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        ValidateTools(tools);
        var updateLock = new SemaphoreSlim(1, 1);
        ServedToolSet activeTools = Serve(new McpWorkspaceToolSet(tools, workingDirectory));
        bool rootsLoaded = false;
        McpServer? server = null;

#pragma warning disable MCP9005 // Roots are retained for the pinned 2025-11-25 stdio protocol.
        async ValueTask EnsureRootsLoadedAsync(CancellationToken cancellationToken)
        {
            if (rootsLoaded || resolveToolSet is null || server?.ClientCapabilities?.Roots is null)
            {
                return;
            }

            await updateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (rootsLoaded)
                {
                    return;
                }

                IReadOnlyList<string> roots = await RequestClientRootsAsync(
                        server,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                activeTools = Serve(resolveToolSet(roots));
                rootsLoaded = true;
            }
            finally
            {
                updateLock.Release();
            }
        }

        async ValueTask HandleRootsChangedAsync(CancellationToken cancellationToken)
        {
            if (
                resolveToolSet is null
                || server?.ClientCapabilities?.Roots?.ListChanged is not true
                || !rootsLoaded
            )
            {
                return;
            }

            await updateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                IReadOnlyList<string> roots = await RequestClientRootsAsync(
                        server,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                ServedToolSet replacement = Serve(resolveToolSet(roots));
                bool changed = !SameToolSet(activeTools, replacement);
                activeTools = replacement;
                if (changed)
                {
                    await server
                        .SendMessageAsync(
                            new JsonRpcNotification
                            {
                                Method = NotificationMethods.ToolListChangedNotification,
                            },
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                updateLock.Release();
            }
        }
#pragma warning restore MCP9005

#pragma warning disable MCP9005 // The roots/list_changed notification remains part of the pinned protocol.
        var options = new SdkServerOptions
        {
            ServerInfo = new Implementation { Name = serverName, Version = serverVersion },
            ServerInstructions = instructions,
            ProtocolVersion = ProtocolVersion,
            Capabilities = new ServerCapabilities
            {
                Tools = new ToolsCapability { ListChanged = resolveToolSet is not null },
            },
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = async (request, cancellationToken) =>
                {
                    await EnsureRootsLoadedAsync(cancellationToken).ConfigureAwait(false);
                    ServedToolSet snapshot = activeTools;
                    return new ListToolsResult
                    {
                        Tools = [.. snapshot.Tools.Select(tool => tool.ProtocolTool)],
                    };
                },
                CallToolHandler = async (request, cancellationToken) =>
                {
                    await EnsureRootsLoadedAsync(cancellationToken).ConfigureAwait(false);
                    string? name = request.Params?.Name;
                    McpServerToolAdapter? tool = activeTools.Tools.FirstOrDefault(candidate =>
                        string.Equals(candidate.ProtocolTool.Name, name, StringComparison.Ordinal)
                    );
                    if (tool is null)
                    {
                        throw new McpProtocolException(
                            $"Unknown tool: '{name ?? ""}'",
                            McpErrorCode.InvalidParams
                        );
                    }

                    return await tool.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
                },
                NotificationHandlers =
                [
                    new(
                        NotificationMethods.RootsListChangedNotification,
                        (_, cancellationToken) => HandleRootsChangedAsync(cancellationToken)
                    ),
                ],
            },
        };
#pragma warning restore MCP9005

        await using var transport = new StreamServerTransport(input, output, serverName, null);
        await using (server = McpServer.Create(transport, options))
        {
            await server.RunAsync(ct).ConfigureAwait(false);
        }
    }

#pragma warning disable MCP9005 // The pinned stdio protocol still uses the SDK's roots API.
    private static IReadOnlyList<string> RequestRoots(ListRootsResult roots)
    {
        StringComparer pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        return roots
            .Roots.Select(root =>
                Uri.TryCreate(root.Uri, UriKind.Absolute, out Uri? uri) && uri.IsFile
                    ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(uri.LocalPath))
                    : null
            )
            .OfType<string>()
            .Distinct(pathComparer)
            .ToArray();
    }

    private static async ValueTask<IReadOnlyList<string>> RequestClientRootsAsync(
        McpServer server,
        CancellationToken ct
    )
    {
        ListRootsResult result = await server
            .RequestRootsAsync(new ListRootsRequestParams(), ct)
            .ConfigureAwait(false);
        return RequestRoots(result);
    }
#pragma warning restore MCP9005

    private static void ValidateTools(IEnumerable<ITool> tools)
    {
        foreach (ITool tool in tools)
        {
            // There is no approval panel behind this server: a tool that can change the workspace
            // must not be exposable by accident.
            if (tool.Annotations?.ReadOnly is not true)
            {
                throw new InvalidOperationException(
                    $"the MCP server exposes read-only tools only; '{tool.Name}' does not declare readOnly"
                );
            }
        }
    }

    private static ServedToolSet Serve(McpWorkspaceToolSet toolSet)
    {
        ValidateTools(toolSet.Tools);
        return new ServedToolSet(
            toolSet.WorkingDirectory,
            [
                .. toolSet.Tools.Select(tool => new McpServerToolAdapter(
                    tool,
                    toolSet.WorkingDirectory
                )),
            ]
        );
    }

    private static bool SameToolSet(ServedToolSet left, ServedToolSet right)
    {
        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(left.WorkingDirectory, right.WorkingDirectory, pathComparison)
            && left.Tools.Select(tool => tool.ProtocolTool.Name)
                .SequenceEqual(
                    right.Tools.Select(tool => tool.ProtocolTool.Name),
                    StringComparer.Ordinal
                );
    }

    private sealed record ServedToolSet(
        string WorkingDirectory,
        IReadOnlyList<McpServerToolAdapter> Tools
    );
}

internal sealed record McpWorkspaceToolSet(IReadOnlyList<ITool> Tools, string WorkingDirectory);

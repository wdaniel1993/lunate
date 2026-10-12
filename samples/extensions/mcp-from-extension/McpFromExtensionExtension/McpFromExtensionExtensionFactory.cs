using Lunate.Extensibility.Abstractions;

namespace McpFromExtensionExtension;

/// <summary>
/// The one public factory the loader discovers in the entry assembly. It registers one MCP server
/// named <c>fake</c> that runs the bundled <c>FakeMcpServer</c> console app next to this assembly:
/// exposure <see cref="ToolExposure.Direct"/> with the <c>secret</c> tool overridden to
/// <see cref="ToolExposure.Hidden"/>. The marker path from the settings travels as the config's
/// environment variable. Registration is registration only: nothing starts until the first tool
/// listing.
/// </summary>
public sealed class McpFromExtensionExtensionFactory : IExtensionFactory
{
    public IExtension Create(IExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        McpSettings settings = McpSettings.From(context.Settings);
        string serverDll = Path.Combine(
            Path.GetDirectoryName(typeof(McpFromExtensionExtensionFactory).Assembly.Location)!,
            "FakeMcpServer.dll"
        );
        IReadOnlyDictionary<string, string>? environment = settings.MarkerPath
            is { Length: > 0 } marker
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["LUNATE_FAKE_MCP_MARKER"] = marker,
            }
            : null;
        context.RegisterMcpServer(
            new McpServerRegistration(
                "fake",
                new McpServerConfig("dotnet", ["exec", serverDll], environment),
                ToolExposure.Direct,
                new Dictionary<string, ToolExposure> { ["secret"] = ToolExposure.Hidden }
            )
        );

        return new McpFromExtensionExtension(context.Id);
    }
}

/// <summary>The extension instance.</summary>
public sealed class McpFromExtensionExtension(string id) : IExtension
{
    public string Id { get; } = id;
}

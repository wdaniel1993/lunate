using Lunate.Extensibility.Abstractions;

namespace LspDiagnosticsExtension;

/// <summary>
/// The one public factory the loader discovers in the entry assembly. It reads the settings,
/// resolves the workspace identity handle, registers the lazily started language server service,
/// the <c>typescript</c> write verifier and the <c>ToolResultReady</c> handler. Registration only:
/// the process starts on the first eligible write verification or tool result.
/// </summary>
public sealed class LspDiagnosticsExtensionFactory : IExtensionFactory
{
    public IExtension Create(IExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        LspSettings settings = LspSettings.From(context.Settings);
        context.TryGetCoreService("core/workspace", out WorkspaceInfo? workspace);
        var server = new LspServerService(settings, context.Log);
        context.RegisterService($"ext/{context.Id}/server", server);
        context.RegisterWriteVerifier(
            new WriteVerifierRegistration(
                "typescript",
                new LspWriteVerifier(context.Id, context.Log, server, settings)
            )
        );
        context.Register(
            new LspDiagnosticsToolResultReadyHandler(
                context.Id,
                context.Log,
                server,
                settings,
                workspace
            )
        );

        return new LspDiagnosticsExtension(context.Id);
    }
}

/// <summary>The extension instance.</summary>
public sealed class LspDiagnosticsExtension(string id) : IExtension
{
    public string Id { get; } = id;
}

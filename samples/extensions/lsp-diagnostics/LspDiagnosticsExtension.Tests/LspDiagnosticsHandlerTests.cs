using System.Text.Json;
using LspDiagnosticsExtension;
using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Testing;

namespace LspDiagnosticsExtension.Tests;

public sealed class LspDiagnosticsHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_handler_passes_through_when_the_workspace_or_the_path_is_missing()
    {
        var log = new RecordingExtensionLog();
        var settings = new LspSettings("dotnet", [], TimeSpan.FromSeconds(1), 5);
        var server = new LspServerService(settings, log);
        JsonElement noPath = JsonDocument.Parse("{}").RootElement.Clone();

        var withoutWorkspace = new LspDiagnosticsToolResultReadyHandler(
            "lsp-diagnostics",
            log,
            server,
            settings,
            workspace: null
        );
        ToolResultReadyResult missingIdentity = await withoutWorkspace.HandleAsync(
            new ToolResultReadyPayload("r1", "c1", "write", noPath, "original", false),
            Ct
        );
        Assert.Equal("original", missingIdentity.Output);
        Assert.Null(missingIdentity.StructuredContent);

        var handler = new LspDiagnosticsToolResultReadyHandler(
            "lsp-diagnostics",
            log,
            server,
            settings,
            new WorkspaceInfo("/work", null, null)
        );
        ToolResultReadyResult missingPath = await handler.HandleAsync(
            new ToolResultReadyPayload("r1", "c1", "write", noPath, "original", false),
            Ct
        );
        Assert.Equal("original", missingPath.Output);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task A_path_outside_the_workspace_passes_through_and_is_reported()
    {
        var log = new RecordingExtensionLog();
        var settings = new LspSettings("dotnet", [], TimeSpan.FromSeconds(1), 5);
        var handler = new LspDiagnosticsToolResultReadyHandler(
            "lsp-diagnostics",
            log,
            new LspServerService(settings, log),
            settings,
            new WorkspaceInfo("/work", null, null)
        );
        JsonElement outside = JsonDocument.Parse("""{"path":"../evil.cs"}""").RootElement.Clone();

        ToolResultReadyResult result = await handler.HandleAsync(
            new ToolResultReadyPayload("r1", "c1", "write", outside, "original", false),
            Ct
        );

        Assert.Equal("original", result.Output);
        Assert.Contains(
            log.Messages,
            message => message.Contains("outside the workspace", StringComparison.Ordinal)
        );
    }
}

using Lunate.Ai;
using Lunate.Tui;
using Spectre.Console;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    /// <summary>
    /// Prints the welcome header block into scrollback: once on the first frame and again on
    /// <c>/new</c> (design D1/D5). A null count record renders the placeholder and arms the one
    /// counts update line that follows composition; the first-run form prints one actionable fix
    /// and no counts line at all (design D4). The <c>startupHeader</c> setting suppresses it.
    /// </summary>
    private void PrintWelcomeHeader(WelcomeHeaderCounts? counts)
    {
        _headerCountsPending = false;
        if (!_settings.StartupHeader)
        {
            return;
        }

        WelcomeHeaderState state = HeaderState(counts);
        int width = _console.Size.Columns;
        WriteScrollbackLines(WelcomeHeaderRenderer.Render(state, _capabilities, width));
        _headerCountsPending =
            counts is null
            && state.Fix is null
            && WelcomeHeaderRenderer.ShowsBlock(_capabilities, width);
    }

    /// <summary>The one counts update line after composition finished; never a redraw of the block.</summary>
    private void PrintHeaderCounts()
    {
        if (!_headerCountsPending)
        {
            return;
        }

        _headerCountsPending = false;
        WriteScrollbackLines([WelcomeHeaderRenderer.CountsLine(HeaderCounts(), _capabilities)]);
    }

    /// <summary>
    /// The header's render state. The model is the session's own once the harness exists (a
    /// <c>/model</c> switch is reflected by the <c>/new</c> reprint); the first frame prints before
    /// resolution, so it names the configured catalog entry — an extension-registered id keeps the
    /// configured name without a context window until then.
    /// </summary>
    private WelcomeHeaderState HeaderState(WelcomeHeaderCounts? counts)
    {
        ModelInfo? model = HeaderModel();
        string configured = _settings.Model?.Trim() ?? string.Empty;
        return new WelcomeHeaderState
        {
            Version = ProductVersion.Display,
            ModelId = model?.Id ?? configured,
            ContextWindow = model?.ContextWindow ?? 0,
            Workspace = _workingDirectory,
            CSharpWorkspace = HarnessFactory.LooksLikeCSharpWorkspace(_workspace.WorktreeRoot),
            Counts = counts,
            Fix = model is null ? null : ProviderKeyProbe.FixFor(model, _auth, _environment),
        };
    }

    private ModelInfo? HeaderModel() =>
        _harness is not null ? _model
        : string.IsNullOrWhiteSpace(_settings.Model) ? null
        : _catalog.Find(_settings.Model);

    /// <summary>
    /// The counts the header shows: discovered extensions, the configured and extension-registered
    /// MCP servers (a shadowed registration is not a server the session runs) and the valid
    /// discovered skills. A kind that found nothing stays an honest zero.
    /// </summary>
    private WelcomeHeaderCounts HeaderCounts() =>
        new(
            Extensions: _extensions?.Descriptors.Count ?? 0,
            McpServers: (_mcp?.Statuses.Count ?? 0)
                + (_extensions?.McpServers?.Statuses.Count ?? 0),
            Skills: _skills?.Skills.Count(skill => skill.Valid) ?? 0
        );

    /// <summary>
    /// Writes finished lines into scrollback through the live area's one writer: the header, its
    /// counts update line and the <c>/help</c> listing all land above the live region, never in it.
    /// </summary>
    private void WriteScrollbackLines(IReadOnlyList<string> lines) =>
        _live.WriteScrollback(console =>
        {
            foreach (string line in lines)
            {
                console.Write(new Markup(line + "\n"));
            }
        });
}

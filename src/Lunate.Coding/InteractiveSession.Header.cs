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
        _headerHasUpdateLine = false;
        if (!_settings.StartupHeader)
        {
            return;
        }

        WelcomeHeaderState state = HeaderState(counts);
        _headerHasUpdateLine = state.Fix is null;
        int width = _console.Size.Columns;
        WriteScrollbackLines(WelcomeHeaderRenderer.Render(state, _capabilities, width));
        _headerCountsPending =
            counts is null
            && state.Fix is null
            && WelcomeHeaderRenderer.ShowsBlock(_capabilities, width);
    }

    /// <summary>
    /// The background check behind the first frame: a fresh cache returns without a request, a
    /// newly found release becomes the header's line only when the header did not already carry it,
    /// and a failure prints nothing at all.
    /// </summary>
    private async Task AnnounceUpdateAsync(UpdateCheckSeams seams, CancellationToken ct)
    {
        UpdateInfo? found = await UpdateCheck.CheckAsync(seams, ct).ConfigureAwait(false);
        UpdateInfo? newer = UpdateCheck.IsNewer(found, ProductVersion.Display);
        if (newer is null)
        {
            return;
        }

        bool first = _availableUpdate is null;
        _availableUpdate = newer;
        if (!first || !_headerHasUpdateLine)
        {
            return;
        }

        WriteScrollbackLines([
            WelcomeHeaderRenderer.UpdateLine(newer.Version, newer.ChangelogUrl, _capabilities),
        ]);
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
            Version = ProductVersion.Build,
            ModelId = model?.Id ?? configured,
            ContextWindow = model?.ContextWindow ?? 0,
            Workspace = _workingDirectory,
            CSharpWorkspace = _extensions?.RoslynIsActive(_workspace.WorktreeRoot) == true,
            Home = LunateHome.ActiveHomeLine,
            Counts = counts,
            Fix = FirstRunFix(model, configured),
            UpdateVersion = _availableUpdate?.Version,
            UpdateUrl = _availableUpdate?.ChangelogUrl,
        };
    }

    /// <summary>
    /// The one actionable fix of the first-run state, or null when the session is configured (design
    /// D4). Nothing configured has no model to probe, so the header names <c>/auth</c> as the next
    /// step and still prints no counts or hint block (D8 #9); a configured id the catalog does not
    /// know yet — an extension-registered model on the first frame — keeps the normal block (D8 #6).
    /// </summary>
    private string? FirstRunFix(ModelInfo? model, string configured)
    {
        if (model is not null)
        {
            return ProviderKeyProbe.FixFor(model, _auth, _environment);
        }

        return configured.Length == 0 ? ProviderKeyProbe.ConfigureProviderFix : null;
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

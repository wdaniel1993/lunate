using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility;
using Lunate.Tui;
using Spectre.Console;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    /// <summary>
    /// <c>/export html|jsonl [path] [--force]</c> (design decisions 4 to 8): the destination is
    /// defaulted to <c>~/.lunate/exports</c> — never the current directory, where an export would be
    /// committed — an existing file is refused without <c>--force</c>, every credential Lunate
    /// knows is redacted before anything is written, and the confirmation names the absolute path,
    /// the entry count and the review notice. Read-only with respect to the session, so it stays
    /// available while a turn runs.
    /// </summary>
    private void ExportSession(string argument)
    {
        if (_session is not { } session)
        {
            Notify("no session file to export");
            return;
        }

        if (!ExportRequest.TryParse(argument, out ExportRequest request, out string usage))
        {
            Notify(usage, NoticeLevel.Warning);
            return;
        }

        string title = SessionTitle.Read(session.Path) ?? session.SessionId;
        string store = Lunate.Ai.LunateHome.In(_options.HomeDirectory);
        DateTimeOffset now = _options.Clock?.Invoke() ?? DateTimeOffset.Now;
        string destination = ExportTargets.Resolve(request, store, _workingDirectory, title, now);
        if (string.Equals(destination, Path.GetFullPath(session.Path), StringComparison.Ordinal))
        {
            Notify("refusing to export over the session file itself", NoticeLevel.Warning);
            return;
        }

        if (File.Exists(destination) && !request.Force)
        {
            Notify(
                FormattableString.Invariant($"refusing to overwrite {destination} without --force"),
                NoticeLevel.Warning
            );
            return;
        }

        try
        {
            CredentialRedactor redactor = CredentialRedactorSource.Collect(
                _auth,
                _environment,
                _options.Extensions?.StorePath ?? ExtensionHostOptions.Default.StorePath,
                McpConfigPaths(),
                AuthStore.ProviderStoreFilePath(_options.AuthPath)
            );
            ExportOutcome outcome = SessionExporter.Write(
                session,
                request,
                destination,
                title,
                redactor
            );
            WriteScrollbackLines([
                .. new[]
                {
                    FormattableString.Invariant(
                        $"exported {outcome.Entries:N0} entries to {outcome.Path}"
                    ),
                    "may contain file contents and command output; review before sharing",
                }.Select(Markup.Escape),
            ]);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Notify("export failed: " + exception.Message, NoticeLevel.Error);
        }
    }

    /// <summary>
    /// The MCP config files the session reads, in the composition's own order: the two user files
    /// and the three project files of the worktree.
    /// </summary>
    private IReadOnlyList<string> McpConfigPaths()
    {
        List<string> paths =
        [
            _options.McpAgentsConfigPath ?? McpComposition.DefaultAgentsConfigPath,
            _options.McpConfigPath ?? McpComposition.DefaultConfigPath,
        ];
        if (_workspace is { } workspace)
        {
            paths.AddRange(McpComposition.ProjectConfigPaths(workspace.WorktreeRoot));
        }

        return paths;
    }
}

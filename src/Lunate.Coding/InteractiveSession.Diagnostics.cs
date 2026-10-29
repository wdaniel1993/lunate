using System.Globalization;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility;
using Spectre.Console;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    /// <summary>The recent events the bundle can list; older ones are dropped as new ones arrive.</summary>
    private readonly List<DiagnosticEvent> _diagnosticEvents = [];

    /// <summary>
    /// The scrubber the session's own host diagnostics take before they are recorded for the
    /// bundle: spill-failure reasons and later exceptions never reach the record unscrubbed.
    /// </summary>
    private readonly DiagnosticScrubber _eventScrubber = DiagnosticScrubber.FromEnvironment(
        CredentialRedactor.Empty
    );

    /// <summary>Every event the session has seen, including those trimmed from the ring.</summary>
    private int _diagnosticEventCount;

    /// <summary>
    /// <c>/debug [path]</c>: writes one diagnostic bundle and names the absolute path. Read-only
    /// with respect to the session — no model call, no session write — so it is available while a
    /// turn runs. The default destination is under the state root, never the working directory.
    /// Every host diagnostic inside the bundle — a spill-failure reason, an extension load reason,
    /// an OS error message — enters through the one scrubbing path, and the whole document is then
    /// redacted against the values <see cref="CredentialRedactorSource"/> collects; the turn's own
    /// content never enters the bundle at all.
    /// </summary>
    private void WriteDiagnosticBundle(string argument)
    {
        if (!TryParseDiagnosticPath(argument, out string? path, out string usage))
        {
            Notify(usage, NoticeLevel.Warning);
            return;
        }

        DateTimeOffset now = _options.Clock?.Invoke() ?? DateTimeOffset.UtcNow;
        string store = LunateHome.In(_options.HomeDirectory);
        string destination = DiagnosticBundle.Resolve(
            path,
            store,
            _workingDirectory,
            _session?.SessionId,
            now
        );
        if (File.Exists(destination))
        {
            Notify(
                FormattableString.Invariant(
                    $"refusing to overwrite {destination}; choose another path"
                ),
                NoticeLevel.Warning
            );
            return;
        }

        DiagnosticScrubber scrubber = DiagnosticScrubber.FromEnvironment(CredentialRedactor.Empty);
        try
        {
            CredentialRedactor redactor = CredentialRedactorSource.Collect(
                _auth,
                _environment,
                _options.Extensions?.StorePath ?? ExtensionHostOptions.Default.StorePath,
                McpConfigPaths()
            );
            scrubber = DiagnosticScrubber.FromEnvironment(redactor);
            DiagnosticBundleInput input = DiagnosticInput(destination, now, store) with
            {
                Scrubber = scrubber,
            };
            DiagnosticBundleWritten written = DiagnosticBundle.Write(input, redactor);
            WriteScrollbackLines([
                .. new[]
                {
                    FormattableString.Invariant(
                        $"diagnostic bundle written to {written.Destination}"
                    ),
                    FormattableString.Invariant(
                        $"wrote {written.Files} file, {written.Bytes.ToString("N0", CultureInfo.InvariantCulture)} bytes"
                    ),
                    "may contain paths and configuration; review before sharing",
                }.Select(Markup.Escape),
            ]);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Notify("diagnostic bundle failed: " + scrubber.Describe(exception), NoticeLevel.Error);
        }
    }

    /// <summary>
    /// Parses the optional single destination. A path with a space can be quoted, which keeps the
    /// command usable from a directory whose name has one. More than one token is a usage error.
    /// </summary>
    private static bool TryParseDiagnosticPath(string argument, out string? path, out string usage)
    {
        const string Usage = "usage: /debug [path]";
        usage = Usage;
        path = null;
        string trimmed = argument.Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            string quoted = trimmed[1..^1].Trim();
            if (quoted.Length == 0)
            {
                return false;
            }

            path = quoted;
            usage = string.Empty;
            return true;
        }

        if (trimmed.Any(char.IsWhiteSpace))
        {
            return false;
        }

        path = trimmed;
        usage = string.Empty;
        return true;
    }

    /// <summary>The evidence one bundle carries. Text content is never included; only shapes and paths.</summary>
    private DiagnosticBundleInput DiagnosticInput(
        string destination,
        DateTimeOffset now,
        string store
    )
    {
        DiagnosticEvent[] events;
        int recorded;
        lock (_diagnosticEvents)
        {
            events = [.. _diagnosticEvents];
            recorded = _diagnosticEventCount;
        }

        return new DiagnosticBundleInput
        {
            Destination = destination,
            GeneratedAt = now,
            SessionPath = _session?.Path,
            SessionId = _session?.SessionId,
            SessionTitle = _session is { } session ? SessionTitle.Read(session.Path) : null,
            SessionEntries = _session?.Entries.Count ?? 0,
            WorkingDirectory = _workspace.WorktreeRoot,
            GitBranch = _branch,
            StateRoot = store,
            HomeConfigured = LunateHome.IsConfigured,
            Settings = _settings,
            AuthKeys = [.. _auth.Keys.Keys.Order(StringComparer.Ordinal)],
            Model = _model,
            ModelSource = _extensionModelId is null ? "catalog" : "extension",
            ModelRoute = _modelRoute,
            Thinking = _thinkingLevel,
            ExtensionsText = ExtensionsText(),
            ExtensionErrors = _extensions?.Errors ?? [],
            SingleFileExtensions = _extensions?.RefreshSingleFileExtensions() ?? [],
            Events = events,
            RecordedEvents = recorded,
        };
    }

    /// <summary>
    /// Records one event for the bundle. Called from the event pump before the event is rendered,
    /// so a child run's events are kept too (they explain a slow or failing turn) without their
    /// text. The ring is bounded: the bundle is a recent window, not a second transcript.
    /// </summary>
    private void RecordDiagnosticEvent(AgentEvent agentEvent)
    {
        DiagnosticEvent entry = new(
            _scheduler.Now,
            DiagnosticBundle.Describe(agentEvent, _eventScrubber)
        );
        lock (_diagnosticEvents)
        {
            _diagnosticEvents.Add(entry);
            _diagnosticEventCount++;
            if (_diagnosticEvents.Count > DiagnosticBundle.KeptEventLimit)
            {
                _diagnosticEvents.RemoveAt(0);
            }
        }
    }
}

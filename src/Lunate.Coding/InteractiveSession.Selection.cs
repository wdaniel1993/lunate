using Lunate.Agent;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    /// <summary>The session store root the launch selection reads; defaults to the profile's store.</summary>
    private string SessionsRoot => _options.SessionsRoot ?? SessionSelection.DefaultRoot;

    /// <summary>
    /// Whether the launch deferred a fresh session until a turn starts: the picker opens first, so
    /// choosing an existing session (or abandoning the picker) must not leave an empty file behind.
    /// </summary>
    private bool _deferFreshSession;

    /// <summary>
    /// Resolves the session the session starts with, after the workspace and the session directory
    /// exist: an injected start file wins over the CLI's selection flag; without one a fresh session
    /// is created. A selection that cannot resolve reports a notice and still starts fresh, so the
    /// TUI stays usable.
    /// </summary>
    private void StartInitialSession()
    {
        if (_options.StartSession is { } start)
        {
            _session = LoadStartupSession(start, start);
            return;
        }

        LaunchOptions launch = _options.Launch;
        if (launch.Session is { } value)
        {
            _session = SessionFromValue(value);
            return;
        }

        if (launch.Continue)
        {
            _session = ContinueSession();
            return;
        }

        if (launch.NoSession)
        {
            _fileless = true;
            _session = null;
            return;
        }

        // --resume (and the injected picker seam) opens the picker after composition: no session
        // exists until the user chooses one or starts a turn, so an abandoned picker leaves no file.
        if (_options.StartWithPicker || launch.Resume)
        {
            _session = null;
            _fileless = false;
            _deferFreshSession = true;
            return;
        }

        _session = FreshSession();
    }

    /// <summary>
    /// <c>--session &lt;value&gt;</c>: a path separator or a <c>.jsonl</c> suffix marks a file; any
    /// other value is an id resolved across the store. An unknown or ambiguous id reports the input
    /// (the matches by path) and starts fresh.
    /// </summary>
    private Session SessionFromValue(string value)
    {
        SessionChoice choice = SessionSelection.FromValue(SessionsRoot, value);
        if (choice.Error is { } error)
        {
            Notify(error, NoticeLevel.Error);
            return FreshSession();
        }

        return LoadStartupSession(choice.Path!, choice.Label);
    }

    private Session ContinueSession()
    {
        SessionChoice choice = SessionSelection.Continue(SessionsRoot, _workspace.WorktreeRoot);
        if (choice.Error is { } error)
        {
            Notify(error, NoticeLevel.Error);
            return FreshSession();
        }

        return LoadStartupSession(choice.Path!, choice.Label);
    }

    /// <summary>Loads a selected file; a failure names the input and starts fresh.</summary>
    private Session LoadStartupSession(string path, string input)
    {
        try
        {
            return WithRedaction(Session.Load(path));
        }
        catch (Exception exception)
            when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Notify($"could not load session '{input}': {exception.Message}", NoticeLevel.Error);
            return FreshSession();
        }
    }

    private Session FreshSession()
    {
        _fileless = false;
        _deferFreshSession = false;
        return CreateFreshSession();
    }

    /// <summary>
    /// Creates the session a picker launch deferred, once a turn is about to start: the skills
    /// section composed without a session is recorded on it, and the harness is rebuilt against
    /// it so the run writes to the new file.
    /// </summary>
    private void MaterializeDeferredSession()
    {
        if (!_deferFreshSession || _session is not null)
        {
            return;
        }

        _session = CreateFreshSession();
        _deferFreshSession = false;
        if (_skillSection is { } skills)
        {
            RecordSkillsSection(skills);
        }

        _harness = BuildHarness(_model);
    }

    /// <summary>
    /// Creates a fresh session and records the launch prompt directives on it, so a later resume
    /// replays the caller's contract without the flags.
    /// </summary>
    private Session CreateFreshSession()
    {
        Session created = HarnessFactory.CreateSession(_sessionDirectory, _workspace);
        WithRedaction(created);
        SystemPrompt.Record(created, _options.Launch);
        return created;
    }

    /// <summary>
    /// Attaches the persistence redactor to a session this host created or resumed: what the model
    /// sees in memory stays as it was, while every line the session file and spill files receive
    /// has the credentials this host knows replaced.
    /// </summary>
    private Session WithRedaction(Session session)
    {
        HarnessFactory.AttachRedaction(
            session,
            _options.AuthPath,
            _options.Environment,
            _options.Extensions?.StorePath,
            _workspace.WorktreeRoot,
            _options.McpConfigPath,
            _options.McpAgentsConfigPath
        );
        return session;
    }
}

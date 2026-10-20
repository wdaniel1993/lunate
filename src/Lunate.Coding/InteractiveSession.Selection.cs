using Lunate.Agent;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    /// <summary>The session store root the launch selection reads; defaults to the profile's store.</summary>
    private string SessionsRoot => _options.SessionsRoot ?? SessionSelection.DefaultRoot;

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
            return Session.Load(path);
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
        return HarnessFactory.CreateSession(_sessionDirectory, _workspace);
    }
}

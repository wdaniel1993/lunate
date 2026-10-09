using System.Text;
using Lunate.Agent;
using Lunate.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lunate.Coding;

/// <summary>
/// The injectable seams of a print run. Production leaves them null and takes the defaults
/// (settings under the user profile, the embedded catalog, a real client factory, the session
/// paths of the working directory); tests pass temp paths and a fake factory.
/// </summary>
internal sealed record PrintModeOptions
{
    /// <summary>The prompt to run.</summary>
    public string Prompt { get; init; } = string.Empty;

    /// <summary>Stream every event to stdout as one JSON object per line.</summary>
    public bool Json { get; init; }

    /// <summary>The per-run flag that allows every tool call; never a saved setting.</summary>
    public bool Yolo { get; init; }

    /// <summary>The client factory; null builds the real factory with the auth store as key source.</summary>
    public IChatClientFactory? Factory { get; init; }

    /// <summary>The settings file path; null uses <c>~/.lunate/settings.json</c>.</summary>
    public string? SettingsPath { get; init; }

    /// <summary>The user model catalog path; null uses <c>~/.lunate/models.json</c>.</summary>
    public string? ModelsPath { get; init; }

    /// <summary>The auth store path; null uses <c>~/.lunate/auth.json</c>.</summary>
    public string? AuthPath { get; init; }

    /// <summary>The session directory; null derives it from the workspace and repository identity.</summary>
    public string? SessionDirectory { get; init; }

    /// <summary>The working directory; null uses the process working directory.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>The environment lookup for settings and auth; null reads the process environment.</summary>
    public Func<string, string?>? Environment { get; init; }
}

/// <summary>
/// The print-mode frontend: one prompt, no interactive prompt, the final answer on stdout and
/// diagnostics on stderr. It consumes <see cref="AgentHarness.RunAsync"/> and adds no core
/// knowledge; see the print-mode spec for the stdout and exit-code contract.
/// </summary>
internal static class PrintMode
{
    /// <summary>Runs one prompt; returns the process exit code (0 stop, 1 error, 2 length/step limit, 130 cancelled).</summary>
    internal static async Task<int> RunAsync(
        PrintModeOptions options,
        TextWriter output,
        TextWriter errors,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(errors);

        AgentSettings settings;
        ModelInfo model;
        ModelCatalog catalog;
        IChatClientFactory factory;
        try
        {
            settings = SettingsStore.Resolve(options.SettingsPath, options.Environment);
            catalog = ModelCatalog.Load(options.ModelsPath);
            model = ResolveModel(settings.Model, catalog);
            factory = options.Factory ?? CreateFactory(options);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.WriteLine($"lunate: {exception.Message}");
            return 1;
        }

        AgentHarness harness;
        try
        {
            harness = CreateHarness(options, settings, model, catalog, factory);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.WriteLine($"lunate: {exception.Message}");
            return 1;
        }

        var summary = new RunSummary(settings.Approval);
        bool cancelled = false;
        try
        {
            await foreach (AgentEvent agentEvent in harness.RunAsync(options.Prompt, ct))
            {
                if (options.Json)
                {
                    output.WriteLine(PrintEventJson.Serialize(agentEvent));
                }

                if (summary.Observe(agentEvent) is { } diagnostic)
                {
                    errors.WriteLine(diagnostic);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            cancelled = true;
        }
        catch (Exception exception)
        {
            errors.WriteLine($"lunate: {exception.Message}");
            return 1;
        }

        return Finish(summary, cancelled, options.Json, output, errors);
    }

    private static int Finish(
        RunSummary summary,
        bool cancelled,
        bool json,
        TextWriter output,
        TextWriter errors
    )
    {
        if (cancelled || summary.StopReason == StopReasons.Cancelled)
        {
            return 130;
        }

        if (summary.Error is { } error)
        {
            errors.WriteLine($"lunate: {error}");
            return 1;
        }

        switch (summary.StopReason)
        {
            case StopReasons.Stop:
                WriteAnswer(summary, json, output);
                return 0;
            case StopReasons.Length or StopReasons.StepLimit:
                WriteAnswer(summary, json, output);
                return 2;
            default:
                errors.WriteLine("lunate: the run ended without a finish event.");
                return 1;
        }
    }

    private static void WriteAnswer(RunSummary summary, bool json, TextWriter output)
    {
        if (!json && summary.FinalAnswer is { } answer)
        {
            output.Write(answer);
        }
    }

    private static AgentHarness CreateHarness(
        PrintModeOptions options,
        AgentSettings settings,
        ModelInfo model,
        ModelCatalog catalog,
        IChatClientFactory factory
    )
    {
        var workspace = new Workspace(options.WorkingDirectory ?? Environment.CurrentDirectory);
        var tools = new ToolRegistry();
        tools.Add(new ReadTool(workspace));
        tools.Add(new WriteTool(workspace));
        tools.Add(new EditTool(workspace));
        tools.Add(new BashTool(workspace));

        string directory = options.SessionDirectory ?? DefaultSessionDirectory(workspace);
        Session session = CreateSession(directory, workspace);
        var harnessOptions = new AgentHarnessOptions
        {
            SystemPrompt = SystemPrompt.Compose(workspace, [.. tools.Tools.Select(tool => tool.Name)]),
            WorkingDirectory = workspace.WorktreeRoot,
            ToolOutputLimit = settings.ToolOutputLimit,
            Approver = new NonInteractiveApprover(settings.Approval, options.Yolo),
            Session = session,
            ModelCatalog = catalog,
            ModelId = model.Id,
        };

        return new AgentHarness(factory.Create(model), tools, harnessOptions);
    }

    private static IChatClientFactory CreateFactory(PrintModeOptions options)
    {
        AuthStore auth = AuthStore.Load(options.AuthPath, options.Environment);
        return new ChatClientFactory(NullLoggerFactory.Instance, namedKeySource: auth.TryGet);
    }

    private static ModelInfo ResolveModel(string? modelId, ModelCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new InvalidOperationException(
                "No model is configured. Set 'model' in ~/.lunate/settings.json, set LUNATE_MODEL, "
                    + "or run lunate --discover <name-or-url> to draft a catalog entry."
            );
        }

        return catalog.Find(modelId)
            ?? throw new InvalidOperationException(
                $"Model '{modelId}' is not in the catalog. Run lunate --discover <name-or-url> "
                    + $"to draft a models.json entry or add '{modelId}' to ~/.lunate/models.json."
            );
    }

    private static string DefaultSessionDirectory(Workspace workspace) =>
        workspace.GitCommonDir is { } identity
            ? SessionPaths.ForRepository(identity, workspace.WorktreeRoot)
            : SessionPaths.ForProject(workspace.WorktreeRoot);

    /// <summary>
    /// Creates the session file under the directory with its id as the file name (the store names
    /// sessions <c>&lt;sessionId&gt;.jsonl</c>): create, rename to the store-assigned id, reload.
    /// </summary>
    private static Session CreateSession(string directory, Workspace workspace)
    {
        Directory.CreateDirectory(directory);
        string provisional = Path.Combine(directory, Path.GetRandomFileName());
        Session created = Session.Create(
            provisional,
            workspace.WorktreeRoot,
            workspace.GitCommonDir,
            workspace.WorktreeRoot
        );
        string path = Path.Combine(directory, SessionPaths.SessionFileName(created.SessionId));
        File.Move(provisional, path);
        return Session.Load(path);
    }

    private sealed class RunSummary(ApprovalPolicy policy)
    {
        private readonly Dictionary<string, StringBuilder> _openText = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _toolNames = new(StringComparer.Ordinal);

        public string? FinalAnswer { get; private set; }

        public string? StopReason { get; private set; }

        public string? Error { get; private set; }

        /// <summary>Tracks the run state; returns a stderr diagnostic for an error tool call, else null.</summary>
        public string? Observe(AgentEvent agentEvent)
        {
            switch (agentEvent)
            {
                case TextMessageStart start:
                    _openText[start.MessageId] = new StringBuilder();
                    break;
                case TextMessageContent content:
                    OpenText(content.MessageId).Append(content.Text);
                    break;
                case TextMessageEnd end:
                    if (_openText.Remove(end.MessageId, out var completed) && completed.Length > 0)
                    {
                        FinalAnswer = completed.ToString();
                    }

                    break;
                case ToolCallStart call:
                    _toolNames[call.CallId] = call.ToolName;
                    break;
                case ToolCallResult result when result.IsError:
                    return Diagnostic(_toolNames.GetValueOrDefault(result.CallId, "?"), result.Output);
                case RunFinished finished:
                    StopReason = finished.StopReason;
                    break;
                case RunError error:
                    Error = error.Message;
                    break;
            }

            return null;
        }

        private StringBuilder OpenText(string messageId) =>
            _openText.TryGetValue(messageId, out var text)
                ? text
                : _openText[messageId] = new StringBuilder();

        private string Diagnostic(string toolName, string output) =>
            output.StartsWith("Denied:", StringComparison.Ordinal)
                ? $"tool '{toolName}' denied: approval policy '{PolicyText(policy)}' (pass --yolo to run unattended)"
                : FirstNonEmptyLine(output) is { } line
                    ? $"tool '{toolName}' failed: {line}"
                    : $"tool '{toolName}' failed";

        private static string PolicyText(ApprovalPolicy policy) =>
            policy switch
            {
                ApprovalPolicy.Ask => "ask",
                ApprovalPolicy.AutoEdit => "auto-edit",
                _ => policy.ToString(),
            };

        private static string? FirstNonEmptyLine(string output)
        {
            foreach (string line in output.Split('\n'))
            {
                string trimmed = line.TrimEnd('\r').Trim();
                if (trimmed.Length > 0)
                {
                    return trimmed;
                }
            }

            return null;
        }
    }
}

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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return 130;
        }
        catch (Exception exception)
        {
            errors.WriteLine($"lunate: {exception.Message}");
            return 1;
        }

        AgentHarness harness;
        try
        {
            harness = CreateHarness(options, settings, model, catalog, factory);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return 130;
        }
        catch (Exception exception)
        {
            errors.WriteLine($"lunate: {exception.Message}");
            return 1;
        }

        var summary = new PrintRunSummary(settings.Approval);
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
        PrintRunSummary summary,
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

    private static void WriteAnswer(PrintRunSummary summary, bool json, TextWriter output)
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
            SystemPrompt = SystemPrompt.Compose(
                workspace,
                [.. tools.Tools.Select(tool => tool.Name)]
            ),
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
}

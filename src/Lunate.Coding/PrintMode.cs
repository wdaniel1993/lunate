using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility.Abstractions;
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

    /// <summary>The extension host seams; null takes the store, prompt and log defaults.</summary>
    public ExtensionSeams? Extensions { get; init; }

    /// <summary>The global mcp.json path; null uses <c>~/.lunate/mcp.json</c>.</summary>
    public string? McpConfigPath { get; init; }
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
        var interaction = new PrintUserInteraction(errors);

        AgentSettings settings;
        ModelInfo model;
        ModelCatalog catalog;
        IChatClientFactory factory;
        try
        {
            settings = SettingsStore.Resolve(options.SettingsPath, options.Environment);
            catalog = ModelCatalog.Load(options.ModelsPath);
            model = HarnessFactory.ResolveModel(settings.Model, catalog);
            factory =
                options.Factory
                ?? HarnessFactory.CreateFactory(options.AuthPath, options.Environment);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return 130;
        }
        catch (Exception exception)
        {
            interaction.Notify(new UserNotice(NoticeLevel.Error, exception.Message));
            return 1;
        }

        ExtensionSession? extensions = null;
        McpToolSet? mcp = null;
        try
        {
            IReadOnlyList<ITool> mcpTools = [];
            Workspace workspace;
            try
            {
                workspace = new Workspace(options.WorkingDirectory ?? Environment.CurrentDirectory);
                IExtensionLog log = options.Extensions?.Log ?? new StderrExtensionLog(errors);
                if (options.Extensions?.NoExtensions is not true)
                {
                    extensions = await ComposeExtensionsAsync(
                            options.Extensions,
                            workspace,
                            errors,
                            ct
                        )
                        .ConfigureAwait(false);
                }

                mcp = await McpComposition
                    .ComposeAsync(
                        workspace,
                        options.McpConfigPath,
                        options.Extensions,
                        new NonInteractiveMcpConfigTrustPrompt(),
                        log,
                        notice => interaction.Notify(notice),
                        ct
                    )
                    .ConfigureAwait(false);

                List<ITool> tools = [];
                if (extensions is not null)
                {
                    // Extension tools first, in registration order, then the MCP tools (design D7).
                    tools.AddRange(extensions.Tools);
                    // The configured names are the collision baseline: a same-named registration
                    // is skipped before listing (the configured server wins); nothing starts yet.
                    ExtensionMcpServers servers = extensions.ComposeMcpServers(
                        McpNameCollisions.ConfiguredServers(mcp)
                    );
                    ToolComposition.AddSkippingTaken(
                        tools,
                        await servers.ListToolsAsync(ct).ConfigureAwait(false),
                        "an extension-registered MCP server",
                        notice => interaction.Notify(new UserNotice(NoticeLevel.Warning, notice))
                    );
                }

                if (mcp is not null)
                {
                    ToolComposition.AddSkippingTaken(
                        tools,
                        await mcp.ListToolsAsync(ct).ConfigureAwait(false),
                        "mcp.json",
                        notice => interaction.Notify(new UserNotice(NoticeLevel.Warning, notice))
                    );
                    McpComposition.NotifyFailures(mcp, notice => interaction.Notify(notice));
                }

                mcpTools = tools;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return 130;
            }
            catch (Exception exception)
            {
                interaction.Notify(new UserNotice(NoticeLevel.Error, exception.Message));
                return 1;
            }

            AgentHarness harness;
            try
            {
                harness = CreateHarness(
                    options,
                    workspace,
                    settings,
                    model,
                    catalog,
                    factory,
                    extensions,
                    mcpTools
                );
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return 130;
            }
            catch (Exception exception)
            {
                interaction.Notify(new UserNotice(NoticeLevel.Error, exception.Message));
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
                        // One JSON object per line, canonically '\n'-terminated: the --json
                        // stream is a scripting contract and must not depend on the platform.
                        output.Write(PrintEventJson.Serialize(agentEvent));
                        output.Write('\n');
                    }

                    if (summary.Observe(agentEvent) is { } diagnostic)
                    {
                        errors.WriteLine(diagnostic);
                    }

                    if (agentEvent.ParentRunId is null && NoticeFor(agentEvent) is { } notice)
                    {
                        interaction.Notify(notice);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                cancelled = true;
            }
            catch (Exception exception)
            {
                interaction.Notify(new UserNotice(NoticeLevel.Error, exception.Message));
                return 1;
            }

            return Finish(summary, cancelled, options.Json, output, interaction);
        }
        finally
        {
            if (mcp is not null)
            {
                try
                {
                    await mcp.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    interaction.Notify(
                        new UserNotice(
                            NoticeLevel.Error,
                            "the MCP servers failed to stop cleanly: " + exception.Message
                        )
                    );
                }
            }

            if (extensions is not null)
            {
                try
                {
                    await extensions.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    interaction.Notify(
                        new UserNotice(
                            NoticeLevel.Error,
                            "the extension host failed to stop cleanly: " + exception.Message
                        )
                    );
                }
            }
        }
    }

    /// <summary>The events print mode now surfaces as ambient notices on stderr; the answer and
    /// the JSON event stream on stdout are untouched.</summary>
    private static UserNotice? NoticeFor(AgentEvent agentEvent) =>
        agentEvent switch
        {
            Retrying retrying => new UserNotice(
                NoticeLevel.Warning,
                FormattableString.Invariant($"retrying (attempt {retrying.Attempt})")
            ),
            CompactionApplied => new UserNotice(NoticeLevel.Info, "context compacted"),
            StepLimitReached limit => new UserNotice(
                NoticeLevel.Warning,
                FormattableString.Invariant($"step limit reached ({limit.MaxSteps} steps)")
            ),
            _ => null,
        };

    private static int Finish(
        PrintRunSummary summary,
        bool cancelled,
        bool json,
        TextWriter output,
        PrintUserInteraction interaction
    )
    {
        if (cancelled || summary.StopReason == StopReasons.Cancelled)
        {
            return 130;
        }

        if (summary.Error is { } error)
        {
            interaction.Notify(new UserNotice(NoticeLevel.Error, error));
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
                interaction.Notify(
                    new UserNotice(NoticeLevel.Error, "the run ended without a finish event.")
                );
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

    /// <summary>
    /// Composes the session's extension host: the mode's stderr writer receives host diagnostics;
    /// the trust prompt defaults to the non-interactive refusal (a terminal-less run never approves
    /// repository code).
    /// </summary>
    private static async Task<ExtensionSession> ComposeExtensionsAsync(
        ExtensionSeams? seams,
        Workspace workspace,
        TextWriter errors,
        CancellationToken ct
    )
    {
        ExtensionSeams resolved = (seams ?? new ExtensionSeams()) with
        {
            Log = seams?.Log ?? new StderrExtensionLog(errors),
        };
        return await ExtensionSession
            .StartAsync(workspace.Identity, resolved, ct)
            .ConfigureAwait(false);
    }

    private static AgentHarness CreateHarness(
        PrintModeOptions options,
        Workspace workspace,
        AgentSettings settings,
        ModelInfo model,
        ModelCatalog catalog,
        IChatClientFactory factory,
        ExtensionSession? extensions,
        IReadOnlyList<ITool> mcpTools
    )
    {
        ToolRegistry tools = HarnessFactory.CreateTools(workspace);
        foreach (ITool tool in mcpTools)
        {
            tools.Add(tool);
        }

        string directory =
            options.SessionDirectory ?? HarnessFactory.DefaultSessionDirectory(workspace);
        Session session = HarnessFactory.CreateSession(directory, workspace);
        var harnessOptions = new AgentHarnessOptions
        {
            SystemPrompt = SystemPrompt.Compose(
                workspace,
                [.. tools.Tools.Select(tool => tool.Name)]
            ),
            WorkingDirectory = workspace.WorktreeRoot,
            ToolOutputLimit = settings.ToolOutputLimit,
            SubagentLimits = settings.Subagents,
            Approver = new NonInteractiveApprover(settings.Approval, options.Yolo),
            Hooks = extensions?.Hooks,
            Session = session,
            FileChanges = (IFileChangeSink?)extensions?.FileChanges ?? NullFileChangeSink.Instance,
            ModelCatalog = catalog,
            ModelId = model.Id,
        };

        return new AgentHarness(factory.Create(model), tools, harnessOptions);
    }
}

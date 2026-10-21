using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility;
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

    /// <summary>The session store root the launch selection reads; null uses the profile's store.</summary>
    public string? SessionsRoot { get; init; }

    /// <summary>The working directory; null uses the process working directory.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>The environment lookup for settings and auth; null reads the process environment.</summary>
    public Func<string, string?>? Environment { get; init; }

    /// <summary>The extension host seams; null takes the store, prompt and log defaults.</summary>
    public ExtensionSeams? Extensions { get; init; }

    /// <summary>The global mcp.json path; null uses <c>~/.lunate/mcp.json</c>.</summary>
    public string? McpConfigPath { get; init; }

    /// <summary>The standard user-scope mcp.json path; null uses <c>~/.agents/mcp.json</c>.</summary>
    public string? McpAgentsConfigPath { get; init; }

    /// <summary>The user-profile root the skill user scope resolves under; null uses the profile.</summary>
    public string? HomeDirectory { get; init; }

    /// <summary>The clipboard source behind <c>@clipboard</c>; null uses the platform helper chain.</summary>
    public IClipboardImageSource? Clipboard { get; init; }

    /// <summary>The launch flags parsed once by the CLI; defaults stand in for direct calls.</summary>
    public LaunchOptions Launch { get; init; } = new();
}

/// <summary>
/// The print-mode frontend: one prompt, no interactive prompt, the final answer on stdout and
/// diagnostics on stderr. It consumes <see cref="AgentHarness.RunAsync"/> and adds no core
/// knowledge; see the print-mode spec for the stdout and exit-code contract.
/// </summary>
internal static class PrintMode
{
    /// <summary>Runs one prompt; returns the process exit code (0 stop, 1 error, 2 launch usage error or length/step limit, 130 cancelled).</summary>
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
            settings = settings with
            {
                Model = options.Launch.Model ?? settings.Model,
                Thinking = options.Launch.Thinking ?? settings.Thinking,
            };
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
        catch (UnknownModelException exception)
        {
            // An unknown --model is a launch usage error (design D1); an unknown configured
            // model stays a run error.
            interaction.Notify(new UserNotice(NoticeLevel.Error, exception.Message));
            return options.Launch.Model is null ? 1 : 2;
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
            List<ITool> inheritedTools = [];
            Workspace workspace;
            SkillCatalog skills;
            Session? session;
            try
            {
                workspace = new Workspace(options.WorkingDirectory ?? Environment.CurrentDirectory);
                (Session? selected, string? selectionError) = ResolveSession(options, workspace);
                if (selectionError is { } message)
                {
                    interaction.Notify(new UserNotice(NoticeLevel.Error, message));
                    return 2;
                }

                session = selected;
                IExtensionLog log = options.Extensions?.Log ?? new StderrExtensionLog(errors);
                if (options.Extensions?.NoExtensions is not true)
                {
                    extensions = await ComposeExtensionsAsync(
                            options.Extensions,
                            workspace,
                            catalog,
                            factory,
                            errors,
                            ct
                        )
                        .ConfigureAwait(false);
                }

                // Same discovery + index path as the interactive session; the terminal-less run
                // never approves repository skills, so a prompt decision stays untrusted.
                skills = SkillCatalog.Discover(
                    new SkillScanRequest(
                        workspace.WorktreeRoot,
                        workspace.RepoRoot,
                        options.HomeDirectory ?? DefaultHomeDirectory(),
                        extensions?.SkillContributions() ?? []
                    )
                );
                skills.SetProjectTrusted(
                    EnsureSkillsTrusted(skills, workspace, options.Extensions, interaction)
                );
                skills.BuildIndex(
                    workspace.WorktreeRoot,
                    extensions?.HookRunner.Options.ContextBudgetPerExtension
                        ?? new HookRunnerOptions().ContextBudgetPerExtension,
                    message => extensions?.Log.Warn(message)
                );
                foreach (string warning in skills.Warnings)
                {
                    interaction.Notify(new UserNotice(NoticeLevel.Warning, warning));
                }

                mcp = await McpComposition
                    .ComposeAsync(
                        workspace,
                        options.McpConfigPath,
                        options.McpAgentsConfigPath,
                        options.Extensions,
                        new NonInteractiveMcpConfigTrustPrompt(),
                        new NonInteractiveMcpServerApprovalPrompt(),
                        log,
                        notice => interaction.Notify(notice),
                        ct
                    )
                    .ConfigureAwait(false);

                List<ITool> tools = [];
                List<ITool> inherited = [];
                if (extensions is not null)
                {
                    // Extension tools first, in registration order, then the MCP tools (design D7).
                    tools.AddRange(extensions.Tools);
                    inherited.AddRange(extensions.Tools);
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
                    // A child run in another worktree may be offered the tools of servers
                    // explicitly marked workspace-independent (and only those); skipped
                    // collisions stay skipped.
                    foreach (
                        ITool tool in mcp.ListedToolsFrom(options => options.WorkspaceIndependent)
                    )
                    {
                        if (tools.Contains(tool))
                        {
                            inherited.Add(tool);
                        }
                    }

                    McpComposition.NotifyFailures(mcp, notice => interaction.Notify(notice));
                }

                mcpTools = tools;
                inheritedTools = inherited;
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

            using var workspaceServices = new WorkspaceServices(
                settings.Roslyn.MaxLoadedWorkspaces
            );
            // The bus exists only when the extension host composed; scan-only without extensions.
            if (extensions is not null)
            {
                workspaceServices.Connect(extensions.FileChanges);
            }

            AgentHarness harness;
            try
            {
                harness = CreateHarness(
                    options,
                    workspace,
                    session,
                    settings,
                    model,
                    catalog,
                    factory,
                    extensions,
                    mcpTools,
                    workspaceServices,
                    inheritedTools,
                    skills
                );
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return 130;
            }
            catch (ToolFilterException exception)
            {
                interaction.Notify(new UserNotice(NoticeLevel.Error, exception.Message));
                return 2;
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
                var imageInputs = new ImageInputResolver(
                    workspace.WorktreeRoot,
                    model,
                    options.Clipboard ?? new PlatformClipboardImageSource()
                );
                ImageInputResult input = await imageInputs.ResolveAsync(options.Prompt, ct);
                if (input.Outcome != ImageInputOutcome.Ready)
                {
                    if (input.Error is { } refusal)
                    {
                        interaction.Notify(new UserNotice(NoticeLevel.Error, refusal));
                    }

                    return 2;
                }

                await foreach (
                    AgentEvent agentEvent in harness.RunAsync(
                        new UserInput(input.Text, input.Images),
                        ct
                    )
                )
                {
                    if (options.Json)
                    {
                        // One JSON object per line, canonically '\n'-terminated: the --json
                        // stream is a scripting contract and must not depend on the platform.
                        output.Write(PrintEventJson.Serialize(agentEvent, catalog, model.Id));
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
        ModelCatalog catalog,
        IChatClientFactory factory,
        TextWriter errors,
        CancellationToken ct
    )
    {
        ExtensionSeams resolved = (seams ?? new ExtensionSeams()) with
        {
            Log = seams?.Log ?? new StderrExtensionLog(errors),
            ModelCatalog = seams?.ModelCatalog ?? catalog,
            ClientFactory = seams?.ClientFactory ?? factory,
        };
        return await ExtensionSession
            .StartAsync(workspace.Identity, resolved, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The non-interactive skills trust gate: trusted and new-worktree decisions pass (the latter
    /// records silently like extension trust); a prompt decision is refused — a terminal-less run
    /// never approves repository content — with one warning naming the fix.
    /// </summary>
    private static bool EnsureSkillsTrusted(
        SkillCatalog skills,
        Workspace workspace,
        ExtensionSeams? seams,
        PrintUserInteraction interaction
    )
    {
        IReadOnlyList<SkillDescriptor> project =
        [
            .. skills.Skills.Where(skill => skill is { Valid: true, Scope: SkillScope.Project }),
        ];
        if (project.Count == 0)
        {
            return true;
        }

        string identity = workspace.GitCommonDir ?? workspace.WorktreeRoot;
        var store = new ExtensionTrustStore(
            new ExtensionHostOptions
            {
                StorePath = seams?.StorePath ?? ExtensionHostOptions.Default.StorePath,
            }
        );
        string hash = SkillTrust.ComputeContentHash(
            project,
            workspace.RepoRoot ?? workspace.WorktreeRoot
        );
        switch (store.EvaluateSkills(identity, workspace.WorktreeRoot, hash))
        {
            case ExtensionTrustDecision.Trusted:
                return true;
            case ExtensionTrustDecision.NewWorktree:
                store.RecordSkillsTrusted(
                    identity,
                    workspace.WorktreeRoot,
                    hash,
                    DateTimeOffset.UtcNow
                );
                return true;
            default:
                interaction.Notify(
                    new UserNotice(
                        NoticeLevel.Warning,
                        "project skills were not trusted and stay out of the model index; run an interactive session or 'lunate trust' to approve them"
                    )
                );
                return false;
        }
    }

    private static string DefaultHomeDirectory() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// The launch selection: <c>--no-session</c> runs fileless, <c>--session</c> loads by id or
    /// path, <c>--continue</c> loads the newest session recorded for the workspace, and no flag
    /// creates a fresh session file as before. A selection that cannot resolve returns the error
    /// to report instead of starting a harness.
    /// </summary>
    private static (Session? Session, string? Error) ResolveSession(
        PrintModeOptions options,
        Workspace workspace
    )
    {
        LaunchOptions launch = options.Launch;
        if (launch.NoSession)
        {
            return (null, null);
        }

        if (launch.Session is null && !launch.Continue)
        {
            string directory =
                options.SessionDirectory ?? HarnessFactory.DefaultSessionDirectory(workspace);
            Session created = HarnessFactory.CreateSession(directory, workspace);
            SystemPrompt.Record(created, launch);
            return (created, null);
        }

        string sessionsRoot = options.SessionsRoot ?? SessionSelection.DefaultRoot;
        SessionChoice choice = launch.Session is { } value
            ? SessionSelection.FromValue(sessionsRoot, value)
            : SessionSelection.Continue(sessionsRoot, workspace.WorktreeRoot);
        if (choice.Error is { } error)
        {
            return (null, error);
        }

        try
        {
            return (Session.Load(choice.Path!), null);
        }
        catch (Exception exception)
            when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return (null, $"could not load session '{choice.Label}': {exception.Message}");
        }
    }

    private static AgentHarness CreateHarness(
        PrintModeOptions options,
        Workspace workspace,
        Session? session,
        AgentSettings settings,
        ModelInfo model,
        ModelCatalog catalog,
        IChatClientFactory factory,
        ExtensionSession? extensions,
        IReadOnlyList<ITool> mcpTools,
        WorkspaceServices workspaceServices,
        List<ITool> inheritedTools,
        SkillCatalog skills
    )
    {
        ToolRegistry tools = HarnessFactory.CreateTools(
            workspace,
            services: workspaceServices,
            skillReadAllowance: new SkillReadAllowance(skills.ReadRoots),
            log: extensions?.Log
        );
        foreach (ITool tool in mcpTools)
        {
            tools.Add(tool);
        }

        if (
            !HarnessFactory.TryFilterTools(tools, inheritedTools, options.Launch, out string? error)
        )
        {
            throw new ToolFilterException(error!);
        }

        if (session is { } active && skills.Index.Text.Length > 0)
        {
            RecordSkillsSection(active, skills.Index.Text);
        }

        var harnessOptions = new AgentHarnessOptions
        {
            SystemPrompt = SystemPrompt.Compose(
                workspace,
                [.. tools.Tools.Select(tool => tool.Name)],
                SystemPrompt.Directives(options.Launch, session)
            ),
            PromptSections =
                skills.Index.Text.Length > 0
                    ? [new AgentPromptSection("skills", skills.Index.Text)]
                    : null,
            WorkingDirectory = workspace.WorktreeRoot,
            ToolOutputLimit = settings.ToolOutputLimit,
            SubagentLimits = settings.Subagents,
            ThinkingLevel = settings.Thinking,
            Approver = new NonInteractiveApprover(settings.Approval, options.Yolo),
            Hooks = extensions?.Hooks,
            Session = session,
            FileChanges = (IFileChangeSink?)extensions?.FileChanges ?? NullFileChangeSink.Instance,
            Model = model,
            ModelCatalog = catalog,
            ModelId = model.Id,
            ModelRegistry = extensions?.ModelRegistry,
            WorkspaceTools = HarnessFactory.CreateWorkspaceTools(
                workspaceServices,
                inheritedTools,
                root => skills.ReadRootsFor(root),
                log: extensions?.Log
            ),
        };

        return new AgentHarness(factory.Create(model), tools, harnessOptions);
    }

    /// <summary>
    /// Records the skills index on the session, once per index: a resumed session keeps its last
    /// recorded section instead of gaining a duplicate (the interactive session's rule).
    /// </summary>
    private static void RecordSkillsSection(Session session, string text)
    {
        SessionPromptSectionEntry? last = session
            .Entries.OfType<SessionPromptSectionEntry>()
            .LastOrDefault(entry =>
                string.Equals(entry.Section, "skills", StringComparison.Ordinal)
            );
        if (last is null || !string.Equals(last.Text, text, StringComparison.Ordinal))
        {
            session.AppendPromptSection("skills", text);
        }
    }
}

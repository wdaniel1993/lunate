using Lunate.Ai;

namespace Lunate.Coding;

/// <summary>
/// The launch flags, parsed once before mode dispatch (design D1). The parser owns flag meaning;
/// the frontends consume the resolved values and resolve <see cref="Model"/> and
/// <see cref="Thinking"/> against the settings themselves.
/// </summary>
internal sealed record LaunchOptions
{
    /// <summary>Continue the newest session recorded for the working directory.</summary>
    public bool Continue { get; init; }

    /// <summary>Open the interactive session picker.</summary>
    public bool Resume { get; init; }

    /// <summary>Load one session by id or file path.</summary>
    public string? Session { get; init; }

    /// <summary>Run without writing a session file.</summary>
    public bool NoSession { get; init; }

    /// <summary>The model id for this run; null keeps the resolved setting.</summary>
    public string? Model { get; init; }

    /// <summary>The thinking level for this run; null keeps the resolved setting.</summary>
    public ThinkingLevel? Thinking { get; init; }

    /// <summary>The exact model-facing tool names to expose; null keeps the composed registry.</summary>
    public IReadOnlyList<string>? Tools { get; init; }

    /// <summary>
    /// The optional search tools to enable for this run. Null means the flag was absent (keep the
    /// file/environment list); an empty list means <c>--tools</c> named none.
    /// </summary>
    public IReadOnlyList<string>? ExtraTools { get; init; }

    /// <summary>Expose no tools; conflicts with <see cref="Tools"/>.</summary>
    public bool NoTools { get; init; }

    /// <summary>Replace the composed system prompt wholesale.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>Append a section after the composed system prompt.</summary>
    public string? AppendSystemPrompt { get; init; }

    /// <summary>Run one prompt in print mode.</summary>
    public bool Print { get; init; }

    /// <summary>Stream every print-mode event as JSON.</summary>
    public bool Json { get; init; }

    /// <summary>Approve every tool call for this run.</summary>
    public bool Yolo { get; init; }

    /// <summary>The print-mode prompt; null outside print mode.</summary>
    public string? Prompt { get; init; }

    /// <summary>Whether the invocation selects a session (including the no-session choice).</summary>
    public bool HasSelection => Continue || Resume || Session is not null || NoSession;

    /// <summary>
    /// Parses every launch flag. A failure writes <paramref name="error"/> without a
    /// <c>lunate:</c> prefix and leaves the mode undecided.
    /// </summary>
    internal static bool TryParse(
        IReadOnlyList<string> args,
        out LaunchOptions options,
        out string? error
    )
    {
        var print = false;
        var json = false;
        var yolo = false;
        var resume = false;
        var noSession = false;
        var noTools = false;
        var toolsGiven = false;
        string? session = null;
        string? model = null;
        string? tools = null;
        string? extraToolsFlag = null;
        string? systemPrompt = null;
        string? appendSystemPrompt = null;
        ThinkingLevel? thinking = null;
        List<string> positional = [];

        options = new LaunchOptions();
        error = null;
        for (var index = 0; index < args.Count; index++)
        {
            string token = args[index];
            string? inline = null;
            int equals = token.IndexOf('=');
            if (token.StartsWith("--", StringComparison.Ordinal) && equals >= 0)
            {
                inline = token[(equals + 1)..];
                token = token[..equals];
                if (IsBooleanFlag(token))
                {
                    error = $"option '{token}' does not take a value";
                    return false;
                }
            }

            switch (token)
            {
                case "-p" or "--print":
                    print = true;
                    break;
                case "--json":
                    json = true;
                    break;
                case "--yolo":
                    yolo = true;
                    break;
                case "-c" or "--continue":
                    options = options with { Continue = true };
                    break;
                case "-r" or "--resume":
                    resume = true;
                    break;
                case "--no-session":
                    noSession = true;
                    break;
                case "--no-tools":
                    noTools = true;
                    break;
                case "--session":
                    if (!TryValue(args, ref index, "--session", inline, out session, out error))
                    {
                        return false;
                    }
                    break;
                case "--model":
                    if (!TryValue(args, ref index, "--model", inline, out model, out error))
                    {
                        return false;
                    }
                    break;
                case "--thinking":
                    if (
                        !TryValue(
                            args,
                            ref index,
                            "--thinking",
                            inline,
                            out string level,
                            out error
                        ) || !TryThinking(level, out thinking, out error)
                    )
                    {
                        return false;
                    }
                    break;
                case "--tools":
                    if (!TryValue(args, ref index, "--tools", inline, out tools, out error))
                    {
                        return false;
                    }
                    toolsGiven = true;
                    break;
                case "--extra-tools":
                    if (
                        !TryValue(
                            args,
                            ref index,
                            "--extra-tools",
                            inline,
                            out extraToolsFlag,
                            out error
                        )
                    )
                    {
                        return false;
                    }
                    break;
                case "--system-prompt":
                    if (
                        !TryValue(
                            args,
                            ref index,
                            "--system-prompt",
                            inline,
                            out systemPrompt,
                            out error
                        )
                    )
                    {
                        return false;
                    }
                    break;
                case "--append-system-prompt":
                    if (
                        !TryValue(
                            args,
                            ref index,
                            "--append-system-prompt",
                            inline,
                            out appendSystemPrompt,
                            out error
                        )
                    )
                    {
                        return false;
                    }
                    break;
                default:
                    if (args[index].StartsWith('-'))
                    {
                        error = $"unknown option '{args[index]}'";
                        return false;
                    }
                    if (string.IsNullOrWhiteSpace(args[index]))
                    {
                        error = "the prompt cannot be empty";
                        return false;
                    }
                    positional.Add(args[index]);
                    break;
            }
        }

        if (
            !TryTools(tools, out IReadOnlyList<string>? toolNames, out error)
            || !TryExtraTools(extraToolsFlag, out IReadOnlyList<string>? extraTools, out error)
        )
        {
            return false;
        }

        var selection = new List<string>();
        if (options.Continue)
        {
            selection.Add("--continue");
        }
        if (resume)
        {
            selection.Add("--resume");
        }
        if (session is not null)
        {
            selection.Add("--session");
        }
        if (noSession)
        {
            selection.Add("--no-session");
        }

        if (selection.Count > 1)
        {
            error = $"{string.Join(", ", selection)} are mutually exclusive; use only one of them";
            return false;
        }

        if (toolsGiven && noTools)
        {
            error = "--tools and --no-tools are mutually exclusive; use only one of them";
            return false;
        }

        if (print && positional.Count == 0)
        {
            error = "print mode needs one prompt";
            return false;
        }

        if (print && positional.Count > 1)
        {
            error = "print mode takes exactly one prompt";
            return false;
        }

        if (!print && positional.Count > 0)
        {
            error = $"unexpected argument '{positional[0]}'; interactive mode takes flags only";
            return false;
        }

        options = options with
        {
            Resume = resume,
            Session = session,
            NoSession = noSession,
            Model = model,
            Thinking = thinking,
            Tools = toolNames,
            ExtraTools = extraTools,
            NoTools = noTools,
            SystemPrompt = systemPrompt,
            AppendSystemPrompt = appendSystemPrompt,
            Print = print,
            Json = json,
            Yolo = yolo,
            Prompt = print ? positional[0] : null,
        };
        return true;
    }

    /// <summary>
    /// Accepts a value inline (<c>--flag=value</c>) or as the next token. Space-separated values
    /// starting with <c>-</c> are rejected; the inline form is the escape for such values.
    /// </summary>
    private static bool TryValue(
        IReadOnlyList<string> args,
        ref int index,
        string flag,
        string? inline,
        out string value,
        out string? error
    )
    {
        error = null;
        if (inline is not null)
        {
            if (string.IsNullOrWhiteSpace(inline))
            {
                value = string.Empty;
                error = $"option '{flag}' expects a value";
                return false;
            }

            value = inline;
            return true;
        }

        value = string.Empty;
        if (
            index + 1 >= args.Count
            || string.IsNullOrWhiteSpace(args[index + 1])
            || args[index + 1].StartsWith('-')
        )
        {
            error = $"option '{flag}' expects a value";
            return false;
        }

        value = args[++index];
        return true;
    }

    /// <summary>A flag without a value rejects the inline assignment form.</summary>
    private static bool IsBooleanFlag(string token) =>
        token
            is "-p"
                or "--print"
                or "--json"
                or "--yolo"
                or "-c"
                or "--continue"
                or "-r"
                or "--resume"
                or "--no-session"
                or "--no-tools";

    private static bool TryThinking(string value, out ThinkingLevel? thinking, out string? error)
    {
        error = null;
        if (!SettingsStore.TryParseThinking(value, out ThinkingLevel parsed))
        {
            thinking = null;
            error = $"'--thinking' must be 'off', 'low', 'medium' or 'high', but was '{value}'";
            return false;
        }

        thinking = parsed;
        return true;
    }

    /// <summary>
    /// Parses <c>--tools</c> into the registry filter. An optional search tool name is refused
    /// here — it belongs to <c>--extra-tools</c> — so the flag keeps exactly one meaning.
    /// </summary>
    private static bool TryTools(string? value, out IReadOnlyList<string>? tools, out string? error)
    {
        error = null;
        tools = null;
        if (value is null)
        {
            return true;
        }

        List<string> names = [];
        foreach (string part in value.Split(','))
        {
            string name = part.Trim();
            if (name.Length == 0)
            {
                error = "'--tools' expects a comma-separated list of tool names";
                return false;
            }

            if (OptionalTools.IsName(name))
            {
                error = $"'{name}' is an optional search tool; name it with --extra-tools";
                return false;
            }

            names.Add(name);
        }

        tools = names.Count == 0 ? null : names;
        return true;
    }

    /// <summary>Parses <c>--extra-tools</c> into the search tools to enable beside the core set.</summary>
    private static bool TryExtraTools(
        string? value,
        out IReadOnlyList<string>? extraTools,
        out string? error
    )
    {
        error = null;
        extraTools = null;
        if (value is null)
        {
            return true;
        }

        List<string> names = [];
        foreach (string part in value.Split(','))
        {
            string name = part.Trim();
            if (name.Length == 0)
            {
                error = "'--extra-tools' expects a comma-separated list of search tool names";
                return false;
            }

            if (!OptionalTools.IsName(name))
            {
                error =
                    $"'{name}' is not an optional search tool; --extra-tools accepts {OptionalTools.Accepted}";
                return false;
            }

            if (!names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }
        }

        extraTools = names;
        return true;
    }
}

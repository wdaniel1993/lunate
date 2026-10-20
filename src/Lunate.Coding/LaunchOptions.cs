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
        string? session = null;
        string? model = null;
        string? tools = null;
        string? systemPrompt = null;
        string? appendSystemPrompt = null;
        ThinkingLevel? thinking = null;
        List<string> positional = [];

        options = new LaunchOptions();
        error = null;
        for (var index = 0; index < args.Count; index++)
        {
            string arg = args[index];
            switch (arg)
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
                    if (!TryValue(args, ref index, "--session", out session, out error))
                    {
                        return false;
                    }
                    break;
                case "--model":
                    if (!TryValue(args, ref index, "--model", out model, out error))
                    {
                        return false;
                    }
                    break;
                case "--thinking":
                    if (
                        !TryValue(args, ref index, "--thinking", out string level, out error)
                        || !TryThinking(level, out thinking, out error)
                    )
                    {
                        return false;
                    }
                    break;
                case "--tools":
                    if (!TryValue(args, ref index, "--tools", out tools, out error))
                    {
                        return false;
                    }
                    break;
                case "--system-prompt":
                    if (!TryValue(args, ref index, "--system-prompt", out systemPrompt, out error))
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
                            out appendSystemPrompt,
                            out error
                        )
                    )
                    {
                        return false;
                    }
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        error = $"unknown option '{arg}'";
                        return false;
                    }
                    if (string.IsNullOrWhiteSpace(arg))
                    {
                        error = "the prompt cannot be empty";
                        return false;
                    }
                    positional.Add(arg);
                    break;
            }
        }

        if (!TryTools(tools, out IReadOnlyList<string>? toolNames, out error))
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

        if (toolNames is not null && noTools)
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

    private static bool TryValue(
        IReadOnlyList<string> args,
        ref int index,
        string flag,
        out string value,
        out string? error
    )
    {
        value = string.Empty;
        error = null;
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

            names.Add(name);
        }

        tools = names;
        return true;
    }
}

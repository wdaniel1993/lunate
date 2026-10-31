using System.Text.Json;
using Lunate.Agent;
using Lunate.Ai;

namespace Lunate.Coding;

/// <summary>
/// The calls the approval panel must never wave through: the ones that can approve other code
/// (<c>lunate trust</c>, <c>lunate install</c>, <c>lunate uninstall</c>, through bash) and the ones
/// that write the state root the approvals live in. Classified in one place so every approver
/// agrees, and consulted before a policy or an "always this session" grant — both are exactly what
/// such a call can rewrite.
/// <para>
/// This is defence in depth, not a boundary. It reads one command line: a process with the user's
/// shell can spell the same write another way (a script, an encoded string, a child process), and
/// under <c>--yolo</c> no panel exists at all. What it buys is that the default configuration keeps
/// showing these calls after the tool itself was granted for the session.
/// </para>
/// </summary>
internal static partial class HighRiskCall
{
    private static readonly string[] StateCommands = ["trust", "install", "uninstall"];

    /// <summary>Whether this call must always be shown before it runs.</summary>
    internal static bool Is(ApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Is(
            request.Tool.Name,
            request.Args,
            request.WorkspaceRoot ?? Environment.CurrentDirectory,
            LunateHome.Store,
            LunateHome.Profile
        );
    }

    /// <summary>
    /// The classification with its roots supplied: <paramref name="workspaceRoot"/> resolves a
    /// relative path the way the bash tool runs it, <paramref name="storeRoot"/> is the state root
    /// <see cref="LunateHome.Store"/> resolves, and <paramref name="homeRoot"/> expands <c>~</c>.
    /// </summary>
    internal static bool Is(
        string toolName,
        JsonElement args,
        string workspaceRoot,
        string storeRoot,
        string homeRoot
    )
    {
        if (args.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        return toolName switch
        {
            BashTool.ToolName => RunsStateCommand(args)
                || NamesStateRoot(args, workspaceRoot, storeRoot, homeRoot)
                || MountsStateRoot(args, workspaceRoot, storeRoot, homeRoot),
            WriteTool.ToolName or EditTool.ToolName => WritesStateRoot(
                args,
                workspaceRoot,
                storeRoot,
                homeRoot
            ),
            _ => false,
        };
    }

    /// <summary>
    /// Any word that is the <c>lunate</c> executable followed by a state-changing subcommand.
    /// Flags between the two are skipped (<c>lunate --no-extensions trust</c>). False positives
    /// are acceptable here — the cost is one more panel, not a wrong answer.
    /// </summary>
    private static bool RunsStateCommand(JsonElement args)
    {
        if (
            !args.TryGetProperty("command", out JsonElement element)
            || element.ValueKind is not JsonValueKind.String
        )
        {
            return false;
        }

        IReadOnlyList<string> words = Words(element.GetString() ?? string.Empty);
        for (var index = 0; index < words.Count; index++)
        {
            if (!IsLunate(words[index]))
            {
                continue;
            }

            for (int next = index + 1; next < words.Count; next++)
            {
                if (words[next].StartsWith('-'))
                {
                    continue;
                }

                if (StateCommands.Contains(words[next], StringComparer.Ordinal))
                {
                    return true;
                }

                break;
            }
        }

        return false;
    }

    /// <summary>Any word that resolves inside the state root: <c>~</c>, <c>$HOME</c> and
    /// <c>$LUNATE_HOME</c> expand to the same roots the product uses.</summary>
    private static bool NamesStateRoot(
        JsonElement args,
        string workspaceRoot,
        string storeRoot,
        string homeRoot
    )
    {
        if (
            !args.TryGetProperty("command", out JsonElement element)
            || element.ValueKind is not JsonValueKind.String
        )
        {
            return false;
        }

        foreach (string word in Words(element.GetString() ?? string.Empty))
        {
            if (ResolvesInsideStore(word, workspaceRoot, storeRoot, homeRoot))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The <c>write</c> and <c>edit</c> tools' <c>path</c> argument.</summary>
    private static bool WritesStateRoot(
        JsonElement args,
        string workspaceRoot,
        string storeRoot,
        string homeRoot
    )
    {
        if (
            !args.TryGetProperty("path", out JsonElement element)
            || element.ValueKind is not JsonValueKind.String
        )
        {
            return false;
        }

        return ResolvesInsideStore(
            element.GetString() ?? string.Empty,
            workspaceRoot,
            storeRoot,
            homeRoot
        );
    }

    private static bool ResolvesInsideStore(
        string word,
        string workspaceRoot,
        string storeRoot,
        string homeRoot
    )
    {
        string expanded = Expand(word, storeRoot, homeRoot);
        try
        {
            return LunateHome.IsInside(Path.GetFullPath(expanded, workspaceRoot), storeRoot);
        }
        catch (Exception exception)
            when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A word the file system cannot even resolve cannot name a path we would protect.
            return false;
        }
    }

    /// <summary>The shell's root variables, expanded the way the shell would.</summary>
    private static string Expand(string word, string storeRoot, string homeRoot)
    {
        string expanded = word.Replace("${LUNATE_HOME}", storeRoot, StringComparison.Ordinal)
            .Replace("$LUNATE_HOME", storeRoot, StringComparison.Ordinal)
            .Replace("${HOME}", homeRoot, StringComparison.Ordinal)
            .Replace("$HOME", homeRoot, StringComparison.Ordinal);

        if (expanded == "~")
        {
            return homeRoot;
        }

        return
            expanded.StartsWith("~/", StringComparison.Ordinal)
            || expanded.StartsWith("~\\", StringComparison.Ordinal)
            ? homeRoot + expanded[1..]
            : expanded;
    }

    private static bool IsLunate(string word) =>
        Path.GetFileName(word)
            .Equals(
                "lunate",
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal
            ) || Path.GetFileName(word).Equals("lunate.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Splits one command line into words: whitespace, quotes and the shell's metacharacters
    /// separate them, so <c>a &amp;&amp; b</c>, <c>a;b</c> and <c>a|b</c> are all three words of
    /// the same kind. It is not a shell parser — it only has to find the words that matter.
    /// </summary>
    private static IReadOnlyList<string> Words(string command)
    {
        List<string> words = [];
        var current = new System.Text.StringBuilder();
        foreach (char character in command)
        {
            if (
                char.IsWhiteSpace(character)
                || character is ';' or '|' or '&' or '(' or ')' or '<' or '>' or '\'' or '"' or '`'
            )
            {
                if (current.Length > 0)
                {
                    words.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        return words;
    }
}

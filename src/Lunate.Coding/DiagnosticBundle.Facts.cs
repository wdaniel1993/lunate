using System.Globalization;
using System.Text;
using Lunate.Agent;
using Lunate.Ai;

namespace Lunate.Coding;

internal static partial class DiagnosticBundle
{
    /// <summary>
    /// The state root paths a bundle names, in listing order. A directory ends in <c>/</c>. Only
    /// existence is read: nothing under the root is opened.
    /// </summary>
    private static readonly string[] StateRootPaths =
    [
        "auth.json",
        "settings.json",
        "models.json",
        "models.catalog.json",
        "history",
        "trust.json",
        "trust.lock",
        "sessions/",
        "exports/",
        "diagnostics/",
        "extensions/",
        "extensions-settings/",
        "extensions-secrets/",
        "cache/",
        "recordings/",
        "packages/",
        "skills/",
    ];

    private static void AppendSession(StringBuilder text, DiagnosticBundleInput input)
    {
        Section(text, "session");
        text.Line("file: " + (input.SessionPath ?? "(none)"));
        text.Line("id: " + (input.SessionId ?? "(none)"));
        text.Line("title: " + (input.SessionTitle ?? "(none)"));
        text.Line("entries: " + input.SessionEntries.ToString(CultureInfo.InvariantCulture));
        text.Line("working directory: " + (input.WorkingDirectory ?? "(unknown)"));
        text.Line("git branch: " + (input.GitBranch ?? "(none)"));
    }

    private static void AppendStateRoot(StringBuilder text, DiagnosticBundleInput input)
    {
        Section(text, "state root");
        text.Line("root: " + input.StateRoot);
        text.Line("LUNATE_HOME: " + (input.HomeConfigured ? "set (the root above)" : "unset"));
        text.Line("paths:");
        foreach (string relative in StateRootPaths)
        {
            string path = Path.Combine(input.StateRoot, relative);
            bool present = relative.EndsWith('/') ? Directory.Exists(path) : File.Exists(path);
            text.Line("  " + relative.PadRight(26) + (present ? "present" : "missing"));
        }

        text.Line("top-level entries (names only, no contents):");
        string[] entries = TopLevelEntries(input.StateRoot);
        if (entries.Length == 0)
        {
            text.Line("  (none)");
            return;
        }

        foreach (string entry in entries)
        {
            text.Line("  " + entry);
        }
    }

    private static string[] TopLevelEntries(string root)
    {
        try
        {
            return
            [
                .. Directory
                    .GetFileSystemEntries(root)
                    .Select(Path.GetFileName)
                    .OfType<string>()
                    .Order(StringComparer.Ordinal),
            ];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void AppendConfig(StringBuilder text, DiagnosticBundleInput input)
    {
        Section(text, "config");
        AgentSettings settings = input.Settings;
        text.Line("model (configured): " + (settings.Model ?? "(catalog default)"));
        text.Line("approval: " + Lower(settings.Approval));
        text.Line("tool details: " + Lower(settings.ToolDetails));
        text.Line("tool output limit: " + Number(settings.ToolOutputLimit));
        text.Line("startup header: " + Lower(settings.StartupHeader));
        text.Line("thinking: " + Lower(input.Thinking));
        text.Line("thinking full: " + Lower(settings.ThinkingFull));
        text.Line(
            "favorites: "
                + (settings.Favorites.Count == 0 ? "(none)" : string.Join(", ", settings.Favorites))
        );
        text.Line(
            "extra tools: "
                + (
                    settings.Tools.Extra.Count == 0
                        ? "(none)"
                        : string.Join(", ", settings.Tools.Extra)
                )
        );
        SubagentLimits limits = settings.Subagents;
        text.Line(
            "subagents: concurrency "
                + Number(limits.Concurrency)
                + ", token budget "
                + Number(limits.TokenBudget)
                + ", tree token budget "
                + Number(limits.TreeTokenBudget)
        );
        text.Line("roslyn: max loaded workspaces " + Number(settings.Roslyn.MaxLoadedWorkspaces));
        text.Line(
            "auth keys: "
                + (input.AuthKeys.Count == 0 ? "(none)" : string.Join(", ", input.AuthKeys))
        );
        text.Line("custom providers:");
        if (settings.Providers.Count == 0)
        {
            text.Line("  (none)");
            return;
        }

        foreach (CustomProvider provider in settings.Providers)
        {
            text.Line(
                "  - id="
                    + provider.Id
                    + " catalog="
                    + provider.CatalogName
                    + " model="
                    + provider.Model
                    + " base="
                    + Endpoint(provider.BaseUrl)
            );
        }
    }

    private static void AppendModel(StringBuilder text, DiagnosticBundleInput input)
    {
        Section(text, "model");
        ModelInfo model = input.Model;
        text.Line("id: " + model.Id);
        text.Line("provider: " + model.Provider);
        text.Line("source: " + input.ModelSource);
        text.Line("endpoint: " + Endpoint(model.Endpoint));
        text.Line("context window: " + Number(model.ContextWindow));
        text.Line("supports tools: " + Lower(model.SupportsTools));
        text.Line("supports vision: " + Lower(model.SupportsVision));
        text.Line("thinking: " + Lower(input.Thinking));
        text.Line("last routed model: " + (input.ModelRoute ?? "(none)"));
        if (model.WireId is { Length: > 0 } wireId)
        {
            text.Line("wire id: " + wireId);
        }
    }
}

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lunate.Extensibility;

namespace Lunate.Coding;

/// <summary>
/// The <c>/extensions</c> view's formatting (design D2/D5): one segment per discovered extension
/// ordered failed → trust-denied → loaded, then by id — problems first. Loaded segments carry the
/// registered contribution counts (a field differing from the manifest's declared list reads
/// <c>(N declared)</c>) and the load time, marked <c>(slow)</c> above the threshold; trust-denied
/// and failed segments carry the manifest's declared counts, and a failed message is truncated to
/// its first sentence with a <c> — see the log</c> pointer. The interactive command wraps the
/// segments in the notice prefix; the CLI prints one line per segment and the same data as JSON.
/// Everything formats culture-invariantly; the view never prints settings values, secrets or
/// environment values.
/// </summary>
internal static class ExtensionsView
{
    /// <summary>The slow-load threshold in milliseconds (design D2, pinned).</summary>
    private const double SlowThresholdMs = 500;

    /// <summary>The interactive empty view.</summary>
    internal const string NoneText = "extensions: none configured";

    /// <summary>The <c>--no-extensions</c> view (interactive and CLI).</summary>
    internal const string DisabledText = "extensions disabled (--no-extensions)";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
    };

    /// <summary>
    /// The ordered segments, one per descriptor; <paramref name="registeredCounts"/> supplies the
    /// registered counts per extension id, and <paramref name="cliMeasured"/> labels the load time
    /// <c>(CLI-measured)</c> (this run's measurement, not a session's).
    /// </summary>
    internal static IReadOnlyList<string> Segments(
        IReadOnlyList<ExtensionDescriptor> descriptors,
        IReadOnlyList<ExtensionLoadResult> results,
        Func<string, ExtensionCounts> registeredCounts,
        bool cliMeasured = false
    )
    {
        ArgumentNullException.ThrowIfNull(registeredCounts);
        return
        [
            .. Ordered(descriptors, results)
                .Select(entry =>
                    Segment(entry.Descriptor, entry.Result, registeredCounts, cliMeasured)
                ),
        ];
    }

    /// <summary>
    /// The <c>--json</c> contract: the same data, in the same order, as an array of
    /// <c>{id, version, scope, state, loadMs?, slow?, tools, commands, hooks, services}</c>
    /// objects (load time and slow marker only on loaded extensions).
    /// </summary>
    internal static string Json(
        IReadOnlyList<ExtensionDescriptor> descriptors,
        IReadOnlyList<ExtensionLoadResult> results,
        Func<string, ExtensionCounts> registeredCounts
    )
    {
        ArgumentNullException.ThrowIfNull(registeredCounts);
        var array = new JsonArray();
        foreach (
            (ExtensionDescriptor descriptor, ExtensionLoadResult result) in Ordered(
                descriptors,
                results
            )
        )
        {
            var node = new JsonObject
            {
                ["id"] = descriptor.Id,
                ["version"] = descriptor.Version,
                ["scope"] = ScopeText(descriptor.Scope),
                ["state"] = StateText(result.State),
            };
            if (result.State == ExtensionLoadState.Loaded)
            {
                node["loadMs"] = (long)Math.Round(result.Elapsed.TotalMilliseconds);
                if (IsSlow(result.Elapsed))
                {
                    node["slow"] = true;
                }
            }

            ExtensionCounts counts =
                result.State == ExtensionLoadState.Loaded
                    ? registeredCounts(descriptor.Id)
                    : Declared(descriptor);
            node["tools"] = counts.Tools;
            node["commands"] = counts.Commands;
            node["hooks"] = counts.Hooks;
            node["services"] = counts.Services;
            array.Add(node);
        }

        return array.ToJsonString(JsonOptions);
    }

    private static IEnumerable<(
        ExtensionDescriptor Descriptor,
        ExtensionLoadResult Result
    )> Ordered(
        IReadOnlyList<ExtensionDescriptor> descriptors,
        IReadOnlyList<ExtensionLoadResult> results
    )
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(results);
        Dictionary<string, ExtensionLoadResult> byId = new(StringComparer.Ordinal);
        foreach (ExtensionLoadResult result in results)
        {
            byId[result.Id] = result;
        }

        return descriptors
            .Select(descriptor =>
                (
                    Descriptor: descriptor,
                    Result: byId.TryGetValue(descriptor.Id, out ExtensionLoadResult? result)
                        ? result
                        : throw new InvalidOperationException(
                            $"extension '{descriptor.Id}' has no load result."
                        )
                )
            )
            .OrderBy(entry => Rank(entry.Result.State))
            .ThenBy(entry => entry.Descriptor.Id, StringComparer.Ordinal);
    }

    private static string Segment(
        ExtensionDescriptor descriptor,
        ExtensionLoadResult result,
        Func<string, ExtensionCounts> registeredCounts,
        bool cliMeasured
    )
    {
        string head = FormattableString.Invariant(
            $"{descriptor.Id} {descriptor.Version} ({ScopeText(descriptor.Scope)})"
        );
        return result.State switch
        {
            ExtensionLoadState.Loaded => head
                + FormattableString.Invariant(
                    $" loaded in {Milliseconds(result.Elapsed)}{LoadMarkers(result.Elapsed, cliMeasured)}"
                )
                + RegisteredCounts(descriptor, registeredCounts(descriptor.Id)),
            ExtensionLoadState.TrustDenied => head + " trust-denied" + DeclaredCounts(descriptor),
            _ => head + " failed: " + FailedMessage(result.Message) + DeclaredCounts(descriptor),
        };
    }

    /// <summary>The registered counts; a field differing from the manifest's list reads <c>(N declared)</c>.</summary>
    private static string RegisteredCounts(
        ExtensionDescriptor descriptor,
        ExtensionCounts counts
    ) =>
        FormattableString.Invariant(
            $", tools {Field(counts.Tools, descriptor.Manifest.Tools.Count)}, commands {Field(counts.Commands, descriptor.Manifest.Commands.Count)}, hooks {Field(counts.Hooks, descriptor.Manifest.Hooks.Count)}, services {Field(counts.Services, descriptor.Manifest.Services.Count)}"
        );

    /// <summary>The manifest's declared counts (trust-denied and failed extensions load nothing).</summary>
    private static string DeclaredCounts(ExtensionDescriptor descriptor)
    {
        ExtensionCounts declared = Declared(descriptor);
        return FormattableString.Invariant(
            $", tools {declared.Tools}, commands {declared.Commands}, hooks {declared.Hooks}, services {declared.Services}"
        );
    }

    private static ExtensionCounts Declared(ExtensionDescriptor descriptor) =>
        new(
            descriptor.Manifest.Tools.Count,
            descriptor.Manifest.Commands.Count,
            descriptor.Manifest.Hooks.Count,
            descriptor.Manifest.Services.Count
        );

    private static string Field(int registered, int declared) =>
        registered == declared
            ? registered.ToString(CultureInfo.InvariantCulture)
            : FormattableString.Invariant($"{registered} ({declared} declared)");

    private static string LoadMarkers(TimeSpan elapsed, bool cliMeasured)
    {
        List<string> markers = [];
        if (cliMeasured)
        {
            markers.Add("CLI-measured");
        }

        if (IsSlow(elapsed))
        {
            markers.Add("slow");
        }

        return markers.Count == 0 ? string.Empty : " (" + string.Join(", ", markers) + ")";
    }

    private static bool IsSlow(TimeSpan elapsed) => elapsed.TotalMilliseconds > SlowThresholdMs;

    private static string Milliseconds(TimeSpan elapsed) =>
        elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// The failure line: the message's first sentence (up to and including its first <c>.</c>)
    /// plus the <c> — see the log</c> pointer when the message was truncated; a message without a
    /// sentence end passes through whole.
    /// </summary>
    private static string FailedMessage(string? message)
    {
        string text = message ?? string.Empty;
        int end = text.IndexOf('.', StringComparison.Ordinal);
        if (end < 0)
        {
            return text;
        }

        string sentence = text[..(end + 1)];
        return end == text.Length - 1 ? sentence : sentence + " — see the log";
    }

    private static string ScopeText(ExtensionScope scope) =>
        scope == ExtensionScope.Project ? "project" : "global";

    private static string StateText(ExtensionLoadState state) =>
        state switch
        {
            ExtensionLoadState.Loaded => "loaded",
            ExtensionLoadState.TrustDenied => "trust-denied",
            _ => "failed",
        };

    private static int Rank(ExtensionLoadState state) =>
        state switch
        {
            ExtensionLoadState.Failed => 0,
            ExtensionLoadState.TrustDenied => 1,
            _ => 2,
        };
}

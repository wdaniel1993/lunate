using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lunate.Extensibility;

namespace Lunate.Coding;

/// <summary>
/// The <c>/extensions</c> view's formatting (design D2/D5): one segment per discovered extension
/// ordered failed → trust-denied → loaded, then by id — problems first. Loaded segments carry the
/// registered contribution counts (a field differing from the manifest's declared list reads
/// <c>(N declared)</c>) and the load time, marked <c>(slow)</c> above the threshold, plus — when
/// something crossed the hook slow threshold — the slowest such hook as
/// <c>hook &lt;Hook&gt; max &lt;ms&gt; ms, &lt;slow&gt;/&lt;calls&gt; slow</c> and, for a remote
/// extension, its most recent start as <c>start &lt;ms&gt; ms</c>; trust-denied and failed
/// segments carry the manifest's declared counts, and a failed message is truncated to its first
/// sentence with a <c> — see the log</c> pointer. The interactive command wraps the segments in
/// the notice prefix; the CLI prints one line per segment and the same data as JSON. Everything
/// formats culture-invariantly; the view never prints settings values, secrets or environment
/// values.
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
    /// <c>(CLI-measured)</c> (this run's measurement, not a session's). The optional
    /// <paramref name="hookTimings"/> and <paramref name="remoteStart"/> supply the session's
    /// per-hook aggregates and a remote extension's most recent start (the register-only CLI path
    /// passes neither).
    /// </summary>
    internal static IReadOnlyList<string> Segments(
        IReadOnlyList<ExtensionDescriptor> descriptors,
        IReadOnlyList<ExtensionLoadResult> results,
        Func<string, ExtensionCounts> registeredCounts,
        bool cliMeasured = false,
        Func<string, IReadOnlyList<HookTimings>>? hookTimings = null,
        Func<string, TimeSpan?>? remoteStart = null
    )
    {
        ArgumentNullException.ThrowIfNull(registeredCounts);
        return
        [
            .. Ordered(descriptors, results)
                .Select(entry =>
                    Segment(
                        entry.Descriptor,
                        entry.Result,
                        registeredCounts,
                        cliMeasured,
                        hookTimings,
                        remoteStart
                    )
                ),
        ];
    }

    /// <summary>
    /// The <c>--json</c> contract: the same data, in the same order, as an array of
    /// <c>{id, version, scope, state, loadMs?, slow?, tools, commands, hooks, services,
    /// hookTimings?, startMs?}</c> objects (load time and slow marker only on loaded extensions;
    /// <c>hookTimings</c> — one <c>{hook, calls, totalMs, lastMs, maxMs, slowCount, completed,
    /// timedOut, cancelled}</c> per timed hook — only on loaded extensions that dispatched, and
    /// <c>startMs</c> only when a remote extension's most recent start is known).
    /// </summary>
    internal static string Json(
        IReadOnlyList<ExtensionDescriptor> descriptors,
        IReadOnlyList<ExtensionLoadResult> results,
        Func<string, ExtensionCounts> registeredCounts,
        Func<string, IReadOnlyList<HookTimings>>? hookTimings = null,
        Func<string, TimeSpan?>? remoteStart = null
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
                node["loadMs"] = Milliseconds(result.Elapsed);
                if (IsSlow(result.Elapsed))
                {
                    node["slow"] = true;
                }

                if (hookTimings?.Invoke(descriptor.Id) is { Count: > 0 } timed)
                {
                    var timings = new JsonArray();
                    foreach (HookTimings entry in timed)
                    {
                        timings.Add(
                            new JsonObject
                            {
                                ["hook"] = entry.Hook,
                                ["calls"] = entry.Calls,
                                ["totalMs"] = Milliseconds(entry.TotalMs),
                                ["lastMs"] = Milliseconds(entry.LastMs),
                                ["maxMs"] = Milliseconds(entry.MaxMs),
                                ["slowCount"] = entry.SlowCount,
                                ["completed"] = entry.Completed,
                                ["timedOut"] = entry.TimedOut,
                                ["cancelled"] = entry.Cancelled,
                            }
                        );
                    }

                    node["hookTimings"] = timings;
                }
            }

            if (remoteStart?.Invoke(descriptor.Id) is { } start)
            {
                node["startMs"] = Milliseconds(start);
            }

            ExtensionCounts counts =
                result.State == ExtensionLoadState.Loaded
                    ? registeredCounts(descriptor.Id)
                    : Declared(descriptor);
            node["tools"] = counts.Tools;
            node["commands"] = counts.Commands;
            if (
                result.State == ExtensionLoadState.Loaded
                && counts.DeclaredNotProvidedCommands.Count > 0
            )
            {
                var names = new JsonArray();
                foreach (string name in counts.DeclaredNotProvidedCommands)
                {
                    names.Add(name);
                }

                node["commandsDeclaredNotProvided"] = names;
            }

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
        bool cliMeasured,
        Func<string, IReadOnlyList<HookTimings>>? hookTimings,
        Func<string, TimeSpan?>? remoteStart
    )
    {
        string head = FormattableString.Invariant(
            $"{descriptor.Id} {descriptor.Version} ({ScopeText(descriptor.Scope)})"
        );
        return result.State switch
        {
            ExtensionLoadState.Loaded => head
                + FormattableString.Invariant(
                    $" loaded in {Milliseconds(result.Elapsed)} ms{LoadMarkers(result.Elapsed, cliMeasured)}"
                )
                + RegisteredCounts(descriptor, registeredCounts(descriptor.Id))
                + SlowestHook(hookTimings?.Invoke(descriptor.Id))
                + StartMarker(remoteStart?.Invoke(descriptor.Id)),
            ExtensionLoadState.TrustDenied => head + " trust-denied" + DeclaredCounts(descriptor),
            _ => head + " failed: " + FailedMessage(result.Message) + DeclaredCounts(descriptor),
        };
    }

    /// <summary>
    /// The slowest hook that crossed the slow threshold, as
    /// <c>, hook &lt;Hook&gt; max &lt;ms&gt; ms, &lt;slowCount&gt;/&lt;calls&gt; slow</c>; empty
    /// when no hook crossed, so a quiet extension stays one line.
    /// </summary>
    private static string SlowestHook(IReadOnlyList<HookTimings>? timings)
    {
        HookTimings? slowest = timings
            ?.Where(entry => entry.SlowCount > 0)
            .OrderByDescending(entry => entry.MaxMs)
            .ThenBy(entry => entry.Hook, StringComparer.Ordinal)
            .FirstOrDefault();
        return slowest is null
            ? string.Empty
            : FormattableString.Invariant(
                $", hook {slowest.Hook} max {Milliseconds(slowest.MaxMs)} ms, {slowest.SlowCount}/{slowest.Calls} slow"
            );
    }

    /// <summary>A remote extension's most recent start as <c>, start &lt;ms&gt; ms</c>; empty when none.</summary>
    private static string StartMarker(TimeSpan? start) =>
        start is { } duration
            ? FormattableString.Invariant($", start {Milliseconds(duration)} ms")
            : string.Empty;

    /// <summary>The registered counts; a field differing from the manifest's list reads <c>(N declared)</c>.</summary>
    private static string RegisteredCounts(
        ExtensionDescriptor descriptor,
        ExtensionCounts counts
    ) =>
        FormattableString.Invariant(
            $", tools {Field(counts.Tools, descriptor.Manifest.Tools.Count)}, commands {CommandsField(descriptor, counts)}, hooks {Field(counts.Hooks, descriptor.Manifest.Hooks.Count)}, services {Field(counts.Services, descriptor.Manifest.Services.Count)}"
        );

    /// <summary>
    /// The commands field: the registered count against the declared list, with the declared names
    /// no source provided called out as <c>declared, not provided</c> and a name another source owns
    /// as <c>shadowed by &lt;id&gt;</c> (the registry's first-source-wins rule makes it not live).
    /// </summary>
    private static string CommandsField(ExtensionDescriptor descriptor, ExtensionCounts counts)
    {
        List<string> notes = [];
        if (counts.DeclaredNotProvidedCommands.Count > 0)
        {
            notes.Add($"not provided: {string.Join(", ", counts.DeclaredNotProvidedCommands)}");
        }

        foreach (string owner in counts.ShadowedCommandOwners)
        {
            notes.Add($"shadowed by {owner}");
        }

        if (notes.Count == 0)
        {
            return Field(counts.Commands, descriptor.Manifest.Commands.Count);
        }

        return FormattableString.Invariant(
            $"{counts.Commands} ({descriptor.Manifest.Commands.Count} declared, {string.Join(", ", notes)})"
        );
    }

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

    /// <summary>
    /// A whole-millisecond representation, rounded half away from zero and shared by the text and
    /// JSON renderings (design D2).
    /// </summary>
    private static long Milliseconds(TimeSpan elapsed) => Milliseconds(elapsed.TotalMilliseconds);

    /// <summary>
    /// A whole-millisecond representation of an already-millisecond value (the hook timing
    /// aggregates and start durations), rounded half away from zero.
    /// </summary>
    private static long Milliseconds(double milliseconds) =>
        (long)Math.Round(milliseconds, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The failure line: the message's first sentence (up to and including the first
    /// sentence-ending period — a <c>.</c> followed by a space or the end of the message) plus the
    /// <c> — see the log</c> pointer when the message was truncated; a message without such a
    /// period passes through whole, so dotted tokens like <c>Broken.dll</c> never split.
    /// </summary>
    private static string FailedMessage(string? message)
    {
        string text = message ?? string.Empty;
        int end = -1;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '.' && (index == text.Length - 1 || text[index + 1] == ' '))
            {
                end = index;
                break;
            }
        }

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

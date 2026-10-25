using System.Text.Json.Nodes;
using Lunate.Extensibility;

namespace Lunate.Coding;

/// <summary>
/// The <c>/extensions</c> view of single-file candidates: the state line each one reads, the
/// declared counts a usable header carries and the JSON shape of the same data. Kept beside the
/// packaged view (a partial of <see cref="ExtensionsView"/>) because the two render one list.
/// </summary>
internal static partial class ExtensionsView
{
    /// <summary>
    /// The single-file candidates' segments, appended after the packaged ones and ordered
    /// invalid → failed (trust) → stale → not-built, then by id (or file name): problems first
    /// inside the group. A candidate's state is followed by its reason on the same line, and a
    /// usable header carries the counts it declares.
    /// </summary>
    private static IEnumerable<string> SingleFileSegments(
        IReadOnlyList<SingleFileExtension>? singleFile
    ) => OrderedSingleFile(singleFile).Select(SingleFileSegment);

    private static IEnumerable<SingleFileExtension> OrderedSingleFile(
        IReadOnlyList<SingleFileExtension>? singleFile
    ) =>
        singleFile is null
            ? []
            : singleFile
                .OrderBy(candidate => SingleFileRank(candidate.State))
                .ThenBy(candidate => candidate.Id ?? candidate.FileName, StringComparer.Ordinal);

    private static string SingleFileSegment(SingleFileExtension candidate)
    {
        string head = candidate.Id is { } id
            ? FormattableString.Invariant(
                $"{id} {candidate.Version} ({ScopeText(candidate.Scope)}, single file)"
            )
            : FormattableString.Invariant(
                $"{candidate.FileName} ({ScopeText(candidate.Scope)}, single file)"
            );
        return candidate.State switch
        {
            SingleFileExtensionState.NotBuilt => head
                + " not-built"
                + SingleFileCounts(candidate)
                + SingleFileResolved(candidate)
                + SingleFileGenerators(candidate)
                + SingleFileNote(candidate),
            SingleFileExtensionState.Restoring => head
                + " restoring"
                + SingleFileResolved(candidate),
            SingleFileExtensionState.Stale => head
                + " stale — "
                + Reason(candidate)
                + SingleFileResolved(candidate)
                + SingleFileGenerators(candidate),
            SingleFileExtensionState.TrustFailed => head + " failed (trust) — " + Reason(candidate),
            SingleFileExtensionState.RestoreFailed => head
                + " failed (restore) — "
                + Reason(candidate),
            SingleFileExtensionState.CompileFailed => head
                + " failed (compile) — "
                + Reason(candidate),
            SingleFileExtensionState.Built => head
                + " built"
                + SingleFileCounts(candidate)
                + SingleFileResolved(candidate)
                + SingleFileGenerators(candidate),
            _ => head + " invalid (" + Reason(candidate) + ")",
        };
    }

    /// <summary>
    /// The resolved graph the lock records, once there is one: the packages and exact versions the
    /// extension will compile against, which is the half of the decision no prompt can show before
    /// the restore ran.
    /// </summary>
    private static string SingleFileResolved(SingleFileExtension candidate) =>
        candidate.Lock is not { } @lock || @lock.Packages.Count == 0
            ? string.Empty
            : ", resolved: "
                + string.Join(
                    ", ",
                    @lock
                        .Packages.OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
                        .Select(package =>
                            FormattableString.Invariant($"{package.Id} {package.Version}")
                        )
                );

    /// <summary>
    /// The packages whose build-time code the compile leaves out, named so the omission is visible:
    /// <c>, generators skipped: Refit</c>. Only a resolved graph can say which packages ship them,
    /// so the note appears once a lock exists and only while the header does not opt in.
    /// </summary>
    private static string SingleFileGenerators(SingleFileExtension candidate) =>
        candidate.GeneratorsSkipped.Count == 0
            ? string.Empty
            : ", generators skipped: " + string.Join(", ", candidate.GeneratorsSkipped);

    /// <summary>
    /// The declared counts of a usable header, plus — when a declaration nothing provided is
    /// known — <c>declared, not provided: &lt;names&gt;</c>.
    /// </summary>
    private static string SingleFileCounts(SingleFileExtension candidate)
    {
        ExtensionHeader header = candidate.Header;
        string counts = FormattableString.Invariant(
            $", tools {header.Tools.Count}, commands {header.Commands.Count}, hooks {header.Hooks.Count}, services {header.Services.Count}"
        );
        return candidate.DeclaredNotProvided.Count == 0
            ? counts
            : counts
                + ", declared, not provided: "
                + string.Join(", ", candidate.DeclaredNotProvided.Select(entry => entry.Name));
    }

    private static string Reason(SingleFileExtension candidate) =>
        candidate.Reason is { Length: > 0 } reason ? reason : "no reason recorded";

    /// <summary>
    /// The same-line note a not-built row carries when it has one: its lock on disk was refused (a
    /// toolset block that no longer answers for itself) and the next restore rebuilds it.
    /// </summary>
    private static string SingleFileNote(SingleFileExtension candidate) =>
        candidate.Reason is { Length: > 0 } note ? " — " + note : string.Empty;

    /// <summary>
    /// Problems first, then what needs building, then what is live: a built extension is the
    /// healthy end state and sorts last, so the rows that need the user's attention stay on top.
    /// </summary>
    private static int SingleFileRank(SingleFileExtensionState state) =>
        state switch
        {
            SingleFileExtensionState.Invalid => 0,
            SingleFileExtensionState.TrustFailed => 1,
            SingleFileExtensionState.CompileFailed => 2,
            SingleFileExtensionState.RestoreFailed => 3,
            SingleFileExtensionState.Stale => 4,
            SingleFileExtensionState.Restoring => 5,
            SingleFileExtensionState.NotBuilt => 6,
            _ => 7,
        };

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
        Func<string, TimeSpan?>? remoteStart = null,
        IReadOnlyList<SingleFileExtension>? singleFile = null
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

        foreach (SingleFileExtension candidate in OrderedSingleFile(singleFile))
        {
            array.Add(SingleFileNode(candidate));
        }

        return array.ToJsonString(JsonOptions);
    }

    /// <summary>
    /// One single-file candidate as JSON: <c>id</c> (null when the header declares none),
    /// <c>version</c>, <c>scope</c>, <c>state</c> (<c>not-built</c>, <c>stale</c>, <c>failed</c> or
    /// <c>invalid</c>), <c>singleFile</c> (the file name), <c>reason</c> when there is one,
    /// <c>failure</c> (<c>trust</c>) on a failed trust step, the declared counts of a usable header,
    /// <c>declaredNotProvided</c> when declarations nothing provided are known, and
    /// <c>generatorsSkipped</c> when a locked package ships build-time code the header did not opt
    /// in for.
    /// </summary>
    private static JsonObject SingleFileNode(SingleFileExtension candidate)
    {
        var node = new JsonObject
        {
            ["id"] = candidate.Id,
            ["version"] = candidate.Version,
            ["scope"] = ScopeText(candidate.Scope),
            ["state"] = SingleFileStateText(candidate.State),
            ["singleFile"] = candidate.FileName,
        };
        if (candidate.Reason is { Length: > 0 } reason)
        {
            node["reason"] = reason;
        }

        if (candidate.State == SingleFileExtensionState.TrustFailed)
        {
            node["failure"] = "trust";
        }
        else if (candidate.State == SingleFileExtensionState.RestoreFailed)
        {
            node["failure"] = "restore";
        }
        else if (candidate.State == SingleFileExtensionState.CompileFailed)
        {
            node["failure"] = "compile";
        }

        if (candidate.BuildKey is { Length: > 0 } buildKey)
        {
            node["build"] = buildKey;
        }

        if (candidate.Lock is { Packages.Count: > 0 } @lock)
        {
            var resolved = new JsonArray();
            foreach (
                ExtensionLockPackage package in @lock.Packages.OrderBy(
                    entry => entry.Id,
                    StringComparer.OrdinalIgnoreCase
                )
            )
            {
                resolved.Add($"{package.Id} {package.Version}");
            }

            node["resolved"] = resolved;
        }

        // The packages whose build-time code a compile leaves out, so a scripted surface can report
        // what the human view says: `generators skipped: <package>`.
        if (candidate.GeneratorsSkipped is { Count: > 0 } skippedPackages)
        {
            var skipped = new JsonArray();
            foreach (string package in skippedPackages)
            {
                skipped.Add(package);
            }

            node["generatorsSkipped"] = skipped;
        }

        if (candidate.State != SingleFileExtensionState.Invalid)
        {
            node["tools"] = candidate.Header.Tools.Count;
            node["commands"] = candidate.Header.Commands.Count;
            node["hooks"] = candidate.Header.Hooks.Count;
            node["services"] = candidate.Header.Services.Count;
        }

        if (candidate.DeclaredNotProvided.Count > 0)
        {
            var names = new JsonArray();
            foreach (ExtensionDeclaration declaration in candidate.DeclaredNotProvided)
            {
                names.Add(declaration.Name);
            }

            node["declaredNotProvided"] = names;
        }

        return node;
    }

    private static string SingleFileStateText(SingleFileExtensionState state) =>
        state switch
        {
            SingleFileExtensionState.NotBuilt => "not-built",
            SingleFileExtensionState.Restoring => "restoring",
            SingleFileExtensionState.Stale => "stale",
            SingleFileExtensionState.Built => "built",
            SingleFileExtensionState.TrustFailed => "failed",
            SingleFileExtensionState.RestoreFailed => "failed",
            SingleFileExtensionState.CompileFailed => "failed",
            _ => "invalid",
        };
}

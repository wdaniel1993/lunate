using Lunate.Ai;

namespace Lunate.Extensibility;

/// <summary>
/// One single-file trust question: the file the user is being asked about, the id and version it
/// declares, every <c>#:package</c> argument as written, whether the file opted into build-time
/// code, and whether this is a re-decision (the bytes changed) or the first one.
/// </summary>
public sealed record SingleFileTrustRequest
{
    public required string Path { get; init; }

    public required string Id { get; init; }

    public required string Version { get; init; }

    /// <summary>Declared packages as written, in header order (the transitive set is unknown here).</summary>
    public IReadOnlyList<string> Packages { get; init; } = [];

    /// <summary>True when the header carries <c>#:generators on</c>.</summary>
    public bool Generators { get; init; }

    /// <summary>True when a decision exists for other bytes: the source, a package or the lock changed.</summary>
    public bool Changed { get; init; }

    /// <summary>
    /// The resolution lines a restore would change, when the question is about an existing lock: the
    /// user sees the packages that would replace the ones they approved, not a bare "different". A
    /// toolset difference never reaches this list — it is a rebuild, not a decision.
    /// </summary>
    public IReadOnlyList<string> LockChanges { get; init; } = [];
}

/// <summary>
/// The wording of the single-file trust question. One formatter, so the interactive prompt, the
/// non-interactive refusal and the tests agree on what the user is told: the file, its id, each
/// declared package "and their dependencies" — nothing has resolved the graph yet, so pretending
/// to list it would be a lie — and, only when the header opted in, that compiling runs build-time
/// code from those packages.
/// <para>
/// The title ends with exactly one question mark and owns it: the prompt surface adds one only when
/// the title does not already end with one, so a phrased question is never asked twice.
/// </para>
/// </summary>
public static class SingleFileTrustQuestion
{
    /// <summary>The question's title: what is being trusted, and why now.</summary>
    public static string Title(SingleFileTrustRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string path = RealPath(request.Path);
        return request.Changed
            ? FormattableString.Invariant(
                $"Trust the single-file extension '{request.Id}' {request.Version} at '{path}' again — it changed since it was trusted last, so the earlier decision does not apply?"
            )
            : FormattableString.Invariant(
                $"Trust the single-file extension '{request.Id}' {request.Version} at '{path}'?"
            );
    }

    /// <summary>
    /// The real path shown in the question: absolute, with <c>..</c> and symbolic links resolved,
    /// so the deciding segment cannot hide behind a spelling. A path that is not rooted, cannot be
    /// resolved, or is a clean spelling that does not exist yet (a cosmetic <c>/home</c>,
    /// <c>/var</c> or placeholder path) is shown as given.
    /// </summary>
    private static string RealPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            return path;
        }

        // A missing path has nothing to resolve; canonicalizing would only rewrite a system
        // alias (macOS /home, /var) and make the cosmetic path worse.
        if (!HasParentSegment(path) && !Path.Exists(path))
        {
            return path;
        }

        try
        {
            return PathCanonical.Canonicalize(path);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            return path;
        }
    }

    private static bool HasParentSegment(string path) =>
        path.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries
            )
            .Contains("..");

    /// <summary>
    /// The question's details, one line each: the declared packages (or that there are none), the
    /// dependency caveat, and the build-time-code line only when the header opted in.
    /// </summary>
    public static IReadOnlyList<string> Details(SingleFileTrustRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<string> details = [];
        if (request.Packages.Count == 0)
        {
            details.Add("It declares no NuGet packages.");
        }
        else
        {
            details.Add(
                "It declares " + string.Join(", ", request.Packages) + " and their dependencies."
            );
        }

        details.Add(
            "Trust covers this file's bytes, the packages as written and, after the first restore, "
                + "the packages it resolves to. A .NET update does not change it. It is not a "
                + "signature check."
        );

        if (request.Generators)
        {
            details.Add(
                "Because the header says #:generators on, compiling it runs build-time code "
                    + "(analyzers and source generators) from those packages."
            );
        }

        if (request.LockChanges.Count > 0)
        {
            details.Add("Restoring it would change its lock file:");
            foreach (string change in request.LockChanges)
            {
                details.Add("  " + change);
            }
        }

        return details;
    }
}

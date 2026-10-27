using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lunate.Ai;

namespace Lunate.Extensibility;

/// <summary>
/// What the store knows about one single-file extension's trust: never decided, decided for exactly
/// this digest, decided for an earlier digest (stale), or recorded in a shape that cannot be read.
/// </summary>
public enum SingleFileExtensionTrust
{
    Untrusted,
    Trusted,
    Stale,
    Unreadable,
}

public sealed class ExtensionTrustStore
{
    private const string RepositoriesSection = "repositories";
    private const string McpConfigSection = "mcpConfig";
    private const string McpServerSection = "mcpServers";
    private const string SkillsSection = "skills";
    private const string SingleFileSection = "singleFile";
    private const string WorktreesKey = "worktrees";
    private const string FilesKey = "files";

    /// <summary>How long a read-modify-write waits for another writer before it refuses.</summary>
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);

    private readonly ExtensionHostOptions _options;

    public ExtensionTrustStore(ExtensionHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// The write claim, beside the store: <c>trust.lock</c> for <c>trust.json</c>. Every
    /// read-modify-write holds it, so two approvals merge instead of each writing the document it
    /// read before the other one landed.
    /// </summary>
    internal string LockPath => Path.ChangeExtension(_options.TrustFilePath, ".lock");

    /// <summary>
    /// The content hash of an extension directory: every file's relative path and its bytes, sorted
    /// and NUL-separated. The bytes are text with CRLF normalized to LF, so the same source tree
    /// checked out on Windows and on POSIX hashes the same and a line-ending-only change does not
    /// re-prompt.
    /// </summary>
    public static string ComputeContentHash(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (Directory.Exists(directory))
        {
            List<(string Relative, string FullPath)> files = [];
            foreach (
                string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            )
            {
                files.Add((Path.GetRelativePath(directory, path).Replace('\\', '/'), path));
            }

            files.Sort(
                (left, right) => StringComparer.Ordinal.Compare(left.Relative, right.Relative)
            );
            byte[] separator = [0];
            foreach ((string relative, string fullPath) in files)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(relative));
                hash.AppendData(separator);
                hash.AppendData(LineEndings.ToLf(File.ReadAllBytes(fullPath)));
                hash.AppendData(separator);
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// The equivalent of <see cref="ComputeContentHash(string)"/> for a single file: the file name
    /// plays the relative-path role. The two hash families feed disjoint trust sections, so a
    /// coincidence between them can never cross-approve. The bytes are text with CRLF normalized to
    /// LF, for the same reason the directory hash normalizes.
    /// </summary>
    public static string ComputeFileContentHash(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (File.Exists(path))
        {
            byte[] separator = [0];
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(path)));
            hash.AppendData(separator);
            hash.AppendData(LineEndings.ToLf(File.ReadAllBytes(path)));
            hash.AppendData(separator);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// The extension trust record: one entry per repository, worktree and extension id. Keying by
    /// the extension's own id is what lets two project extensions in one worktree hold their own
    /// decision; a worktree alone cannot say which of them a digest belongs to. When the worktree
    /// has no entry for this id, the digest the same id was trusted with in another worktree of
    /// this repository answers the new-worktree question — trust travels with the extension, never
    /// with the path alone.
    /// </summary>
    public ExtensionTrustDecision Evaluate(
        string repositoryIdentity,
        string worktreePath,
        string extensionId,
        string contentHash
    ) =>
        EvaluateIn(
            WorktreesKey,
            RepositoriesSection,
            repositoryIdentity,
            worktreePath,
            extensionId,
            contentHash
        );

    /// <summary>
    /// The project mcp.json file record: the same per-repository/hash semantics as extension
    /// trust, kept in a distinct section so extension trust is never weakened or shared with a
    /// config file. Each of the standard project files is its own record, keyed by the config
    /// file's path (three files in one worktree must not overwrite each other). A repository
    /// object without the entries map — a store written by a layout that predates the keying —
    /// cannot answer the question, so it prompts like an unknown repository.
    /// </summary>
    public ExtensionTrustDecision EvaluateMcpConfig(
        string repositoryIdentity,
        string configPath,
        string contentHash
    ) => EvaluateFile(FilesKey, McpConfigSection, repositoryIdentity, configPath, contentHash);

    /// <summary>
    /// The project-skill trust record: one entry per repository, worktree and skill name, in a
    /// section distinct from extension trust so a skill's decision is never inherited from an
    /// extension (or another skill) that happens to share the worktree.
    /// </summary>
    public ExtensionTrustDecision EvaluateSkills(
        string repositoryIdentity,
        string worktreePath,
        string skillName,
        string contentHash
    ) =>
        EvaluateIn(
            WorktreesKey,
            SkillsSection,
            repositoryIdentity,
            worktreePath,
            skillName,
            contentHash
        );

    /// <summary>
    /// The per-server approval record: keyed by repository identity and the canonical hash of the
    /// server's raw definition (never by worktree — a definition can run different code per
    /// repository, so the repository is the boundary; the recorded worktree is audit).
    /// </summary>
    public ExtensionTrustDecision EvaluateMcpServer(
        string repositoryIdentity,
        string definitionHash
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionHash);

        JsonObject document = ReadDocument();
        if (
            document[McpServerSection] is not JsonObject repositories
            || repositories[repositoryIdentity] is not JsonObject repository
            || repository["definitions"] is not JsonObject definitions
        )
        {
            return ExtensionTrustDecision.Prompt;
        }

        return definitions.ContainsKey(definitionHash)
            ? ExtensionTrustDecision.Trusted
            : ExtensionTrustDecision.Prompt;
    }

    public void RecordTrusted(
        string repositoryIdentity,
        string worktreePath,
        string extensionId,
        string contentHash,
        DateTimeOffset trustedAt
    ) =>
        RecordIn(
            WorktreesKey,
            RepositoriesSection,
            repositoryIdentity,
            worktreePath,
            extensionId,
            contentHash,
            trustedAt
        );

    /// <summary>Records one project mcp.json file's trust decision, keyed by its path.</summary>
    public void RecordMcpConfigTrusted(
        string repositoryIdentity,
        string configPath,
        string contentHash,
        DateTimeOffset trustedAt
    ) =>
        RecordFile(
            FilesKey,
            McpConfigSection,
            repositoryIdentity,
            configPath,
            contentHash,
            trustedAt
        );

    /// <summary>Records one worktree's project-skill trust decision, keyed by the skill's name.</summary>
    public void RecordSkillsTrusted(
        string repositoryIdentity,
        string worktreePath,
        string skillName,
        string contentHash,
        DateTimeOffset trustedAt
    ) =>
        RecordIn(
            WorktreesKey,
            SkillsSection,
            repositoryIdentity,
            worktreePath,
            skillName,
            contentHash,
            trustedAt
        );

    /// <summary>
    /// Records one server definition's approval for the repository; the worktree is kept as the
    /// approving worktree for audit.
    /// </summary>
    public void RecordMcpServerTrusted(
        string repositoryIdentity,
        string worktreePath,
        string definitionHash,
        DateTimeOffset trustedAt
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionHash);

        using FileLock lease = AcquireLock();
        JsonObject document = ReadDocument();
        JsonObject repositories = document[McpServerSection] as JsonObject ?? new JsonObject();
        document[McpServerSection] = repositories;

        JsonObject repository = repositories[repositoryIdentity] as JsonObject ?? new JsonObject();
        repositories[repositoryIdentity] = repository;
        repository["trustedAt"] ??= trustedAt.ToString("O", CultureInfo.InvariantCulture);

        JsonObject definitions = repository["definitions"] as JsonObject ?? new JsonObject();
        repository["definitions"] = definitions;
        definitions[definitionHash] = worktreePath;

        WriteAtomically(document);
    }

    /// <summary>
    /// The single-file trust record: one entry per file path, whose value is the first-trust digest
    /// the user approved (<c>singleFile: { "&lt;path&gt;": "&lt;digest&gt;" }</c>) — the same
    /// section → key → hash shape as the extension sections, and a distinct section so a file's
    /// decision is never inherited from another file, another path or a packaged extension. A
    /// missing entry is a decision nobody has made; a value that is not a string is a record this
    /// version cannot read, which is reported rather than guessed at.
    /// </summary>
    public SingleFileExtensionTrust EvaluateSingleFileTrust(string path, string digest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(digest);

        JsonObject document = ReadDocument();
        if (document[SingleFileSection] is not JsonObject files)
        {
            return SingleFileExtensionTrust.Untrusted;
        }

        if (!files.TryGetPropertyValue(path, out JsonNode? recorded))
        {
            return SingleFileExtensionTrust.Untrusted;
        }

        return recorded is JsonValue value && value.TryGetValue<string>(out string? recordedDigest)
            ? string.Equals(recordedDigest, digest, StringComparison.Ordinal)
                ? SingleFileExtensionTrust.Trusted
                : SingleFileExtensionTrust.Stale
            : SingleFileExtensionTrust.Unreadable;
    }

    /// <summary>Records one file's approved first-trust digest, keyed by its full path.</summary>
    public void RecordSingleFileTrust(string path, string digest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(digest);

        using FileLock lease = AcquireLock();
        JsonObject document = ReadDocument();
        JsonObject files = document[SingleFileSection] as JsonObject ?? new JsonObject();
        document[SingleFileSection] = files;
        files[path] = digest;

        WriteAtomically(document);
    }

    /// <summary>
    /// Every trust record this repository holds across all four sections (extension trust, the
    /// project mcp.json files, the per-server approvals and project skills), ordered by section
    /// then worktree: the changed-files re-prompt wording reads it, and <c>/trust</c> lists it.
    /// </summary>
    public IReadOnlyList<TrustRecord> Records(string repositoryIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryIdentity);

        JsonObject document = ReadDocument();
        List<TrustRecord> records = [];
        CollectWorktrees(
            document,
            RepositoriesSection,
            TrustSection.Extensions,
            repositoryIdentity,
            records
        );
        CollectFiles(
            document,
            McpConfigSection,
            TrustSection.McpConfig,
            repositoryIdentity,
            records
        );
        CollectWorktrees(document, SkillsSection, TrustSection.Skills, repositoryIdentity, records);
        CollectDefinitions(
            document,
            McpServerSection,
            TrustSection.McpServer,
            repositoryIdentity,
            records
        );
        records.Sort(
            (left, right) =>
            {
                int section = left.Section.CompareTo(right.Section);
                return section != 0
                    ? section
                    : StringComparer.Ordinal.Compare(left.WorktreePath, right.WorktreePath);
            }
        );
        return records;
    }

    /// <summary>
    /// Removes every record for the repository from all sections (file records and per-server
    /// approvals) and every single-file record whose path lies inside the repository — the root its
    /// identity names, the worktree the caller passes, and any worktree the store already recorded
    /// for it — so the next load asks again. A single-file record carries only a file path, so
    /// containment is what decides which repository it belongs to; a record outside every one of
    /// those roots is another repository's and is kept. Other repositories and unrelated keys are
    /// untouched, nothing is written when there is nothing to clear, and the single-file paths that
    /// were removed are returned so a caller can report them.
    /// </summary>
    public IReadOnlyList<string> ClearRepository(string repositoryIdentity, string? worktree = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryIdentity);

        using FileLock lease = AcquireLock();
        JsonObject document = ReadDocument();
        IReadOnlyList<string> roots = ClearedRoots(document, repositoryIdentity, worktree);
        var changed = false;
        foreach (
            string section in new[]
            {
                RepositoriesSection,
                McpConfigSection,
                McpServerSection,
                SkillsSection,
            }
        )
        {
            if (
                document[section] is JsonObject repositories
                && repositories.Remove(repositoryIdentity)
            )
            {
                changed = true;
            }
        }

        List<string> clearedFiles = [];
        if (document[SingleFileSection] is JsonObject files)
        {
            foreach (string path in files.Select(entry => entry.Key).ToArray())
            {
                if (!roots.Any(root => IsInside(root, path)))
                {
                    continue;
                }

                files.Remove(path);
                clearedFiles.Add(path);
                changed = true;
            }
        }

        if (changed)
        {
            WriteAtomically(document);
        }

        return clearedFiles;
    }

    /// <summary>
    /// The directories whose single-file records this repository owns: the root the identity names
    /// (the parent of a <c>.git</c> common dir, or the identity itself), the caller's worktree, and
    /// every worktree the store already recorded for the repository.
    /// </summary>
    private static IReadOnlyList<string> ClearedRoots(
        JsonObject document,
        string repositoryIdentity,
        string? worktree
    )
    {
        List<string> roots = [];
        if (RepositoryRoot(repositoryIdentity) is { } root)
        {
            roots.Add(root);
        }

        if (!string.IsNullOrWhiteSpace(worktree))
        {
            roots.Add(worktree);
        }

        foreach (string section in new[] { RepositoriesSection, SkillsSection })
        {
            if (
                document[section] is JsonObject repositories
                && repositories[repositoryIdentity] is JsonObject repository
                && repository[WorktreesKey] is JsonObject worktrees
            )
            {
                roots.AddRange(worktrees.Select(entry => entry.Key));
            }
        }

        return roots;
    }

    /// <summary>The worktree root an identity names: its parent for a <c>.git</c> common dir, else itself.</summary>
    private static string? RepositoryRoot(string repositoryIdentity)
    {
        string trimmed = Path.TrimEndingDirectorySeparator(repositoryIdentity);
        return string.Equals(Path.GetFileName(trimmed), ".git", StringComparison.Ordinal)
            ? Path.GetDirectoryName(trimmed)
            : trimmed;
    }

    /// <summary>
    /// Containment for the single-file records: both paths are canonicalized the way discovery
    /// canonicalizes (existing symlink components resolved, so <c>/var/…</c> and
    /// <c>/private/var/…</c> agree on macOS), a trailing separator is ignored, the comparison
    /// follows the platform's case rule, and a path equal to the root counts as inside.
    /// </summary>
    private static bool IsInside(string root, string path)
    {
        string boundary = Normalize(root);
        string candidate = Normalize(path);
        if (string.Equals(candidate, boundary, PathComparison))
        {
            return true;
        }

        string prefix = Path.EndsInDirectorySeparator(boundary)
            ? boundary
            : boundary + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, PathComparison);
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(PathCanonical.Canonicalize(path));
        }
        catch (Exception exception)
            when (exception
                    is ArgumentException
                        or IOException
                        or NotSupportedException
                        or PathTooLongException
            )
        {
            return path;
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// The worktree-keyed collectors (extension trust and project skills): each worktree holds one
    /// map from entry id to digest. A flat string under a worktree is the layout before the id was
    /// part of the key; it cannot say which extension or skill it approved, so it is not reported as
    /// a record (and the prompt still asks).
    /// </summary>
    private static void CollectWorktrees(
        JsonObject document,
        string section,
        TrustSection kind,
        string repositoryIdentity,
        List<TrustRecord> records
    )
    {
        if (
            document[section] is not JsonObject repositories
            || repositories[repositoryIdentity] is not JsonObject repository
            || repository[WorktreesKey] is not JsonObject worktrees
        )
        {
            return;
        }

        foreach ((string worktree, JsonNode? entries) in worktrees)
        {
            if (entries is not JsonObject decided)
            {
                continue;
            }

            foreach ((string _, JsonNode? recorded) in decided)
            {
                if (
                    recorded is JsonValue value
                    && value.TryGetValue<string>(out string? recordedHash)
                )
                {
                    records.Add(new TrustRecord(kind, worktree, recordedHash));
                }
            }
        }
    }

    /// <summary>The file-keyed collector: the project <c>mcp.json</c> records, one per file path.</summary>
    private static void CollectFiles(
        JsonObject document,
        string section,
        TrustSection kind,
        string repositoryIdentity,
        List<TrustRecord> records
    )
    {
        if (
            document[section] is not JsonObject repositories
            || repositories[repositoryIdentity] is not JsonObject repository
            || repository[FilesKey] is not JsonObject files
        )
        {
            return;
        }

        foreach ((string path, JsonNode? recorded) in files)
        {
            if (recorded is JsonValue value && value.TryGetValue<string>(out string? recordedHash))
            {
                records.Add(new TrustRecord(kind, path, recordedHash));
            }
        }
    }

    /// <summary>
    /// The per-server collector: the section stores <c>definitions: hash → approving worktree</c>,
    /// the inversion of the file sections' <c>worktrees</c> map.
    /// </summary>
    private static void CollectDefinitions(
        JsonObject document,
        string section,
        TrustSection kind,
        string repositoryIdentity,
        List<TrustRecord> records
    )
    {
        if (
            document[section] is not JsonObject repositories
            || repositories[repositoryIdentity] is not JsonObject repository
            || repository["definitions"] is not JsonObject definitions
        )
        {
            return;
        }

        foreach (KeyValuePair<string, JsonNode?> entry in definitions)
        {
            if (entry.Value is JsonValue value && value.TryGetValue<string>(out string? worktree))
            {
                records.Add(new TrustRecord(kind, worktree, entry.Key));
            }
        }
    }

    /// <summary>
    /// The project <c>mcp.json</c> record keeps its own flat shape — one config-file path per
    /// digest, never nested under a worktree — so it gets its own reader rather than sharing the
    /// worktree-keyed one. A missing entry is the new-worktree decision it always was.
    /// </summary>
    private ExtensionTrustDecision EvaluateFile(
        string entriesKey,
        string section,
        string repositoryIdentity,
        string entryKey,
        string contentHash
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        JsonObject document = ReadDocument();
        if (
            document[section] is not JsonObject repositories
            || repositories[repositoryIdentity] is not JsonObject repository
            || repository[entriesKey] is not JsonObject entries
        )
        {
            return ExtensionTrustDecision.Prompt;
        }

        if (
            entries[entryKey] is not JsonValue recorded
            || !recorded.TryGetValue<string>(out string? recordedHash)
        )
        {
            return ExtensionTrustDecision.NewWorktree;
        }

        return string.Equals(recordedHash, contentHash, StringComparison.Ordinal)
            ? ExtensionTrustDecision.Trusted
            : ExtensionTrustDecision.Prompt;
    }

    private void RecordFile(
        string entriesKey,
        string section,
        string repositoryIdentity,
        string entryKey,
        string contentHash,
        DateTimeOffset trustedAt
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        using FileLock lease = AcquireLock();
        JsonObject document = ReadDocument();
        JsonObject repositories = document[section] as JsonObject ?? new JsonObject();
        document[section] = repositories;

        JsonObject repository = repositories[repositoryIdentity] as JsonObject ?? new JsonObject();
        repositories[repositoryIdentity] = repository;
        repository["trustedAt"] ??= trustedAt.ToString("O", CultureInfo.InvariantCulture);

        JsonObject entries = repository[entriesKey] as JsonObject ?? new JsonObject();
        repository[entriesKey] = entries;
        entries[entryKey] = contentHash;

        WriteAtomically(document);
    }

    private ExtensionTrustDecision EvaluateIn(
        string entriesKey,
        string section,
        string repositoryIdentity,
        string worktreePath,
        string entryId,
        string contentHash
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        JsonObject document = ReadDocument();
        if (
            document[section] is not JsonObject repositories
            || repositories[repositoryIdentity] is not JsonObject repository
            || repository[entriesKey] is not JsonObject entries
        )
        {
            return ExtensionTrustDecision.Prompt;
        }

        if (Decide(entries, worktreePath, entryId, contentHash) is { } decision)
        {
            return decision;
        }

        // The worktree has no entry for this id. The same extension id trusted with these bytes in
        // another worktree of this repository is the same decision, so it does not ask again; any
        // other digest is a change and takes the normal question.
        return TrustedElsewhere(entries, worktreePath, entryId, contentHash)
            ? ExtensionTrustDecision.NewWorktree
            : ExtensionTrustDecision.Prompt;
    }

    /// <summary>
    /// This worktree's answer for one entry id, or null when this worktree has no entry for it. A
    /// worktree still stored as a flat digest predates keying by id, so it cannot say which
    /// extension or skill it approved: it prompts rather than being guessed at.
    /// </summary>
    private static ExtensionTrustDecision? Decide(
        JsonObject worktrees,
        string worktreePath,
        string entryId,
        string contentHash
    )
    {
        if (!worktrees.TryGetPropertyValue(worktreePath, out JsonNode? node))
        {
            return null;
        }

        if (node is not JsonObject entries)
        {
            return ExtensionTrustDecision.Prompt;
        }

        if (!entries.TryGetPropertyValue(entryId, out JsonNode? recorded))
        {
            return null;
        }

        return
            recorded is JsonValue value
            && value.TryGetValue<string>(out string? recordedHash)
            && string.Equals(recordedHash, contentHash, StringComparison.Ordinal)
            ? ExtensionTrustDecision.Trusted
            : ExtensionTrustDecision.Prompt;
    }

    /// <summary>Whether another worktree of this repository trusted this id with these exact bytes.</summary>
    private static bool TrustedElsewhere(
        JsonObject worktrees,
        string worktreePath,
        string entryId,
        string contentHash
    )
    {
        foreach ((string path, JsonNode? node) in worktrees)
        {
            if (
                string.Equals(path, worktreePath, StringComparison.Ordinal)
                || node is not JsonObject entries
                || !entries.TryGetPropertyValue(entryId, out JsonNode? recorded)
            )
            {
                continue;
            }

            if (
                recorded is JsonValue value
                && value.TryGetValue<string>(out string? recordedHash)
                && string.Equals(recordedHash, contentHash, StringComparison.Ordinal)
            )
            {
                return true;
            }
        }

        return false;
    }

    private void RecordIn(
        string entriesKey,
        string section,
        string repositoryIdentity,
        string worktreePath,
        string entryId,
        string contentHash,
        DateTimeOffset trustedAt
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        using FileLock lease = AcquireLock();
        JsonObject document = ReadDocument();
        JsonObject repositories = document[section] as JsonObject ?? new JsonObject();
        document[section] = repositories;

        JsonObject repository = repositories[repositoryIdentity] as JsonObject ?? new JsonObject();
        repositories[repositoryIdentity] = repository;
        repository["trustedAt"] ??= trustedAt.ToString("O", CultureInfo.InvariantCulture);

        JsonObject worktrees = repository[entriesKey] as JsonObject ?? new JsonObject();
        repository[entriesKey] = worktrees;

        JsonObject entries = worktrees[worktreePath] as JsonObject ?? new JsonObject();
        worktrees[worktreePath] = entries;
        entries[entryId] = contentHash;

        WriteAtomically(document);
    }

    /// <summary>
    /// The store's document, or a refusal naming the file. A store that cannot be read is never
    /// reported as an empty one: every caller that then wrote would replace every decision the
    /// bytes still hold with the single one it was recording, so a damaged file stays exactly as
    /// it is until a person repairs or removes it.
    /// </summary>
    private JsonObject ReadDocument()
    {
        if (!File.Exists(_options.TrustFilePath))
        {
            return new JsonObject();
        }

        string text;
        try
        {
            text = File.ReadAllText(_options.TrustFilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Unreadable($"it could not be read: {exception.Message}", exception);
        }

        try
        {
            return JsonNode.Parse(text) is JsonObject document
                ? document
                : throw Unreadable("it does not hold a JSON object", inner: null);
        }
        catch (JsonException exception)
        {
            throw Unreadable($"it is not valid JSON: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// One message for every way the store can be unreadable: what is wrong with it, that nothing
    /// was changed, and the two things a person can do about it.
    /// </summary>
    private InvalidOperationException Unreadable(string problem, Exception? inner) =>
        new(
            $"the trust store '{_options.TrustFilePath}' cannot be read ({problem}); nothing was "
                + "changed and no decision in it was lost. Repair the file, or delete it to start "
                + "with an empty store and be asked again.",
            inner
        );

    /// <summary>
    /// The store's write claim, held across the read, the change and the write: two processes that
    /// each read first would otherwise each write a document that lacks the other's decision, and
    /// the later one wins silently. A claim that cannot be taken is a refusal, not a write without
    /// it — the decisions in the file are worth more than this one approval.
    /// </summary>
    private FileLock AcquireLock()
    {
        try
        {
            return FileLock.Acquire(LockPath, LockTimeout);
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException(
                $"the trust store '{_options.TrustFilePath}' is being written by another Lunate "
                    + $"process (lock '{LockPath}'); nothing was changed and nothing in it was lost, "
                    + "so try the approval again.",
                exception
            );
        }
    }

    private void WriteAtomically(JsonObject document)
    {
        Directory.CreateDirectory(_options.StorePath);
        string temporary = _options.TrustFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (
            FileStream stream = SecureFiles.OpenOwnerOnly(
                temporary,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None
            )
        )
        {
            stream.Write(
                Encoding.UTF8.GetBytes(
                    document.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
                )
            );
        }

        SecureFiles.RestrictToOwner(temporary);
        File.Move(temporary, _options.TrustFilePath, overwrite: true);
    }
}

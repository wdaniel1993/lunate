using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunate.Extensibility;

public sealed class ExtensionTrustStore
{
    private const string RepositoriesSection = "repositories";
    private const string McpConfigSection = "mcpConfig";
    private const string McpServerSection = "mcpServers";
    private const string WorktreesKey = "worktrees";
    private const string FilesKey = "files";

    private readonly ExtensionHostOptions _options;

    public ExtensionTrustStore(ExtensionHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

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
            byte[] buffer = new byte[81920];
            foreach ((string relative, string fullPath) in files)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(relative));
                hash.AppendData(separator);

                using FileStream stream = File.OpenRead(fullPath);
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                }

                hash.AppendData(separator);
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// The equivalent of <see cref="ComputeContentHash(string)"/> for a single file: the file name
    /// plays the relative-path role. The two hash families feed disjoint trust sections, so a
    /// coincidence between them can never cross-approve.
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

            byte[] buffer = new byte[81920];
            using FileStream stream = File.OpenRead(path);
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }

            hash.AppendData(separator);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public ExtensionTrustDecision Evaluate(
        string repositoryIdentity,
        string worktreePath,
        string contentHash
    ) =>
        EvaluateIn(
            WorktreesKey,
            RepositoriesSection,
            repositoryIdentity,
            worktreePath,
            contentHash
        );

    /// <summary>
    /// The project mcp.json file record: the same per-repository/hash semantics as extension
    /// trust, kept in a distinct section so extension trust is never weakened or shared with a
    /// config file. Each of the standard project files is its own record, keyed by the config
    /// file's path (three files in one worktree must not overwrite each other).
    /// </summary>
    public ExtensionTrustDecision EvaluateMcpConfig(
        string repositoryIdentity,
        string configPath,
        string contentHash
    ) => EvaluateIn(FilesKey, McpConfigSection, repositoryIdentity, configPath, contentHash);

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
        string contentHash,
        DateTimeOffset trustedAt
    ) =>
        RecordIn(
            WorktreesKey,
            RepositoriesSection,
            repositoryIdentity,
            worktreePath,
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
        RecordIn(
            FilesKey,
            McpConfigSection,
            repositoryIdentity,
            configPath,
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
    /// Every trust record this repository holds across all three sections (extension trust, the
    /// project mcp.json files, and the per-server approvals), ordered by section then worktree: the
    /// changed-files re-prompt wording reads it, and <c>/trust</c> lists it.
    /// </summary>
    public IReadOnlyList<TrustRecord> Records(string repositoryIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryIdentity);

        JsonObject document = ReadDocument();
        List<TrustRecord> records = [];
        Collect(
            document,
            WorktreesKey,
            RepositoriesSection,
            TrustSection.Extensions,
            repositoryIdentity,
            records
        );
        Collect(
            document,
            FilesKey,
            McpConfigSection,
            TrustSection.McpConfig,
            repositoryIdentity,
            records
        );
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
    /// approvals), so the next load asks again. Other repositories and unrelated keys are
    /// untouched; nothing is written when there is no record to clear.
    /// </summary>
    public void ClearRepository(string repositoryIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryIdentity);

        JsonObject document = ReadDocument();
        var changed = false;
        foreach (
            string section in new[] { RepositoriesSection, McpConfigSection, McpServerSection }
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

        if (changed)
        {
            WriteAtomically(document);
        }
    }

    private static void Collect(
        JsonObject document,
        string entriesKey,
        string section,
        TrustSection kind,
        string repositoryIdentity,
        List<TrustRecord> records
    )
    {
        if (
            document[section] is not JsonObject repositories
            || repositories[repositoryIdentity] is not JsonObject repository
            || repository[entriesKey] is not JsonObject entries
        )
        {
            return;
        }

        foreach (KeyValuePair<string, JsonNode?> entry in entries)
        {
            if (
                entry.Value is JsonValue value
                && value.TryGetValue<string>(out string? recordedHash)
            )
            {
                records.Add(new TrustRecord(kind, entry.Key, recordedHash));
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

    private ExtensionTrustDecision EvaluateIn(
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
        )
        {
            return ExtensionTrustDecision.Prompt;
        }

        if (repository[entriesKey] is not JsonObject entries)
        {
            return ExtensionTrustDecision.NewWorktree;
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

    private void RecordIn(
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

    private JsonObject ReadDocument()
    {
        if (!File.Exists(_options.TrustFilePath))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(_options.TrustFilePath)) as JsonObject
                ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private void WriteAtomically(JsonObject document)
    {
        Directory.CreateDirectory(_options.StorePath);
        string temporary = _options.TrustFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(
            temporary,
            document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
        File.Move(temporary, _options.TrustFilePath, overwrite: true);
    }
}

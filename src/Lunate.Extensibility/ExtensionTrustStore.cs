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
    /// plays the relative-path role, so a file's hash never collides with a directory's.
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
    ) => EvaluateIn(RepositoriesSection, repositoryIdentity, worktreePath, contentHash);

    /// <summary>
    /// The mcp.json record: the same per-repository/hash semantics as extension trust, kept in a
    /// distinct section so extension trust is never weakened or shared with a config file.
    /// </summary>
    public ExtensionTrustDecision EvaluateMcpConfig(
        string repositoryIdentity,
        string worktreePath,
        string contentHash
    ) => EvaluateIn(McpConfigSection, repositoryIdentity, worktreePath, contentHash);

    public void RecordTrusted(
        string repositoryIdentity,
        string worktreePath,
        string contentHash,
        DateTimeOffset trustedAt
    ) => RecordIn(RepositoriesSection, repositoryIdentity, worktreePath, contentHash, trustedAt);

    /// <summary>Records the mcp.json trust decision in its own section of the trust file.</summary>
    public void RecordMcpConfigTrusted(
        string repositoryIdentity,
        string worktreePath,
        string contentHash,
        DateTimeOffset trustedAt
    ) => RecordIn(McpConfigSection, repositoryIdentity, worktreePath, contentHash, trustedAt);

    private ExtensionTrustDecision EvaluateIn(
        string section,
        string repositoryIdentity,
        string worktreePath,
        string contentHash
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        JsonObject document = ReadDocument();
        if (
            document[section] is not JsonObject repositories
            || repositories[repositoryIdentity] is not JsonObject repository
        )
        {
            return ExtensionTrustDecision.Prompt;
        }

        if (repository["worktrees"] is not JsonObject worktrees)
        {
            return ExtensionTrustDecision.NewWorktree;
        }

        if (
            worktrees[worktreePath] is not JsonValue recorded
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
        string section,
        string repositoryIdentity,
        string worktreePath,
        string contentHash,
        DateTimeOffset trustedAt
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        JsonObject document = ReadDocument();
        JsonObject repositories = document[section] as JsonObject ?? new JsonObject();
        document[section] = repositories;

        JsonObject repository = repositories[repositoryIdentity] as JsonObject ?? new JsonObject();
        repositories[repositoryIdentity] = repository;
        repository["trustedAt"] ??= trustedAt.ToString("O", CultureInfo.InvariantCulture);

        JsonObject worktrees = repository["worktrees"] as JsonObject ?? new JsonObject();
        repository["worktrees"] = worktrees;
        worktrees[worktreePath] = contentHash;

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

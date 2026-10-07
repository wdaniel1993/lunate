using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunate.Extensibility;

public sealed class ExtensionTrustStore
{
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

    public ExtensionTrustDecision Evaluate(
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
            document["repositories"] is not JsonObject repositories
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

    public void RecordTrusted(
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
        JsonObject repositories = document["repositories"] as JsonObject ?? new JsonObject();
        document["repositories"] = repositories;

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

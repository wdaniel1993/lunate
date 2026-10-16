using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Lunate.Extensibility;
using Lunate.Protocols;

namespace Lunate.Coding;

/// <summary>One project extension's trust state in a worktree.</summary>
internal sealed record PendingExtension(
    ExtensionDescriptor Descriptor,
    string ContentHash,
    bool FilesChanged,
    ExtensionTrustDecision Decision
);

/// <summary>The project <c>mcp.json</c>'s trust state in a worktree.</summary>
internal sealed record PendingMcpConfig(
    string Path,
    IReadOnlyList<McpServerOptions> Servers,
    string ContentHash,
    ExtensionTrustDecision Decision
);

/// <summary>
/// One project-scoped server definition without its per-server approval: the raw definition as
/// written and its canonical definition hash (the approval key).
/// </summary>
internal sealed record PendingMcpServer(McpServerOptions Definition, string Hash);

/// <summary>
/// The discovery and evaluation behind every trust decision: project extensions (directory hash),
/// the project <c>mcp.json</c> files (definition hash) and the per-server approvals (definition
/// hash), evaluated against the store. It reads descriptors and computes hashes only — no assembly
/// load, no hook dispatch, no server start — so the interactive session's question count and the
/// trust command cannot drift.
/// </summary>
internal sealed class TrustInventory
{
    private string? _digest;
    private bool _digestComputed;

    private TrustInventory(
        string repositoryIdentity,
        string worktreePath,
        IReadOnlyList<TrustRecord> records,
        IReadOnlyList<PendingExtension> extensions,
        IReadOnlyList<PendingMcpConfig> mcpConfigs,
        IReadOnlyList<PendingMcpServer> mcpServers
    )
    {
        RepositoryIdentity = repositoryIdentity;
        WorktreePath = worktreePath;
        Records = records;
        Extensions = extensions;
        ProjectMcpConfigs = mcpConfigs;
        McpServers = mcpServers;
    }

    internal string RepositoryIdentity { get; }

    internal string WorktreePath { get; }

    /// <summary>This repository's records across all store sections.</summary>
    internal IReadOnlyList<TrustRecord> Records { get; }

    /// <summary>Every project extension that is not already trusted, id-ordered.</summary>
    internal IReadOnlyList<PendingExtension> Extensions { get; }

    /// <summary>
    /// The project files that exist and are not already trusted, in precedence order
    /// (<c>.lunate</c>, <c>.agents</c>, root <c>.mcp.json</c>).
    /// </summary>
    internal IReadOnlyList<PendingMcpConfig> ProjectMcpConfigs { get; }

    /// <summary>
    /// Every project-scoped server definition without its per-server approval, file order then
    /// definition order — a session asks for these after the file trust, and the trust command
    /// records them.
    /// </summary>
    internal IReadOnlyList<PendingMcpServer> McpServers { get; }

    /// <summary>Decisions a session would ask about: unknown or changed hashes.</summary>
    internal int PendingCount =>
        Extensions.Count(extension => extension.Decision is ExtensionTrustDecision.Prompt)
        + ProjectMcpConfigs.Count(config => config.Decision is ExtensionTrustDecision.Prompt)
        + McpServers.Count;

    internal bool HasPending => PendingCount > 0;

    /// <summary>
    /// One digest over the pending decisions — the CI pinning value — computed over a canonical,
    /// deterministic serialization of the pending content only (no repository, worktree or path
    /// input, so a digest pinned on any machine matches the same pending content anywhere); null
    /// when nothing is pending.
    /// </summary>
    internal string? Digest
    {
        get
        {
            if (!_digestComputed)
            {
                _digest = ComputeDigest();
                _digestComputed = true;
            }

            return _digest;
        }
    }

    /// <summary>The pending decisions as the <c>--json</c> contract: extensions id-ordered, then
    /// each mcp.json with name-sorted servers, then the per-server approvals; the human-useful
    /// locations (directory, path, source) stay in this form — they help operators — but never
    /// enter the digest, and env is key names only, never values.
    /// </summary>
    internal JsonArray BuildPendingNode() =>
        BuildPendingNode(includeLocations: true, includeEnvValues: false);

    /// <summary>
    /// The canonical pending node. With <paramref name="includeLocations"/> false it carries
    /// content only — the digest input; the machine-local directory, path and source file are only
    /// added for the <c>--json</c> contract. <paramref name="includeEnvValues"/> keeps the raw env
    /// values in the digest (a value change must change it) and reduces env to key names in the
    /// JSON.
    /// </summary>
    private JsonArray BuildPendingNode(bool includeLocations, bool includeEnvValues)
    {
        var pending = new JsonArray();
        foreach (
            PendingExtension extension in Extensions.Where(extension =>
                extension.Decision is ExtensionTrustDecision.Prompt
            )
        )
        {
            var node = new JsonObject
            {
                ["kind"] = "extension",
                ["id"] = extension.Descriptor.Id,
                ["version"] = extension.Descriptor.Version,
            };
            if (includeLocations)
            {
                node["directory"] = extension.Descriptor.Directory;
            }

            node["filesChanged"] = extension.FilesChanged;
            node["hash"] = extension.ContentHash;
            pending.Add(node);
        }

        foreach (
            PendingMcpConfig config in ProjectMcpConfigs.Where(config =>
                config.Decision is ExtensionTrustDecision.Prompt
            )
        )
        {
            var servers = new JsonArray();
            foreach (
                McpServerOptions server in config.Servers.OrderBy(
                    server => server.Name,
                    StringComparer.Ordinal
                )
            )
            {
                servers.Add(McpConfigTrustHash.DefinitionNode(server, includeEnvValues));
            }

            var configNode = new JsonObject { ["kind"] = "mcp.json" };
            if (includeLocations)
            {
                configNode["path"] = config.Path;
            }

            configNode["servers"] = servers;
            configNode["hash"] = config.ContentHash;
            pending.Add(configNode);
        }

        foreach (PendingMcpServer server in McpServers)
        {
            var node = new JsonObject
            {
                ["kind"] = "mcpServer",
                ["server"] = McpConfigTrustHash.DefinitionNode(server.Definition, includeEnvValues),
            };
            if (includeLocations)
            {
                node["source"] = server.Definition.Source;
            }

            node["hash"] = server.Hash;
            pending.Add(node);
        }

        return pending;
    }

    private string? ComputeDigest()
    {
        if (!HasPending)
        {
            return null;
        }

        JsonArray canonical = BuildPendingNode(includeLocations: false, includeEnvValues: true);
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToJsonString()))
        );
    }

    internal static TrustInventory Create(Workspace workspace, ExtensionSeams? seams)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        string identity = workspace.GitCommonDir ?? workspace.WorktreeRoot;
        string worktree = workspace.WorktreeRoot;
        string storePath = seams?.StorePath ?? ExtensionHostOptions.Default.StorePath;
        var store = new ExtensionTrustStore(new ExtensionHostOptions { StorePath = storePath });
        IReadOnlyList<TrustRecord> records = store.Records(identity);
        bool filesChanged = records.Any(record => record.Section is TrustSection.Extensions);

        List<PendingExtension> extensions = [];
        if (seams?.NoExtensions is not true)
        {
            var loader = new ExtensionLoader(new ExtensionHostOptions { StorePath = storePath });
            loader.Discover(worktree, identity);
            foreach (ExtensionDescriptor descriptor in loader.Descriptors)
            {
                if (descriptor.Scope is not ExtensionScope.Project)
                {
                    continue;
                }

                string hash = ExtensionTrustStore.ComputeContentHash(descriptor.Directory);
                ExtensionTrustDecision decision = store.Evaluate(identity, worktree, hash);
                if (decision is ExtensionTrustDecision.Trusted)
                {
                    continue;
                }

                extensions.Add(new PendingExtension(descriptor, hash, filesChanged, decision));
            }

            extensions.Sort(
                (left, right) =>
                    StringComparer.Ordinal.Compare(left.Descriptor.Id, right.Descriptor.Id)
            );
        }

        List<PendingMcpConfig> mcpConfigs = [];
        List<PendingMcpServer> mcpServers = [];
        foreach (string projectConfig in McpComposition.ProjectConfigPaths(worktree))
        {
            if (!File.Exists(projectConfig))
            {
                continue;
            }

            McpConfigResult parsed = McpConfig.Load(projectConfig);
            string hash = McpConfigTrustHash.Compute(parsed.RawServers);
            ExtensionTrustDecision decision = store.EvaluateMcpConfig(identity, worktree, hash);
            if (decision is not ExtensionTrustDecision.Trusted)
            {
                mcpConfigs.Add(
                    new PendingMcpConfig(projectConfig, parsed.RawServers, hash, decision)
                );
            }

            foreach (McpServerOptions raw in parsed.RawServers)
            {
                string definitionHash = McpConfigTrustHash.ComputeDefinition(raw);
                if (
                    store.EvaluateMcpServer(identity, definitionHash)
                    is not ExtensionTrustDecision.Trusted
                )
                {
                    mcpServers.Add(new PendingMcpServer(raw, definitionHash));
                }
            }
        }

        return new TrustInventory(identity, worktree, records, extensions, mcpConfigs, mcpServers);
    }
}

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
/// The discovery and evaluation behind every trust decision: project extensions (directory hash)
/// and the project <c>mcp.json</c> (definition hash), evaluated against the store. It reads
/// descriptors and computes hashes only — no assembly load, no hook dispatch, no server start — so
/// the interactive session's question count and the trust command cannot drift.
/// </summary>
internal sealed class TrustInventory
{
    private TrustInventory(
        string repositoryIdentity,
        string worktreePath,
        IReadOnlyList<TrustRecord> records,
        IReadOnlyList<PendingExtension> extensions,
        PendingMcpConfig? mcpConfig
    )
    {
        RepositoryIdentity = repositoryIdentity;
        WorktreePath = worktreePath;
        Records = records;
        Extensions = extensions;
        ProjectMcpConfig = mcpConfig;
    }

    internal string RepositoryIdentity { get; }

    internal string WorktreePath { get; }

    /// <summary>This repository's records across both store sections.</summary>
    internal IReadOnlyList<TrustRecord> Records { get; }

    /// <summary>Every project extension that is not already trusted, id-ordered.</summary>
    internal IReadOnlyList<PendingExtension> Extensions { get; }

    /// <summary>The project <c>mcp.json</c> when one exists and is not already trusted.</summary>
    internal PendingMcpConfig? ProjectMcpConfig { get; }

    /// <summary>Decisions a session would ask about: unknown or changed hashes.</summary>
    internal int PendingCount =>
        Extensions.Count(extension => extension.Decision is ExtensionTrustDecision.Prompt)
        + (ProjectMcpConfig is { Decision: ExtensionTrustDecision.Prompt } ? 1 : 0);

    internal bool HasPending => PendingCount > 0;

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

        PendingMcpConfig? mcpConfig = null;
        string projectConfig = McpComposition.ProjectConfigPath(worktree);
        if (File.Exists(projectConfig))
        {
            McpConfigResult parsed = McpConfig.Load(projectConfig);
            string hash = ExtensionTrustStore.ComputeFileContentHash(projectConfig);
            ExtensionTrustDecision decision = store.EvaluateMcpConfig(identity, worktree, hash);
            if (decision is not ExtensionTrustDecision.Trusted)
            {
                mcpConfig = new PendingMcpConfig(projectConfig, parsed.Servers, hash, decision);
            }
        }

        return new TrustInventory(identity, worktree, records, extensions, mcpConfig);
    }
}

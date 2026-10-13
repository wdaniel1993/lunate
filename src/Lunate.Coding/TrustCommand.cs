using System.Text.Json;
using System.Text.Json.Nodes;
using Lunate.Extensibility;

namespace Lunate.Coding;

/// <summary>
/// The <c>lunate trust</c> command: inspect, approve or clear this repository's trust records
/// without loading extension code, dispatching hooks or starting MCP servers. Exit codes: 0
/// success, 1 unexpected failure, 2 usage, 3 inspect found pending decisions, 4 digest mismatch.
/// </summary>
internal static class TrustCommand
{
    internal const int ExitOk = 0;
    internal const int ExitFailure = 1;
    internal const int ExitUsage = 2;
    internal const int ExitPending = 3;
    internal const int ExitDigestMismatch = 4;

    internal const string Usage = "Usage: lunate trust [--yes [--digest <sha>]] [--json] [--clear]";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
    };

    internal static int Run(
        string[] args,
        string? workingDirectory,
        TextWriter output,
        TextWriter errors,
        ExtensionSeams? seams
    )
    {
        if (!TryParse(args, errors, out Options options))
        {
            return ExitUsage;
        }

        try
        {
            var workspace = new Workspace(workingDirectory ?? Environment.CurrentDirectory);
            TrustInventory inventory = TrustInventory.Create(workspace, seams);
            if (options.Clear)
            {
                return Clear(inventory, seams, output);
            }

            if (options.Json)
            {
                WriteJson(inventory, output);
                return inventory.HasPending ? ExitPending : ExitOk;
            }

            return options.Yes
                ? Approve(inventory, seams, options.Digest, output)
                : Inspect(inventory, output);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.WriteLine($"lunate: {exception.Message}");
            return ExitFailure;
        }
    }

    /// <summary>
    /// Inspect: this repository's records, every pending decision with the interactive question's
    /// content plus its hash, and the one pending digest. Records nothing; exit 3 when pending.
    /// </summary>
    private static int Inspect(TrustInventory inventory, TextWriter output)
    {
        WriteReport(inventory, output);
        if (inventory.HasPending)
        {
            output.WriteLine($"digest: {inventory.Digest}");
        }

        return inventory.HasPending ? ExitPending : ExitOk;
    }

    /// <summary>
    /// Approval: the same report, then the recording. The printed hashes are the recorded ones. A
    /// pinned digest gates the recording; a mismatch records nothing and exits 4.
    /// </summary>
    private static int Approve(
        TrustInventory inventory,
        ExtensionSeams? seams,
        string? pinned,
        TextWriter output
    )
    {
        WriteReport(inventory, output);
        if (
            inventory.Digest is { } computed
            && pinned is not null
            && !string.Equals(pinned, computed, StringComparison.Ordinal)
        )
        {
            output.WriteLine("trust: nothing was recorded");
            output.WriteLine($"pinned digest: {pinned}");
            output.WriteLine($"computed digest: {computed}");
            return ExitDigestMismatch;
        }

        ExtensionTrustStore store = Store(seams);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (PendingExtension extension in inventory.Extensions)
        {
            store.RecordTrusted(
                inventory.RepositoryIdentity,
                inventory.WorktreePath,
                extension.ContentHash,
                now
            );
            output.WriteLine(
                $"trust: recorded {RecordedLine(TrustSection.Extensions, inventory, extension.ContentHash)}"
            );
        }

        if (inventory.ProjectMcpConfig is { } config)
        {
            store.RecordMcpConfigTrusted(
                inventory.RepositoryIdentity,
                inventory.WorktreePath,
                config.ContentHash,
                now
            );
            output.WriteLine(
                $"trust: recorded {RecordedLine(TrustSection.McpConfig, inventory, config.ContentHash)}"
            );
        }

        if (inventory.Extensions.Count == 0 && inventory.ProjectMcpConfig is null)
        {
            output.WriteLine("trust: nothing to record");
        }

        return ExitOk;
    }

    /// <summary>Clear: removes this repository's records from both sections and prints them.</summary>
    private static int Clear(TrustInventory inventory, ExtensionSeams? seams, TextWriter output)
    {
        if (inventory.Records.Count == 0)
        {
            output.WriteLine("trust: no records to clear");
            return ExitOk;
        }

        Store(seams).ClearRepository(inventory.RepositoryIdentity);
        foreach (TrustRecord record in inventory.Records)
        {
            output.WriteLine($"trust: cleared {TrustFormat.RecordText(record)}");
        }

        return ExitOk;
    }

    /// <summary>The <c>--json</c> inspect contract: records, pending decisions and digest.</summary>
    private static void WriteJson(TrustInventory inventory, TextWriter output)
    {
        var records = new JsonArray();
        foreach (TrustRecord record in inventory.Records)
        {
            records.Add(
                new JsonObject
                {
                    ["kind"] = record.Section is TrustSection.McpConfig ? "mcp.json" : "extensions",
                    ["worktree"] = record.WorktreePath,
                    ["hash"] = record.ContentHash,
                }
            );
        }

        var document = new JsonObject
        {
            ["records"] = records,
            ["pending"] = inventory.BuildPendingNode(),
            ["digest"] = inventory.Digest,
        };
        output.Write(document.ToJsonString(JsonOptions));
        output.Write('\n');
    }

    private static void WriteReport(TrustInventory inventory, TextWriter output)
    {
        output.WriteLine(TrustFormat.RecordsText(inventory.Records));
        if (!inventory.HasPending)
        {
            output.WriteLine("trust: nothing pending");
            return;
        }

        output.WriteLine("pending:");
        foreach (
            PendingExtension extension in inventory.Extensions.Where(extension =>
                extension.Decision is ExtensionTrustDecision.Prompt
            )
        )
        {
            output.WriteLine(
                TrustFormat.ExtensionQuestion(extension.Descriptor, extension.FilesChanged)
            );
            output.WriteLine($"hash: {extension.ContentHash}");
        }

        if (inventory.ProjectMcpConfig is { Decision: ExtensionTrustDecision.Prompt } config)
        {
            output.WriteLine(TrustFormat.McpQuestionTitle(config.Path, config.Servers.Count));
            foreach (string detail in TrustFormat.McpQuestionDetails(config.Servers))
            {
                output.WriteLine($"  {detail}");
            }

            output.WriteLine($"hash: {config.ContentHash}");
        }
    }

    private static string RecordedLine(
        TrustSection section,
        TrustInventory inventory,
        string contentHash
    ) => TrustFormat.RecordText(new TrustRecord(section, inventory.WorktreePath, contentHash));

    private static ExtensionTrustStore Store(ExtensionSeams? seams) =>
        new(
            new ExtensionHostOptions
            {
                StorePath = seams?.StorePath ?? ExtensionHostOptions.Default.StorePath,
            }
        );

    private static bool TryParse(string[] args, TextWriter errors, out Options options)
    {
        options = new Options(false, null, false, false);
        var yes = false;
        string? digest = null;
        var json = false;
        var clear = false;
        for (var index = 0; index < args.Length; index++)
        {
            string arg = args[index];
            switch (arg)
            {
                case "--yes" when !yes:
                    yes = true;
                    break;
                case "--json" when !json:
                    json = true;
                    break;
                case "--clear" when !clear:
                    clear = true;
                    break;
                case "--digest"
                    when digest is null
                        && index + 1 < args.Length
                        && !string.IsNullOrWhiteSpace(args[index + 1]):
                    digest = args[++index];
                    break;
                default:
                    WriteUsage(errors);
                    return false;
            }
        }

        if (
            (yes && clear)
            || (json && (yes || clear || digest is not null))
            || (digest is not null && !yes)
        )
        {
            WriteUsage(errors);
            return false;
        }

        options = new Options(yes, digest, json, clear);
        return true;
    }

    private static void WriteUsage(TextWriter errors)
    {
        errors.WriteLine(Usage);
        errors.WriteLine(
            "lunate: --yes records the pending decisions (optionally gated by --digest <sha>), --json prints the inspection as one JSON document, --clear removes this repository's records."
        );
    }

    private sealed record Options(bool Yes, string? Digest, bool Json, bool Clear);
}

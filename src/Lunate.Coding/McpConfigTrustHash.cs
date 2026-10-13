using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Lunate.Protocols;

namespace Lunate.Coding;

/// <summary>
/// The project <c>mcp.json</c> trust hash: SHA-256 over the servers' raw definitions as parsed from
/// the file — name, command, args, env, cwd and call timeout as written, unknown fields excluded —
/// name-sorted and canonically serialized with invariant culture. Nothing expanded enters it, so no
/// environment value or secret can influence it, and formatting or key-order edits do not change
/// it.
/// </summary>
internal static class McpConfigTrustHash
{
    internal static string ComputeFile(string configPath) =>
        Compute(McpConfig.Load(configPath).Servers);

    internal static string Compute(IReadOnlyList<McpServerOptions> servers)
    {
        var definitions = new JsonArray();
        foreach (
            McpServerOptions server in servers.OrderBy(
                server => server.Name,
                StringComparer.Ordinal
            )
        )
        {
            definitions.Add(DefinitionNode(server));
        }

        return Hash(definitions);
    }

    /// <summary>One server's canonical raw definition; shared with the pending JSON contract.</summary>
    internal static JsonObject DefinitionNode(McpServerOptions server)
    {
        var arguments = new JsonArray();
        foreach (string argument in server.Arguments)
        {
            arguments.Add(argument);
        }

        JsonObject? environment = null;
        if (server.Environment is not null)
        {
            environment = new JsonObject();
            foreach (
                KeyValuePair<string, string> entry in server.Environment.OrderBy(
                    entry => entry.Key,
                    StringComparer.Ordinal
                )
            )
            {
                environment[entry.Key] = entry.Value;
            }
        }

        double? callTimeoutSeconds = server.CallTimeout?.TotalSeconds;
        return new JsonObject
        {
            ["name"] = server.Name,
            ["command"] = server.Command,
            ["args"] = arguments,
            ["env"] = environment,
            ["cwd"] = server.WorkingDirectory,
            ["callTimeoutSeconds"] = callTimeoutSeconds,
        };
    }

    internal static string Hash(JsonNode node) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(node.ToJsonString())));
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Lunate.Protocols;

namespace Lunate.Coding;

/// <summary>
/// The project <c>mcp.json</c> trust hash: SHA-256 over the servers' raw definitions as parsed from
/// the file — name, command, args, env, cwd and call timeout as written, unknown fields excluded —
/// name-sorted and canonically serialized with invariant culture. An absent <c>env</c> is
/// normalized to an empty map, so absent and <c>"env": {}</c> hash the same. Nothing expanded
/// enters it, so no environment value or secret can influence it, and formatting or key-order
/// edits do not change it.
/// </summary>
internal static class McpConfigTrustHash
{
    internal static string ComputeFile(string configPath) =>
        Compute(McpConfig.Load(configPath).RawServers);

    /// <summary>
    /// One server's canonical definition hash — the per-server approval key. Raw values as written;
    /// nothing expanded enters it.
    /// </summary>
    internal static string ComputeDefinition(McpServerOptions server) =>
        Hash(DefinitionNode(server, includeValues: true));

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
            definitions.Add(DefinitionNode(server, includeValues: true));
        }

        return Hash(definitions);
    }

    /// <summary>
    /// One server's canonical raw definition. With <paramref name="includeValues"/> the env map
    /// carries its values — the digest input, which must change with a value; without, env is the
    /// sorted key-name list (empty when none) — the inspectable <c>--json</c> form that never
    /// carries a value.
    /// </summary>
    internal static JsonObject DefinitionNode(McpServerOptions server, bool includeValues)
    {
        var arguments = new JsonArray();
        foreach (string argument in server.Arguments)
        {
            arguments.Add(argument);
        }

        JsonNode environment;
        if (includeValues)
        {
            var values = new JsonObject();
            if (server.Environment is not null)
            {
                foreach (
                    KeyValuePair<string, string> entry in server.Environment.OrderBy(
                        entry => entry.Key,
                        StringComparer.Ordinal
                    )
                )
                {
                    values[entry.Key] = entry.Value;
                }
            }

            environment = values;
        }
        else
        {
            var names = new JsonArray();
            if (server.Environment is not null)
            {
                foreach (
                    string name in server.Environment.Keys.OrderBy(
                        name => name,
                        StringComparer.Ordinal
                    )
                )
                {
                    names.Add(name);
                }
            }

            environment = names;
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

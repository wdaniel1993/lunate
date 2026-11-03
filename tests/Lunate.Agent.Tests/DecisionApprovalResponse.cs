using System.Text.Json;

namespace Lunate.Agent.Tests;

internal static class DecisionApprovalResponse
{
    private static readonly string[] SignalIds =
    [
        "writes_outside_workspace",
        "network_access",
        "irreversible_deletion",
        "credential_exposure",
    ];

    internal static string Create(
        string? affirmativeSignal = null,
        double affirmativeProbability = 0.1
    )
    {
        var results = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (string signal in SignalIds)
        {
            double yesProbability = signal == affirmativeSignal ? affirmativeProbability : 0.1;
            results.Add(
                signal,
                new
                {
                    answer = yesProbability >= 0.5 ? "yes" : "no",
                    probabilities = new Dictionary<string, double>(StringComparer.Ordinal)
                    {
                        ["yes"] = yesProbability,
                        ["no"] = 1 - yesProbability,
                    },
                }
            );
        }

        return JsonSerializer.Serialize(new { results });
    }
}

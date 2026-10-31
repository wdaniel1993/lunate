using System.Globalization;
using System.Text.Json;
using Lunate.Contracts;

namespace Lunate.Agent.Tests;

public sealed class DecisionCultureTests
{
    [Fact]
    public async Task Adapter_serializes_json_numbers_independently_of_machine_culture()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-AT");
        try
        {
            var client = new ScriptedChatClient().Enqueue(
                LoopScripts.Text(
                    """{"results":{"confirm":{"answer":"yes","probabilities":{"yes":0.7,"no":0.3}}}}"""
                ),
                LoopScripts.Stop()
            );
            var request = new DecisionRequest(
                JsonSerializer.SerializeToElement(new { score = 0.7d }),
                [new DecisionQuestion("confirm", "Is it ready?", DecisionQuestionKind.YesNo)]
            );

            DecisionResponse result = await DecisionEngine.EvaluateAsync(
                request,
                client,
                "decision-model",
                "adapter",
                TestContext.Current.CancellationToken
            );

            Assert.Contains("\"score\":0.7", client.Requests.Single().Messages[1].Text, StringComparison.Ordinal);
            Assert.Equal(0.7d, result.Results["confirm"].Probability);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}

namespace Lunate.Ai.Decisions;

internal static class DecisionResultValidator
{
    public static DecisionAnswer Validate(
        DecisionQuestion question,
        string? answer,
        IReadOnlyDictionary<string, double> probabilities,
        string modelId,
        string method
    )
    {
        IReadOnlyList<string> definedAnswers = question.Answers!;
        if (!definedAnswers.Contains(answer, StringComparer.Ordinal))
        {
            throw Invalid(question.Id, "the chosen answer is not defined by the question");
        }
        if (probabilities.Count != definedAnswers.Count)
        {
            throw Invalid(
                question.Id,
                "the distribution must contain every defined answer exactly once"
            );
        }

        List<DecisionProbability> distribution = [];
        double sum = 0d;
        foreach (string definedAnswer in definedAnswers)
        {
            if (!probabilities.TryGetValue(definedAnswer, out double probability))
            {
                throw Invalid(question.Id, $"the distribution is missing '{definedAnswer}'");
            }
            if (!double.IsFinite(probability) || probability is < 0d or > 1d)
            {
                throw Invalid(
                    question.Id,
                    $"probability for '{definedAnswer}' must be finite and between 0 and 1"
                );
            }

            sum += probability;
            distribution.Add(new DecisionProbability(definedAnswer, probability));
        }
        if (Math.Abs(sum - 1d) > 0.000001d)
        {
            throw Invalid(question.Id, "the probabilities must sum to 1 within 0.000001");
        }

        double confidence = question.Kind switch
        {
            DecisionQuestionKind.Noul => Math.Abs(2d * probabilities["yes"] - 1d),
            DecisionQuestionKind.Choice => ChoiceConfidence(distribution),
            DecisionQuestionKind.Score => ScoreConfidence(distribution),
            _ => throw new InvalidOperationException("The question kind is unsupported."),
        };
        return new DecisionAnswer(
            answer!,
            probabilities[answer!],
            distribution,
            modelId,
            method,
            confidence
        );
    }

    private static double ChoiceConfidence(IReadOnlyList<DecisionProbability> distribution)
    {
        double maximum = distribution.Max(answer => answer.Probability);
        double count = distribution.Count;
        return (maximum - 1d / count) / (1d - 1d / count);
    }

    private static double ScoreConfidence(IReadOnlyList<DecisionProbability> distribution)
    {
        int count = distribution.Count;
        int mode = Enumerable.Range(0, count).MaxBy(index => distribution[index].Probability);
        double meanAbsoluteDeviation = Enumerable
            .Range(0, count)
            .Average(index => Math.Abs(index - (count - 1d) / 2d));
        double expectedDeviation = Enumerable
            .Range(0, count)
            .Sum(index => distribution[index].Probability * Math.Abs(index - mode));
        return Math.Max(0d, 1d - expectedDeviation / meanAbsoluteDeviation);
    }

    private static InvalidDataException Invalid(string questionId, string reason) =>
        new($"Decision question '{questionId}' is off-contract: {reason}.");
}

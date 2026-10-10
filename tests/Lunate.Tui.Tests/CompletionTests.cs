namespace Lunate.Tui.Tests;

public sealed class CompletionTests
{
    private static readonly string[] Commands = ["/compact", "/model", "/new", "/quit", "/resume"];

    [Fact]
    public void A_single_match_completes_to_the_full_command()
    {
        CompletionResult result = Completion.Complete("/comp", Commands);

        Assert.True(result.IsReplacement);
        Assert.Equal("/compact", result.Replacement);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Several_matches_complete_to_the_longest_common_prefix()
    {
        CompletionResult result = Completion.Complete("/mod", ["/model", "/models"]);

        Assert.True(result.IsReplacement);
        Assert.Equal("/model", result.Replacement);
    }

    [Fact]
    public void An_exact_word_with_several_matches_shows_the_candidates()
    {
        CompletionResult result = Completion.Complete("/model", ["/models", "/model"]);

        Assert.False(result.IsReplacement);
        Assert.Equal(["/model", "/models"], result.Candidates);
    }

    [Fact]
    public void No_longer_prefix_lists_the_sorted_candidates()
    {
        CompletionResult result = Completion.Complete("/", ["/model", "/compact", "/new"]);

        Assert.False(result.IsReplacement);
        Assert.Equal(["/compact", "/model", "/new"], result.Candidates);
    }

    [Fact]
    public void No_match_is_a_no_op()
    {
        CompletionResult result = Completion.Complete("/zzz", Commands);

        Assert.False(result.IsReplacement);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Matching_is_case_sensitive_and_ordinal()
    {
        Assert.Empty(Completion.Complete("/MOD", Commands).Candidates);
        Assert.Null(Completion.Complete("/MOD", Commands).Replacement);
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => Completion.Complete(null!, Commands));
        Assert.Throws<ArgumentNullException>(() => Completion.Complete("/m", null!));
    }
}

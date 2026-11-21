namespace Toolkit.Vault.Tests;

/// <summary>Word splitting is shared by every adapter, so it's tested once, here.</summary>
public class SearchTextTests
{
    [Theory]
    [InlineData("retry polly", new[] { "retry", "polly" })]
    [InlineData("C# foo-bar", new[] { "C", "foo", "bar" })]
    [InlineData("say \"hi\" NOT", new[] { "say", "hi", "NOT" })]
    [InlineData("café 2026", new[] { "café", "2026" })]
    public void Splits_on_anything_that_is_not_a_letter_or_digit(string text, string[] expected) =>
        Assert.Equal(expected, SearchText.Words(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#*-()\"")]
    public void Returns_no_words_for_blank_or_punctuation_only_text(string? text) =>
        Assert.Empty(SearchText.Words(text));
}

using Toolkit.Vault.Sqlite;

namespace Toolkit.Vault.Tests;

public class FtsQueryTests
{
    [Fact]
    public void Quotes_each_word_and_allows_whole_or_prefix_matches() =>
        Assert.Equal("(\"retry\" OR \"retry\"*) AND (\"polly\" OR \"polly\"*)", FtsQuery.Build("retry polly"));

    [Theory]
    [InlineData("C#", "(\"C\" OR \"C\"*)")]
    [InlineData("foo-bar", "(\"foo\" OR \"foo\"*) AND (\"bar\" OR \"bar\"*)")]
    [InlineData("say \"hi\"", "(\"say\" OR \"say\"*) AND (\"hi\" OR \"hi\"*)")]
    [InlineData("café", "(\"café\" OR \"café\"*)")]
    public void Treats_punctuation_as_a_separator(string text, string expected) =>
        Assert.Equal(expected, FtsQuery.Build(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#*-()\"")]
    public void Returns_null_when_there_is_nothing_to_search_for(string? text) =>
        Assert.Null(FtsQuery.Build(text));
}

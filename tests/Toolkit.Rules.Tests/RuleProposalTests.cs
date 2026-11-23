namespace Toolkit.Rules.Tests;

public class RuleProposalPromptTests
{
    [Fact]
    public void Build_lists_every_description_once_and_asks_for_json_only()
    {
        var prompt = RuleProposalPrompt.Build(["COUNTDOWN SUPERMARKET", "UBER *TRIP"]);

        Assert.Contains("COUNTDOWN SUPERMARKET", prompt);
        Assert.Contains("UBER *TRIP", prompt);
        Assert.Contains("JSON array", prompt);
        Assert.Contains("propose", prompt, StringComparison.OrdinalIgnoreCase); // asks for rules, not per-row categories
    }
}

public class RuleProposalParserTests
{
    [Fact]
    public void Parses_a_clean_json_array()
    {
        var rules = RuleProposalParser.Parse("""[{"pattern": "COUNTDOWN", "category": "Groceries"}]""");

        Assert.Equal([new CategoryRule("COUNTDOWN", "Groceries")], rules);
    }

    [Fact]
    public void Parses_json_wrapped_in_a_markdown_fence_with_surrounding_prose()
    {
        var reply = """
            Sure, here are the rules you asked for:
            ```json
            [{"pattern": "COUNTDOWN", "category": "Groceries"}, {"pattern": "UBER", "category": "Transport"}]
            ```
            Let me know if you'd like more.
            """;

        var rules = RuleProposalParser.Parse(reply);

        Assert.Equal(2, rules.Count);
        Assert.Equal("Groceries", rules[0].Category);
        Assert.Equal("Transport", rules[1].Category);
    }

    [Fact]
    public void Matches_pattern_and_category_case_insensitively_in_json()
    {
        var rules = RuleProposalParser.Parse("""[{"Pattern": "KMART", "Category": "Shopping"}]""");

        Assert.Equal([new CategoryRule("KMART", "Shopping")], rules);
    }

    [Fact]
    public void Drops_items_missing_a_pattern_or_category()
    {
        var rules = RuleProposalParser.Parse(
            """[{"pattern": "", "category": "Groceries"}, {"pattern": "KMART"}, {"pattern": "BUNNINGS", "category": "Shopping"}]""");

        Assert.Equal([new CategoryRule("BUNNINGS", "Shopping")], rules);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Sorry, I don't understand.")]
    [InlineData("[not valid json")]
    [InlineData(null)]
    public void Returns_an_empty_list_for_anything_unusable(string? reply) =>
        Assert.Empty(RuleProposalParser.Parse(reply));
}

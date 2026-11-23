namespace Toolkit.Rules.Tests;

public class CategoryRuleTests
{
    [Theory]
    [InlineData("COUNTDOWN SUPERMARKET AUCKLAND", "countdown", true)]
    [InlineData("countdown supermarket auckland", "COUNTDOWN", true)] // case-insensitive both ways
    [InlineData("NEW WORLD", "countdown", false)]
    public void Matches_is_a_case_insensitive_substring_by_default(string description, string pattern, bool expected) =>
        Assert.Equal(expected, new CategoryRule(pattern, "Groceries").Matches(description));

    [Theory]
    [InlineData("UBER *TRIP HELP.UBER.COM", @"^UBER \*TRIP", true)]
    [InlineData("UBER EATS", @"^UBER \*TRIP", false)]
    public void Matches_uses_regex_when_IsRegex_is_set(string description, string pattern, bool expected) =>
        Assert.Equal(expected, new CategoryRule(pattern, "Transport", IsRegex: true).Matches(description));
}

public class RuleSetTests
{
    private static Transaction Tx(string description, decimal amount = -10m) =>
        new(new DateOnly(2026, 1, 1), description, amount);

    [Fact]
    public void Match_returns_the_first_rule_that_matches()
    {
        var rules = new RuleSet([
            new CategoryRule("COUNTDOWN", "Groceries"),
            new CategoryRule("COUNT", "Everything else"), // would also match, but comes second
        ]);

        var match = rules.Match(Tx("COUNTDOWN SUPERMARKET"));

        Assert.Equal("Groceries", match?.Category);
    }

    [Fact]
    public void Match_returns_null_when_nothing_matches()
    {
        var rules = new RuleSet([new CategoryRule("COUNTDOWN", "Groceries")]);

        Assert.Null(rules.Match(Tx("KMART")));
    }

    [Fact]
    public void Categorize_reports_the_match_rate_across_a_batch()
    {
        var rules = new RuleSet([new CategoryRule("COUNTDOWN", "Groceries")]);
        var transactions = new[] { Tx("COUNTDOWN"), Tx("COUNTDOWN"), Tx("KMART"), Tx("BUNNINGS") };

        var summary = rules.Categorize(transactions);

        Assert.Equal(4, summary.Total);
        Assert.Equal(2, summary.Categorized);
        Assert.Equal(0.5, summary.CategorizedRate, precision: 5);
        Assert.Equal(2, summary.Uncategorized.Count);
    }

    [Fact]
    public void Categorize_of_an_empty_batch_counts_as_fully_categorised()
    {
        var summary = RuleSet.Empty.Categorize([]);

        Assert.Equal(1.0, summary.CategorizedRate);
    }

    [Fact]
    public void UnmatchedDescriptions_orders_by_frequency_and_deduplicates_case_and_whitespace()
    {
        var transactions = new[]
        {
            Tx("KMART"), Tx("kmart "), Tx(" Kmart"), // same payee, three different ways
            Tx("BUNNINGS"),
        };

        var unmatched = RuleSet.Empty.Categorize(transactions).UnmatchedDescriptions(limit: 10);

        Assert.Equal(2, unmatched.Count);
        Assert.Equal("KMART", unmatched[0], ignoreCase: true); // 3 occurrences, ranks first
    }

    [Fact]
    public void UnmatchedDescriptions_respects_the_limit()
    {
        var transactions = Enumerable.Range(0, 5).Select(i => Tx($"SHOP {i}")).ToArray();

        var unmatched = RuleSet.Empty.Categorize(transactions).UnmatchedDescriptions(limit: 2);

        Assert.Equal(2, unmatched.Count);
    }

    [Fact]
    public void With_appends_rules_not_already_covered_and_keeps_existing_priority()
    {
        var original = new RuleSet([new CategoryRule("COUNTDOWN", "Groceries")]);

        var merged = original.With([
            new CategoryRule("countdown", "Duplicate, should be dropped"), // same pattern, different case
            new CategoryRule("BUNNINGS", "Shopping"),
        ]);

        Assert.Equal(2, merged.Rules.Count);
        Assert.Equal("Groceries", merged.Match(Tx("COUNTDOWN"))?.Category);
        Assert.Equal("Shopping", merged.Match(Tx("BUNNINGS"))?.Category);
    }
}

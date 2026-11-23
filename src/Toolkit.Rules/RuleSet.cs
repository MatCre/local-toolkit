namespace Toolkit.Rules;

/// <summary>An ordered list of rules. The first one whose pattern matches a transaction wins.</summary>
public sealed class RuleSet(IReadOnlyList<CategoryRule> rules)
{
    public static readonly RuleSet Empty = new([]);

    public IReadOnlyList<CategoryRule> Rules => rules;

    public CategoryRule? Match(Transaction transaction) =>
        rules.FirstOrDefault(rule => rule.Matches(transaction.Description));

    public CategorizationSummary Categorize(IEnumerable<Transaction> transactions) =>
        new(transactions.Select(t => new CategorizedTransaction(t, Match(t))).ToList());

    /// <summary>
    /// Adds rules not already covered by an existing one (same pattern and <see cref="CategoryRule.IsRegex"/>,
    /// compared case-insensitively). Existing rules keep their position and priority; new ones are appended.
    /// </summary>
    public RuleSet With(IEnumerable<CategoryRule> additional)
    {
        var merged = new List<CategoryRule>(rules);
        foreach (var rule in additional)
        {
            var alreadyPresent = merged.Any(existing =>
                existing.IsRegex == rule.IsRegex &&
                string.Equals(existing.Pattern, rule.Pattern, StringComparison.OrdinalIgnoreCase));

            if (!alreadyPresent)
            {
                merged.Add(rule);
            }
        }

        return new RuleSet(merged);
    }
}

/// <summary>A transaction together with the rule that matched it, if any.</summary>
public sealed record CategorizedTransaction(Transaction Transaction, CategoryRule? MatchedRule)
{
    public bool IsCategorized => MatchedRule is not null;

    public string? Category => MatchedRule?.Category;
}

/// <summary>The result of running a <see cref="RuleSet"/> over a batch of transactions.</summary>
public sealed class CategorizationSummary(IReadOnlyList<CategorizedTransaction> results)
{
    public IReadOnlyList<CategorizedTransaction> Results => results;

    public int Total => results.Count;

    public int Categorized => results.Count(r => r.IsCategorized);

    /// <summary>Fraction of rows a rule matched, from 0 to 1. An empty batch counts as fully categorised.</summary>
    public double CategorizedRate => Total == 0 ? 1.0 : (double)Categorized / Total;

    public IReadOnlyList<CategorizedTransaction> Uncategorized => results.Where(r => !r.IsCategorized).ToList();

    /// <summary>
    /// Distinct, trimmed descriptions with no matching rule, most frequent first — what's worth
    /// asking the model about, since a rule for one of these covers every row with that description.
    /// </summary>
    public IReadOnlyList<string> UnmatchedDescriptions(int limit) =>
        Uncategorized
            .GroupBy(r => r.Transaction.Description.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(limit)
            .ToList();
}

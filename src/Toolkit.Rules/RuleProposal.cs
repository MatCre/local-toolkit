using System.Text;
using System.Text.Json;

namespace Toolkit.Rules;

/// <summary>
/// Builds the one-shot prompt that asks a model to propose rules, and parses its reply. Both are
/// plain string/text logic with no AI package dependency, so they're tested without a model: the
/// AI call itself is one line in Toolkit.Rules.Cli, the composition root.
/// </summary>
public static class RuleProposalPrompt
{
    /// <summary>
    /// A prompt listing every unmatched payee once. Deliberately asks for rules, not categories
    /// for the transactions themselves: the model proposes a pattern once, and plain C# then
    /// decides every row's category by running <see cref="CategoryRule.Matches"/>, not the model.
    /// </summary>
    public static string Build(IReadOnlyList<string> unmatchedDescriptions)
    {
        var builder = new StringBuilder()
            .AppendLine("You help categorise personal bank transactions.")
            .AppendLine("Below are transaction descriptions that no existing rule matches. For each one, propose a rule:")
            .AppendLine("a short literal substring that appears in the description, and a category for it.")
            .AppendLine()
            .AppendLine("Reply with ONLY a JSON array, no other text, no markdown fences. Each item:")
            .AppendLine("""{"pattern": "...", "category": "..."}""")
            .AppendLine()
            .AppendLine("The pattern must be plain text that literally appears in the description (not a regular")
            .AppendLine("expression), case doesn't matter. Prefer short, general categories, for example:")
            .AppendLine("Groceries, Dining, Transport, Utilities, Subscriptions, Rent, Income, Fees, Health, Shopping.")
            .AppendLine()
            .AppendLine("Descriptions:");

        foreach (var description in unmatchedDescriptions)
        {
            builder.Append("- ").AppendLine(description);
        }

        return builder.ToString();
    }
}

/// <summary>Turns a model's reply into rules, or nothing usable into an empty list.</summary>
public static class RuleProposalParser
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Finds the first <c>[ ... ]</c> span in the text and parses it. Models sometimes wrap the
    /// requested JSON in a markdown code fence or add a sentence before or after it, so this
    /// looks for the array rather than requiring the whole reply to be nothing else.
    /// </summary>
    public static List<CategoryRule> Parse(string? modelOutput)
    {
        if (string.IsNullOrWhiteSpace(modelOutput))
        {
            return [];
        }

        var start = modelOutput.IndexOf('[');
        var end = modelOutput.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            return [];
        }

        List<ProposedRule>? items;
        try
        {
            items = JsonSerializer.Deserialize<List<ProposedRule>>(modelOutput[start..(end + 1)], JsonOptions);
        }
        catch (JsonException)
        {
            return [];
        }

        return (items ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Pattern) && !string.IsNullOrWhiteSpace(item.Category))
            .Select(item => new CategoryRule(item.Pattern.Trim(), item.Category.Trim()))
            .ToList();
    }

    private sealed record ProposedRule(string Pattern, string Category);
}

using System.Text.RegularExpressions;

namespace Toolkit.Rules;

/// <summary>
/// Matches a transaction's description to a category. <paramref name="Pattern"/> is a plain,
/// case-insensitive substring unless <paramref name="IsRegex"/> is set, in which case it's a
/// .NET regular expression. Proposed rules (see <see cref="RuleProposalParser"/>) are always
/// plain substrings: the model is asked for literal text that appears in the description, not
/// a pattern language it could get subtly wrong.
/// </summary>
public sealed record CategoryRule(string Pattern, string Category, bool IsRegex = false)
{
    public bool Matches(string description) => IsRegex
        ? Regex.IsMatch(description, Pattern, RegexOptions.IgnoreCase)
        : description.Contains(Pattern, StringComparison.OrdinalIgnoreCase);
}

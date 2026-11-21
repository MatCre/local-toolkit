using System.Text.RegularExpressions;

namespace Toolkit.Vault;

/// <summary>
/// The provider-neutral half of search: how typed text becomes words. Every adapter splits text
/// the same way, then builds its own engine's query from the words (FTS5 MATCH for SQLite,
/// CONTAINS for SQL Server, and so on). That keeps "what counts as a word" identical everywhere.
/// </summary>
public static partial class SearchText
{
    /// <summary>
    /// Runs of letters and digits; everything else separates words. "C# foo-bar" gives
    /// "C", "foo", "bar". Operators such as NOT come back as ordinary words.
    /// </summary>
    public static IReadOnlyList<string> Words(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : Word().Matches(text).Select(m => m.Value).ToList();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();
}

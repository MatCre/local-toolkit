namespace Toolkit.Vault.Sqlite;

/// <summary>
/// Builds a safe SQLite FTS5 query from typed text. Text passed straight to MATCH is parsed as
/// FTS5 syntax, so "C#", "foo-bar" or a stray quote would throw a syntax error, and NOT or OR
/// would change the meaning. Here every word is quoted, so it can only ever be a plain term.
/// </summary>
/// <example>
/// <c>retry polly</c> becomes <c>("retry" OR "retry"*) AND ("polly" OR "polly"*)</c>.
/// <list type="bullet">
/// <item>Every word must match. The AND has to be written out: FTS5 treats plain words side by
/// side as AND, but bracketed groups side by side are a syntax error.</item>
/// <item><c>"word"</c> matches the whole word, via its stem (porter): "retries" also finds "retry".</item>
/// <item><c>"word"*</c> matches as a prefix, so a half-typed "sql" finds "sqlite".</item>
/// </list>
/// Splitting text into words is shared by every adapter: see <see cref="SearchText.Words"/>.
/// </example>
public static class FtsQuery
{
    /// <summary>Returns null when the text contains no searchable words.</summary>
    public static string? Build(string? text)
    {
        var words = SearchText.Words(text);
        return words.Count == 0
            ? null
            : string.Join(" AND ", words.Select(w => $"(\"{w}\" OR \"{w}\"*)"));
    }
}

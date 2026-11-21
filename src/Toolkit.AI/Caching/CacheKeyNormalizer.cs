using System.Text;
using System.Text.RegularExpressions;

namespace Toolkit.AI.Caching;

/// <summary>
/// Reduces prompt text to a canonical form so that trivially different wordings share one
/// cache entry. Used only to build the cache key: the model always receives the original text.
/// </summary>
/// <remarks>
/// Each step trades a little precision for more cache hits:
/// <list type="number">
/// <item>Unicode NFKC: full-width letters, ligatures and other look-alike forms become plain ones.</item>
/// <item>Lower case: "SQLite" and "sqlite" match. Rarely, case carries meaning ("US" vs "us", code identifiers).</item>
/// <item>Whitespace runs become one space, and ends are trimmed. Indentation in code (Python) is lost from the key.</item>
/// <item>Trailing . ? ! are dropped: "What is SQLite" and "What is SQLite?" match.</item>
/// </list>
/// Turn it off with ToolkitAI:Cache:NormalizeKeys=false when those losses matter.
/// </remarks>
public static partial class CacheKeyNormalizer
{
    public static string Normalize(string text)
    {
        var result = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        result = Whitespace().Replace(result, " ").Trim();
        return result.TrimEnd('.', '?', '!', ' ');
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

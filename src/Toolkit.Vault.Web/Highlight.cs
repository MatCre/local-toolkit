using System.Net;
using Microsoft.AspNetCore.Components;

namespace Toolkit.Vault.Web;

internal static class Highlight
{
    /// <summary>
    /// Turns a search snippet into HTML with matched words in &lt;mark&gt;. The text is HTML-encoded
    /// first and only then are the vault's highlight markers swapped for tags, so nothing stored in
    /// the vault can inject markup into the page.
    /// </summary>
    public static MarkupString Snippet(string snippet) =>
        new(WebUtility.HtmlEncode(snippet)
            .Replace(VaultSearchResult.HighlightStart, "<mark>")
            .Replace(VaultSearchResult.HighlightEnd, "</mark>"));
}

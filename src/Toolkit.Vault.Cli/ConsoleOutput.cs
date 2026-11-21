using System.Text.RegularExpressions;

namespace Toolkit.Vault.Cli;

/// <summary>
/// Console formatting. Uses ANSI colour codes when writing to a terminal, and plain text when
/// output is redirected to a file or another program, or when NO_COLOR is set (no-color.org).
/// </summary>
internal static partial class ConsoleOutput
{
    private static readonly bool UseColor =
        !Console.IsOutputRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null;

    public static void Line(string text) => Console.WriteLine(text);

    public static int Error(string message)
    {
        Console.Error.WriteLine(Paint(message, "31")); // red
        return 1;
    }

    public static string Id(long id) => Paint($"#{id,-4}", "36"); // cyan, padded so titles line up

    public static string Bold(string text) => Paint(text, "1");

    public static string Dim(string text) => Paint(text, "2");

    /// <summary>Puts a search excerpt on one line and shows the matched words in yellow.</summary>
    public static string Snippet(string snippet)
    {
        var oneLine = Whitespace().Replace(snippet, " ").Trim();
        return oneLine
            .Replace(VaultSearchResult.HighlightStart, UseColor ? "\e[1;33m" : "")
            .Replace(VaultSearchResult.HighlightEnd, UseColor ? "\e[0m" : "");
    }

    private static string Paint(string text, string code) => UseColor ? $"\e[{code}m{text}\e[0m" : text;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

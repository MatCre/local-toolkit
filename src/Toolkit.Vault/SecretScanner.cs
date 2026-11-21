using System.Text.RegularExpressions;

namespace Toolkit.Vault;

/// <summary>One thing in some text that looks like a secret.</summary>
/// <param name="Kind">What it looks like, e.g. "API key (sk-…)".</param>
/// <param name="Masked">Enough of the value to recognise it, never the whole value.</param>
public sealed record SecretFinding(string Kind, string Masked);

/// <summary>
/// Spots text that looks like a credential before it's stored. Vault entries are plain text and
/// are printed in search results, so a key saved by mistake (a stale clipboard, a pasted config
/// file, a prompt captured automatically) would be shown on screen and kept on disk.
/// </summary>
/// <remarks>
/// <para>
/// Two kinds of check. <b>Known formats</b>: prefixes that providers put on their keys on purpose
/// so leaks can be detected (sk-, ghp_, AKIA, AIza, xox…), private key blocks, JWTs, and passwords
/// in connection strings. <b>Random-looking tokens</b>: a long unbroken run of letters and digits
/// with high Shannon entropy, the way generated keys look. Git hashes (hex, so low entropy) and
/// GUIDs (split up by their hyphens) are left alone: they're common in notes and aren't secrets.
/// </para>
/// <para>
/// This is a safety net, not a guarantee. It can miss a secret in an unusual format, and it can
/// flag harmless random text; the caller can override it deliberately.
/// </para>
/// </remarks>
public static partial class SecretScanner
{
    private static readonly (string Kind, Regex Pattern)[] KnownFormats =
    [
        ("API key (sk-…)", SkKey()),
        ("GitHub token", GitHubToken()),
        ("AWS access key", AwsAccessKey()),
        ("Google API key", GoogleApiKey()),
        ("Slack token", SlackToken()),
        ("private key", PrivateKeyBlock()),
        ("JSON Web Token", Jwt()),
        ("password in a connection string", ConnectionStringPassword()),
    ];

    private const int MinRandomTokenLength = 32;
    private const double MinRandomTokenEntropy = 4.2; // bits per character; hex tops out at 4.0

    public static IReadOnlyList<SecretFinding> Scan(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var findings = new List<SecretFinding>();
        var flagged = new List<(int Start, int End)>();

        foreach (var (kind, pattern) in KnownFormats)
        {
            foreach (Match match in pattern.Matches(text))
            {
                var value = match.Groups["secret"].Success ? match.Groups["secret"].Value : match.Value;
                findings.Add(new SecretFinding(kind, Mask(value)));
                flagged.Add((match.Index, match.Index + match.Length));
            }
        }

        foreach (Match match in Token().Matches(text))
        {
            var overlapsKnown = flagged.Any(f => match.Index < f.End && f.Start < match.Index + match.Length);
            if (!overlapsKnown && LooksRandom(match.Value))
            {
                findings.Add(new SecretFinding("random-looking token", Mask(match.Value)));
            }
        }

        return findings;
    }

    /// <summary>Shows the start and end only: "sk-or-v…a34". Short values are hidden entirely.</summary>
    public static string Mask(string value) =>
        value.Length < 12 ? new string('*', value.Length) : $"{value[..Math.Min(7, value.Length / 3)]}…{value[^3..]}";

    private static bool LooksRandom(string token) =>
        token.Length >= MinRandomTokenLength
        && token.Any(char.IsLetter)
        && token.Any(char.IsDigit)
        && ShannonEntropy(token) >= MinRandomTokenEntropy;

    /// <summary>Average bits of information per character. English words score about 3; generated keys 5 or more.</summary>
    private static double ShannonEntropy(string text) =>
        -text.GroupBy(c => c)
            .Select(g => (double)g.Count() / text.Length)
            .Sum(p => p * Math.Log2(p));

    [GeneratedRegex(@"\bsk-[A-Za-z0-9][A-Za-z0-9_\-]{19,}")]
    private static partial Regex SkKey();

    [GeneratedRegex(@"\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{22,})")]
    private static partial Regex GitHubToken();

    [GeneratedRegex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b")]
    private static partial Regex AwsAccessKey();

    [GeneratedRegex(@"\bAIza[0-9A-Za-z_\-]{35}")]
    private static partial Regex GoogleApiKey();

    [GeneratedRegex(@"\bxox[abprs]-[A-Za-z0-9\-]{10,}")]
    private static partial Regex SlackToken();

    [GeneratedRegex(@"-----BEGIN (?:[A-Z]+ )*PRIVATE KEY-----")]
    private static partial Regex PrivateKeyBlock();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_\-]{10,}\.eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"(?i)\b(?:password|pwd)\s*=\s*(?<secret>[^;\s""']{4,})")]
    private static partial Regex ConnectionStringPassword();

    // Candidates for the randomness check: base64-ish runs. Hyphens end a candidate, so readable
    // hyphenated names (model IDs, package names) are split into short, harmless pieces. Keys with
    // hyphens in them (sk-…, ghp_…) are caught by the known formats above instead.
    [GeneratedRegex(@"[A-Za-z0-9+/_]{32,}={0,2}")]
    private static partial Regex Token();
}

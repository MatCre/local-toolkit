namespace Toolkit.Vault;

/// <summary>
/// What you supply to add something to the vault. The Normalized* properties hold the input rules
/// (trimming, default title, tag splitting), so every storage adapter applies them the same way.
/// </summary>
/// <param name="Title">Short name to recognise it by. Leave empty to use the first line of the body.</param>
/// <param name="Body">The answer, snippet or note itself.</param>
/// <param name="Source">Where it came from: "claude", "manual", a URL, a file path.</param>
/// <param name="Tags">Labels such as "dotnet" or "sqlite". Commas and spaces separate tags; stored lower case.</param>
public sealed record NewVaultEntry(string? Title, string Body, string? Source = null, IReadOnlyList<string>? Tags = null)
{
    private const int MaxTitleLength = 80;

    /// <summary>
    /// Store the entry even if it looks like it contains a secret. Off by default: entries are plain
    /// text and appear in search results, so credentials belong in user secrets or a password manager.
    /// </summary>
    public bool AllowSecrets { get; init; }

    /// <summary>
    /// An id from wherever the entry came from, e.g. "claude:conversation:&lt;uuid&gt;". At most one entry
    /// can have a given external id, so an import run twice doesn't create duplicates.
    /// </summary>
    public string? ExternalId { get; init; }

    /// <summary>When the content was originally created. Defaults to now; an import sets the original date.</summary>
    public DateTimeOffset? CreatedUtc { get; init; }

    /// <summary>
    /// Checks the entry against the vault's rules. Every <see cref="IVaultStore"/> calls this before
    /// storing anything, so the rules are the same whatever the database.
    /// </summary>
    /// <exception cref="ArgumentException">The body is empty.</exception>
    /// <exception cref="SecretDetectedException">The text looks like it contains a secret and <see cref="AllowSecrets"/> is off.</exception>
    public void Validate()
    {
        if (NormalizedBody.Length == 0)
        {
            throw new ArgumentException("An entry needs a body.", nameof(Body));
        }

        if (!AllowSecrets)
        {
            var findings = SecretScanner.Scan($"{Title}\n{Body}\n{Source}");
            if (findings.Count > 0)
            {
                throw new SecretDetectedException(findings);
            }
        }
    }

    /// <summary>The body with surrounding whitespace removed.</summary>
    public string NormalizedBody => Body.Trim();

    /// <summary>The given title, or the body's first line cut to 80 characters.</summary>
    public string NormalizedTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title))
            {
                return Title.Trim();
            }

            var firstLine = NormalizedBody.Split('\n', 2)[0].Trim();
            return firstLine.Length <= MaxTitleLength ? firstLine : firstLine[..(MaxTitleLength - 1)].TrimEnd() + "…";
        }
    }

    /// <summary>Null when blank.</summary>
    public string? NormalizedSource => string.IsNullOrWhiteSpace(Source) ? null : Source.Trim();

    /// <summary>Tags split on commas and spaces, lower case, without duplicates.</summary>
    public IReadOnlyList<string> NormalizedTags =>
        Tags is null
            ? []
            : Tags.SelectMany(t => t.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries))
                .Select(t => t.Trim().ToLowerInvariant())
                .Distinct()
                .ToList();
}

/// <summary>An entry as stored in the vault.</summary>
public sealed record VaultEntry(
    long Id,
    string Title,
    string Body,
    string? Source,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedUtc,
    string? ExternalId = null);

/// <summary>One search hit: the entry without its full body, plus the matching excerpt.</summary>
/// <param name="Snippet">Part of the body around the match, with matched words between the highlight markers.</param>
/// <param name="Rank">Relevance score from the provider. Only the order is meaningful, not the number.</param>
public sealed record VaultSearchResult(
    long Id,
    string Title,
    string? Source,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedUtc,
    string Snippet,
    double Rank)
{
    /// <summary>Marks the start of a matched word in <see cref="Snippet"/>.</summary>
    public const string HighlightStart = "\u0002";

    /// <summary>Marks the end of a matched word in <see cref="Snippet"/>.</summary>
    public const string HighlightEnd = "\u0003";
}

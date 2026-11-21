namespace Toolkit.Vault.Import;

/// <summary>A conversation that wasn't imported, and why.</summary>
public sealed record SkippedConversation(string Title, DateTimeOffset CreatedUtc, string Reason);

/// <summary>What an import did.</summary>
public sealed record ImportSummary(int Added, int AlreadyInVault, IReadOnlyList<SkippedConversation> Skipped)
{
    public int Total => Added + AlreadyInVault + Skipped.Count;
}

/// <summary>
/// Imports a claude.ai data export into the vault, one entry per conversation. Works with any
/// <see cref="IVaultStore"/>, so it doesn't care which database is behind it.
/// </summary>
/// <remarks>
/// Safe to run again on a newer export: conversations already in the vault (matched by their
/// external id) are left alone, and only new ones are added. A conversation that looks like it
/// contains a secret is skipped and reported, unless secrets are explicitly allowed.
/// </remarks>
public static class ClaudeExportImporter
{
    public const string ImportTag = "claude";

    public static async Task<ImportSummary> ImportAsync(
        IVaultStore vault, string exportPath, bool allowSecrets = false,
        IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        int added = 0, existing = 0, seen = 0;
        var skipped = new List<SkippedConversation>();

        await foreach (var conversation in ClaudeExportReader.ReadAsync(exportPath, cancellationToken))
        {
            progress?.Report(++seen);

            if (await vault.FindByExternalIdAsync(conversation.ExternalId, cancellationToken) is not null)
            {
                existing++;
                continue;
            }

            var entry = new NewVaultEntry(conversation.Title, conversation.ToTranscript(), "claude.ai conversation", [ImportTag])
            {
                ExternalId = conversation.ExternalId,
                CreatedUtc = conversation.CreatedUtc,
                AllowSecrets = allowSecrets,
            };

            try
            {
                await vault.AddAsync(entry, cancellationToken);
                added++;
            }
            catch (SecretDetectedException ex)
            {
                skipped.Add(new SkippedConversation(
                    entry.NormalizedTitle, conversation.CreatedUtc,
                    "looks like it contains " + string.Join(", ", ex.Findings.Select(f => $"a {f.Kind} ({f.Masked})"))));
            }
        }

        return new ImportSummary(added, existing, skipped);
    }
}

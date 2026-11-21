namespace Toolkit.Vault;

/// <summary>An entry was refused because its text looks like it contains a secret. Nothing was stored.</summary>
public sealed class SecretDetectedException(IReadOnlyList<SecretFinding> findings)
    : InvalidOperationException(
        $"Not saved: the text looks like it contains {Describe(findings)}. " +
        "Vault entries are plain text and are shown in search results.")
{
    /// <summary>What was found. Values are masked; the secret itself never appears here.</summary>
    public IReadOnlyList<SecretFinding> Findings { get; } = findings;

    private static string Describe(IReadOnlyList<SecretFinding> findings) =>
        string.Join(", ", findings.Select(f => $"a {f.Kind} ({f.Masked})"));
}

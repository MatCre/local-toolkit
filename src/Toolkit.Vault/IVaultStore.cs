namespace Toolkit.Vault;

/// <summary>
/// Everything the toolkit needs from vault storage. Code that uses the vault (the CLI, and later
/// the Blazor page and the AI layer's auto-capture) depends on this interface only, never on a
/// particular database.
/// </summary>
/// <remarks>
/// <para>
/// To move to another database (EF Core, SQL Server, Postgres), write a new adapter project that
/// implements this interface, run the shared contract tests against it
/// (<c>VaultStoreContractTests</c> in Toolkit.Vault.Tests), and change the one line that creates
/// the store.
/// </para>
/// <para>
/// <see cref="SearchAsync"/> is the part each database does differently: SQLite has FTS5, SQL Server
/// has full-text catalogs with CONTAINS and FREETEXT, Postgres has tsvector. The contract describes
/// the behaviour wanted (every word must match, other forms of a word match, half-typed words
/// match, best match first) and each adapter gets there with its own engine.
/// </para>
/// </remarks>
public interface IVaultStore
{
    /// <summary>
    /// Adds an entry and returns its new id. Implementations must call
    /// <see cref="NewVaultEntry.Validate"/> first; the contract tests check that they do.
    /// </summary>
    /// <exception cref="ArgumentException">The body is empty.</exception>
    /// <exception cref="SecretDetectedException">The text looks like a secret and AllowSecrets is off.</exception>
    Task<long> AddAsync(NewVaultEntry entry, CancellationToken cancellationToken = default);

    Task<VaultEntry?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<VaultEntry>> ListRecentAsync(int limit = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Entries containing every word in <paramref name="text"/>, best match first. Words are split
    /// with <see cref="SearchText.Words"/>; punctuation and query-language operators are never
    /// treated as syntax. Matched words in the snippet are wrapped in
    /// <see cref="VaultSearchResult.HighlightStart"/> and <see cref="VaultSearchResult.HighlightEnd"/>.
    /// </summary>
    Task<IReadOnlyList<VaultSearchResult>> SearchAsync(string text, int limit = 10, CancellationToken cancellationToken = default);

    /// <summary>Returns false when there was no such entry.</summary>
    Task<bool> RemoveAsync(long id, CancellationToken cancellationToken = default);

    Task<long> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>Where the data lives, for messages to the user: a file path or a server and database name.</summary>
    string Location { get; }
}

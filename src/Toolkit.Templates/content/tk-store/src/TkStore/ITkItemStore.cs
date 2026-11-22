namespace TkStore;

/// <summary>
/// Everything code needs from tkitem storage, with no database in sight. Each database gets its own
/// adapter project implementing this (TkStore.Sqlite today); the contract tests in TkStore.Tests
/// say what every adapter must do.
/// </summary>
public interface ITkItemStore
{
    /// <summary>Adds a tkitem and returns its id. Implementations call <see cref="NewTkItem.Validate"/> first.</summary>
    Task<long> AddAsync(NewTkItem item, CancellationToken cancellationToken = default);

    Task<TkItem?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<TkItem>> ListRecentAsync(int limit = 20, CancellationToken cancellationToken = default);

    /// <summary>Returns false when there was no such tkitem.</summary>
    Task<bool> RemoveAsync(long id, CancellationToken cancellationToken = default);

    Task<long> CountAsync(CancellationToken cancellationToken = default);
}

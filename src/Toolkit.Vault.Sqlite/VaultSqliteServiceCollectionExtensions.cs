using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Toolkit.Vault.Sqlite;

public static class VaultSqliteServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IVaultStore"/> on SQLite, at <paramref name="databasePath"/> or, by default,
    /// the per-user vault the <c>vault</c> command uses, so every app shares one vault.
    /// </summary>
    public static IServiceCollection AddVaultSqlite(this IServiceCollection services, string? databasePath = null)
    {
        services.TryAddSingleton<IVaultStore>(sp => new SqliteVaultStore(
            databasePath ?? SqliteVaultStore.DefaultDatabasePath,
            sp.GetService<TimeProvider>()));
        return services;
    }
}

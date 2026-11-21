using Microsoft.Data.Sqlite;
using Toolkit.Vault.Sqlite;

namespace Toolkit.Vault.Tests;

/// <summary>Runs the vault contract against SQLite, on a fresh file in a temp folder.</summary>
public sealed class SqliteVaultStoreTests : VaultStoreContractTests, IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "toolkit-vault-tests", Guid.NewGuid().ToString("N"));

    protected override IVaultStore CreateStore(TimeProvider time) =>
        new SqliteVaultStore(Path.Combine(_dir, "vault.db"), time);

    // SQLite-only behaviour goes here, below the inherited contract tests.

    [Fact]
    public async Task Keeps_data_in_the_file_between_store_instances()
    {
        var id = await Vault.AddAsync(new NewVaultEntry("Persisted", "Still here after a restart."));

        var reopened = CreateStore(Time);

        Assert.Equal("Persisted", (await reopened.GetAsync(id))?.Title);
        Assert.Single(await reopened.SearchAsync("restart"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); // pooled connections keep the file locked on Windows
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}

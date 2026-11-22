using Microsoft.Data.Sqlite;
using TkStore.Sqlite;

namespace TkStore.Tests;

/// <summary>Runs the contract against SQLite on a fresh file, plus checks only SQLite needs.</summary>
public sealed class SqliteTkItemStoreTests : TkItemStoreContractTests, IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tkstore-tests", Guid.NewGuid().ToString("N"));

    protected override ITkItemStore CreateStore() => new SqliteTkItemStore(Path.Combine(_dir, "tkitems.db"));

    [Fact]
    public async Task Keeps_data_in_the_file_between_store_instances()
    {
        var id = await Store.AddAsync(new NewTkItem("Persisted", "Still here after a restart."));

        Assert.Equal("Persisted", (await CreateStore().GetAsync(id))?.Title);
    }

    [Fact]
    public async Task Records_the_schema_version_in_the_file()
    {
        await Store.CountAsync(); // opening the store runs the migrations

        await using var connection = new SqliteConnection($"Data Source={Path.Combine(_dir, "tkitems.db")}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";

        Assert.Equal(1L, await command.ExecuteScalarAsync());
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

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

    [Fact]
    public async Task Upgrades_a_vault_created_before_migrations_existed()
    {
        // A file exactly as Session 4 left it: the original tables, schema version 0, one entry.
        var path = Path.Combine(_dir, "old-vault.db");
        Directory.CreateDirectory(_dir);
        await using (var old = new SqliteConnection($"Data Source={path}"))
        {
            await old.OpenAsync();
            await using var create = old.CreateCommand();
            create.CommandText = """
                CREATE TABLE entries (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT NOT NULL, body TEXT NOT NULL,
                    source TEXT, tags TEXT NOT NULL DEFAULT '', created_utc TEXT NOT NULL);
                CREATE VIRTUAL TABLE entries_fts USING fts5(title, body, tags, content='entries', content_rowid='id', tokenize='porter unicode61');
                CREATE TRIGGER entries_after_insert AFTER INSERT ON entries BEGIN
                    INSERT INTO entries_fts (rowid, title, body, tags) VALUES (new.id, new.title, new.body, new.tags);
                END;
                INSERT INTO entries (title, body, tags, created_utc) VALUES ('Old note', 'Written in Session 4.', '', '2026-11-21T01:00:00.0000000Z');
                """;
            await create.ExecuteNonQueryAsync();
        }

        var vault = new SqliteVaultStore(path, Time);

        Assert.Single(await vault.SearchAsync("session"));                           // old data still found
        var id = await vault.AddAsync(new NewVaultEntry("New", "With an id.") { ExternalId = "x:1" });
        Assert.Equal(id, await vault.FindByExternalIdAsync("x:1"));                   // new column works

        await using var check = new SqliteConnection($"Data Source={path}");
        await check.OpenAsync();
        await using var version = check.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        Assert.Equal(2L, await version.ExecuteScalarAsync());                        // both migrations recorded
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

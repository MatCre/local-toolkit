using System.Globalization;
using Dapper;

namespace TkStore.Sqlite;

/// <summary><see cref="ITkItemStore"/> on SQLite, using Dapper: you write the SQL, Dapper maps parameters and rows.</summary>
public sealed class SqliteTkItemStore(string databasePath, TimeProvider? time = null) : ITkItemStore
{
    // Applied in order; a database file records how many it has had. Only ever add to the end.
    private static readonly string[] Migrations =
    [
        """
        CREATE TABLE IF NOT EXISTS tkitems (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            title       TEXT NOT NULL,
            body        TEXT NOT NULL,
            created_utc TEXT NOT NULL  -- ISO 8601 UTC, sorts correctly as text
        );
        """,
    ];

    private const string SelectSql = "SELECT id AS Id, title AS Title, body AS Body, created_utc AS CreatedUtc FROM tkitems";

    private readonly SqliteDatabase _database = new(databasePath, Migrations);
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public string DatabasePath => _database.FullPath;

    public async Task<long> AddAsync(NewTkItem item, CancellationToken cancellationToken = default)
    {
        item.Validate();

        await using var connection = await _database.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO tkitems (title, body, created_utc) VALUES (@Title, @Body, @CreatedUtc);
            SELECT last_insert_rowid();
            """,
            new
            {
                Title = item.Title.Trim(),
                Body = item.Body,
                CreatedUtc = _time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            },
            cancellationToken: cancellationToken));
    }

    public async Task<TkItem?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(
            new CommandDefinition(SelectSql + " WHERE id = @Id;", new { Id = id }, cancellationToken: cancellationToken));
        return row?.ToTkItem();
    }

    public async Task<IReadOnlyList<TkItem>> ListRecentAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<Row>(
            new CommandDefinition(SelectSql + " ORDER BY id DESC LIMIT @Limit;", new { Limit = limit }, cancellationToken: cancellationToken));
        return rows.Select(r => r.ToTkItem()).ToList();
    }

    public async Task<bool> RemoveAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(
            new CommandDefinition("DELETE FROM tkitems WHERE id = @Id;", new { Id = id }, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM tkitems;", cancellationToken: cancellationToken));
    }

    // Dapper fills this by matching the column aliases in the SQL to property names.
    private sealed class Row
    {
        public long Id { get; set; }

        public string Title { get; set; } = "";

        public string Body { get; set; } = "";

        public string CreatedUtc { get; set; } = "";

        public TkItem ToTkItem() => new(Id, Title, Body,
            DateTimeOffset.Parse(CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));
    }
}

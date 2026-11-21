using System.Globalization;
using Dapper;
using Toolkit.Storage.Sqlite;

namespace Toolkit.Vault.Sqlite;

/// <summary>
/// <see cref="IVaultStore"/> on SQLite: entries in a table, with an FTS5 full-text index for search.
/// </summary>
/// <remarks>
/// <para>
/// <b>How search works.</b> <c>entries_fts</c> is an FTS5 virtual table: an inverted index that
/// maps each word to the entries containing it. It's an "external content" table: it stores only
/// the index and reads the text itself from <c>entries</c>. Triggers on <c>entries</c> keep the
/// index in step on every insert, update and delete, so the C# code never has to.
/// </para>
/// <para>
/// The <c>porter</c> tokenizer reduces words to their stems before indexing, so "retries",
/// "retrying" and "retry" all match each other. <c>unicode61</c> splits on punctuation and ignores
/// accents. Results are ranked by BM25, which favours rare words and short fields; title matches
/// are weighted 10x and tag matches 5x a body match.
/// </para>
/// </remarks>
public sealed class SqliteVaultStore : IVaultStore
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS entries (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            title       TEXT NOT NULL,
            body        TEXT NOT NULL,
            source      TEXT,
            tags        TEXT NOT NULL DEFAULT '',  -- space-separated, lower case
            created_utc TEXT NOT NULL              -- ISO 8601 UTC
        );

        CREATE VIRTUAL TABLE IF NOT EXISTS entries_fts USING fts5(
            title, body, tags,
            content = 'entries', content_rowid = 'id',
            tokenize = 'porter unicode61'
        );

        -- Keep the index in step with the table. An external-content FTS5 table is told about
        -- removals with the special 'delete' command, passing the old values.
        CREATE TRIGGER IF NOT EXISTS entries_after_insert AFTER INSERT ON entries BEGIN
            INSERT INTO entries_fts (rowid, title, body, tags) VALUES (new.id, new.title, new.body, new.tags);
        END;
        CREATE TRIGGER IF NOT EXISTS entries_after_delete AFTER DELETE ON entries BEGIN
            INSERT INTO entries_fts (entries_fts, rowid, title, body, tags) VALUES ('delete', old.id, old.title, old.body, old.tags);
        END;
        CREATE TRIGGER IF NOT EXISTS entries_after_update AFTER UPDATE ON entries BEGIN
            INSERT INTO entries_fts (entries_fts, rowid, title, body, tags) VALUES ('delete', old.id, old.title, old.body, old.tags);
            INSERT INTO entries_fts (rowid, title, body, tags) VALUES (new.id, new.title, new.body, new.tags);
        END;
        """;

    private const string SelectEntrySql = """
        SELECT id AS Id, title AS Title, body AS Body, source AS Source, tags AS Tags, created_utc AS CreatedUtc
        FROM entries
        """;

    // snippet(): column 1 (body), highlight markers, "…" where text is cut, about 16 words.
    // bm25(): one weight per column, in declaration order: title, body, tags.
    private const string SearchSql = """
        SELECT e.id AS Id, e.title AS Title, e.source AS Source, e.tags AS Tags, e.created_utc AS CreatedUtc,
               snippet(entries_fts, 1, @Start, @End, '…', 16) AS Snippet,
               bm25(entries_fts, 10.0, 1.0, 5.0) AS Rank
        FROM entries_fts
        JOIN entries e ON e.id = entries_fts.rowid
        WHERE entries_fts MATCH @Query
        ORDER BY Rank
        LIMIT @Limit;
        """;

    private readonly SqliteDatabase _database;
    private readonly TimeProvider _time;

    public SqliteVaultStore(string databasePath, TimeProvider? time = null)
    {
        _database = new SqliteDatabase(databasePath, SchemaSql);
        _time = time ?? TimeProvider.System;
    }

    /// <summary>One file per user, the same from any folder: %LOCALAPPDATA%\local-toolkit\vault.db on Windows.</summary>
    public static string DefaultDatabasePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "local-toolkit", "vault.db");

    public string Location => _database.FullPath;

    public async Task<long> AddAsync(NewVaultEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry.NormalizedBody.Length == 0)
        {
            throw new ArgumentException("An entry needs a body.", nameof(entry));
        }

        await using var connection = await _database.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO entries (title, body, source, tags, created_utc)
            VALUES (@Title, @Body, @Source, @Tags, @CreatedUtc);
            SELECT last_insert_rowid();
            """,
            new
            {
                Title = entry.NormalizedTitle,
                Body = entry.NormalizedBody,
                Source = entry.NormalizedSource,
                Tags = string.Join(' ', entry.NormalizedTags),
                CreatedUtc = _time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            },
            cancellationToken: cancellationToken));
    }

    public async Task<VaultEntry?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<EntryRow>(
            new CommandDefinition(SelectEntrySql + " WHERE id = @Id;", new { Id = id }, cancellationToken: cancellationToken));
        return row?.ToEntry();
    }

    public async Task<IReadOnlyList<VaultEntry>> ListRecentAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<EntryRow>(
            new CommandDefinition(SelectEntrySql + " ORDER BY id DESC LIMIT @Limit;", new { Limit = limit }, cancellationToken: cancellationToken));
        return rows.Select(r => r.ToEntry()).ToList();
    }

    public async Task<IReadOnlyList<VaultSearchResult>> SearchAsync(string text, int limit = 10, CancellationToken cancellationToken = default)
    {
        var query = FtsQuery.Build(text);
        if (query is null)
        {
            return [];
        }

        await using var connection = await _database.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<SearchRow>(new CommandDefinition(
            SearchSql,
            new { Query = query, Limit = limit, Start = VaultSearchResult.HighlightStart, End = VaultSearchResult.HighlightEnd },
            cancellationToken: cancellationToken));
        return rows.Select(r => r.ToResult()).ToList();
    }

    public async Task<bool> RemoveAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        var removed = await connection.ExecuteAsync(
            new CommandDefinition("DELETE FROM entries WHERE id = @Id;", new { Id = id }, cancellationToken: cancellationToken));
        return removed > 0;
    }

    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM entries;", cancellationToken: cancellationToken));
    }

    private static IReadOnlyList<string> SplitTags(string tags) =>
        tags.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static DateTimeOffset ParseUtc(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    // Dapper fills these by matching the column aliases in the SQL to property names.
    private sealed class EntryRow
    {
        public long Id { get; set; }

        public string Title { get; set; } = "";

        public string Body { get; set; } = "";

        public string? Source { get; set; }

        public string Tags { get; set; } = "";

        public string CreatedUtc { get; set; } = "";

        public VaultEntry ToEntry() => new(Id, Title, Body, Source, SplitTags(Tags), ParseUtc(CreatedUtc));
    }

    private sealed class SearchRow
    {
        public long Id { get; set; }

        public string Title { get; set; } = "";

        public string? Source { get; set; }

        public string Tags { get; set; } = "";

        public string CreatedUtc { get; set; } = "";

        public string Snippet { get; set; } = "";

        public double Rank { get; set; }

        public VaultSearchResult ToResult() => new(Id, Title, Source, SplitTags(Tags), ParseUtc(CreatedUtc), Snippet, Rank);
    }
}

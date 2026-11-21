using System.Globalization;
using Dapper;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Toolkit.AI.Storage;

namespace Toolkit.AI.Caching;

/// <summary>
/// <see cref="IDistributedCache"/> stored in a SQLite file, so cached answers survive restarts.
/// Microsoft.Extensions.AI's <c>DistributedCachingChatClient</c> does the AI-specific work
/// (building a key from the messages and options, serializing the response); this class only
/// stores bytes under a key with an expiry.
/// </summary>
/// <remarks>
/// The synchronous methods wrap the async ones. That's safe here because Microsoft.Data.Sqlite's
/// async methods complete synchronously: SQLite is an in-process file, there's no network I/O.
/// </remarks>
public sealed class SqliteDistributedCache : IDistributedCache
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS response_cache (
            key                     TEXT    PRIMARY KEY,
            value                   BLOB    NOT NULL,
            expires_at_utc          TEXT    NOT NULL,  -- when the entry stops being served
            sliding_seconds         INTEGER,           -- if set, each read pushes expires_at_utc out again
            absolute_expires_at_utc TEXT               -- hard limit that sliding can't extend past
        );
        CREATE INDEX IF NOT EXISTS ix_response_cache_expires ON response_cache (expires_at_utc);
        """;

    private const string SelectSql = """
        SELECT value AS Value, expires_at_utc AS ExpiresAtUtc,
               sliding_seconds AS SlidingSeconds, absolute_expires_at_utc AS AbsoluteExpiresAtUtc
        FROM response_cache WHERE key = @Key;
        """;

    private const string UpsertSql = """
        DELETE FROM response_cache WHERE expires_at_utc <= @Now;  -- tidy up while we're here
        INSERT INTO response_cache (key, value, expires_at_utc, sliding_seconds, absolute_expires_at_utc)
        VALUES (@Key, @Value, @ExpiresAtUtc, @SlidingSeconds, @AbsoluteExpiresAtUtc)
        ON CONFLICT (key) DO UPDATE SET
            value = excluded.value,
            expires_at_utc = excluded.expires_at_utc,
            sliding_seconds = excluded.sliding_seconds,
            absolute_expires_at_utc = excluded.absolute_expires_at_utc;
        """;

    private readonly SqliteDatabase _database;
    private readonly TimeSpan _defaultExpiration;
    private readonly TimeProvider _time;
    private readonly ILogger<SqliteDistributedCache> _logger;

    public SqliteDistributedCache(string databasePath, TimeSpan defaultExpiration, TimeProvider time, ILogger<SqliteDistributedCache> logger)
    {
        _database = new SqliteDatabase(databasePath, SchemaSql);
        _defaultExpiration = defaultExpiration;
        _time = time;
        _logger = logger;
    }

    public string DatabasePath => _database.FullPath;

    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        await using var connection = await _database.OpenAsync(token);
        var row = await connection.QuerySingleOrDefaultAsync<CacheRow>(
            new CommandDefinition(SelectSql, new { Key = key }, cancellationToken: token));

        var now = _time.GetUtcNow();
        if (row is null || Parse(row.ExpiresAtUtc) <= now)
        {
            _logger.LogDebug("Response cache miss");
            return null;
        }

        if (row.SlidingSeconds is { } sliding)
        {
            var extended = Earliest(now.AddSeconds(sliding), ParseOrNull(row.AbsoluteExpiresAtUtc));
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE response_cache SET expires_at_utc = @ExpiresAtUtc WHERE key = @Key;",
                new { Key = key, ExpiresAtUtc = Format(extended) }, cancellationToken: token));
        }

        _logger.LogInformation("Response cache hit; no provider was called");
        return row.Value;
    }

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        var now = _time.GetUtcNow();
        var absolute = options.AbsoluteExpiration ?? now + options.AbsoluteExpirationRelativeToNow;
        var sliding = options.SlidingExpiration;
        if (absolute is null && sliding is null)
        {
            absolute = now + _defaultExpiration; // the caching client sets no expiry of its own
        }

        var expires = sliding is { } s ? Earliest(now + s, absolute) : absolute!.Value;

        await using var connection = await _database.OpenAsync(token);
        await connection.ExecuteAsync(new CommandDefinition(UpsertSql, new
        {
            Key = key,
            Value = value,
            Now = Format(now),
            ExpiresAtUtc = Format(expires),
            SlidingSeconds = (long?)sliding?.TotalSeconds,
            AbsoluteExpiresAtUtc = absolute is { } a ? Format(a) : null,
        }, cancellationToken: token));
    }

    public Task RefreshAsync(string key, CancellationToken token = default) => GetAsync(key, token);

    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        await using var connection = await _database.OpenAsync(token);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM response_cache WHERE key = @Key;", new { Key = key }, cancellationToken: token));
    }

    public byte[]? Get(string key) => GetAsync(key).GetAwaiter().GetResult();

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => SetAsync(key, value, options).GetAwaiter().GetResult();

    public void Refresh(string key) => RefreshAsync(key).GetAwaiter().GetResult();

    public void Remove(string key) => RemoveAsync(key).GetAwaiter().GetResult();

    private static DateTimeOffset Earliest(DateTimeOffset value, DateTimeOffset? limit) =>
        limit is { } l && l < value ? l : value;

    private static string Format(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static DateTimeOffset? ParseOrNull(string? value) => value is null ? null : Parse(value);

    private sealed class CacheRow
    {
        public byte[] Value { get; set; } = [];

        public string ExpiresAtUtc { get; set; } = "";

        public long? SlidingSeconds { get; set; }

        public string? AbsoluteExpiresAtUtc { get; set; }
    }
}

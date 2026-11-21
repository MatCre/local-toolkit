using System.Globalization;
using Dapper;
using Toolkit.AI.Storage;

namespace Toolkit.AI.CallLog;

/// <summary>
/// Appends call records to an <c>ai_calls</c> table in a SQLite file. Open the file in
/// DB Browser for SQLite to browse it. The table is created on first use.
/// </summary>
/// <remarks>
/// Dapper adds <c>ExecuteAsync</c> and <c>QueryAsync</c> extension methods to the connection.
/// You still write the SQL; Dapper fills <c>@Name</c> parameters from an object's properties
/// (turning null into SQL NULL) and maps result columns back onto C# objects.
/// </remarks>
public sealed class SqliteAiCallLog : IAiCallLog
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS ai_calls (
            id                 INTEGER PRIMARY KEY AUTOINCREMENT,
            timestamp_utc      TEXT    NOT NULL,  -- ISO 8601, sorts correctly as text
            provider           TEXT    NOT NULL,
            model              TEXT,
            input_tokens       INTEGER,
            output_tokens      INTEGER,
            duration_ms        INTEGER NOT NULL,
            estimated_cost_usd REAL,
            succeeded          INTEGER NOT NULL,  -- 1 = answered, 0 = failed and fell back
            error              TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_ai_calls_timestamp ON ai_calls (timestamp_utc);
        """;

    private const string InsertSql = """
        INSERT INTO ai_calls
            (timestamp_utc, provider, model, input_tokens, output_tokens, duration_ms, estimated_cost_usd, succeeded, error)
        VALUES
            (@Timestamp, @Provider, @Model, @InputTokens, @OutputTokens, @DurationMs, @Cost, @Succeeded, @Error);
        """;

    // The index on timestamp_utc makes this a range scan over this month's rows only.
    private const string CostSinceSql = """
        SELECT COALESCE(SUM(estimated_cost_usd), 0) FROM ai_calls WHERE timestamp_utc >= @Since;
        """;

    private readonly SqliteDatabase _database;

    public SqliteAiCallLog(string databasePath) => _database = new SqliteDatabase(databasePath, SchemaSql);

    public string DatabasePath => _database.FullPath;

    public async Task RecordAsync(AiCallRecord record, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);

        // Property names match the @parameters in InsertSql. Most come straight from the record;
        // the few that need converting for SQLite are given here under the parameter's name.
        await connection.ExecuteAsync(new CommandDefinition(InsertSql, new
        {
            Timestamp = ToSortableUtc(record.Timestamp),
            record.Provider,
            record.Model,
            record.InputTokens,
            record.OutputTokens,
            DurationMs = (long)record.Duration.TotalMilliseconds,
            Cost = (double?)record.EstimatedCostUsd,
            record.Succeeded,
            record.Error,
        }, cancellationToken: cancellationToken));
    }

    public async Task<decimal> GetCostSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        var total = await connection.ExecuteScalarAsync<double>(
            new CommandDefinition(CostSinceSql, new { Since = ToSortableUtc(since) }, cancellationToken: cancellationToken));
        return (decimal)total;
    }

    // Same format for writing and comparing, so text order equals time order.
    private static string ToSortableUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}

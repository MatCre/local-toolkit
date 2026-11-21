using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

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
    private const string CreateTableSql = """
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

    private readonly string _connectionString;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public SqliteAiCallLog(string databasePath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString();
    }

    public string DatabasePath { get; }

    public async Task RecordAsync(AiCallRecord record, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);

        await using var connection = new SqliteConnection(_connectionString);

        // Property names match the @parameters in InsertSql. Most come straight from the record;
        // the few that need converting for SQLite are given here under the parameter's name.
        await connection.ExecuteAsync(new CommandDefinition(InsertSql, new
        {
            Timestamp = record.Timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
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

    private async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.ExecuteAsync(new CommandDefinition(CreateTableSql, cancellationToken: cancellationToken));

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}

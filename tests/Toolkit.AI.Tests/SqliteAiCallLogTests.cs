using Microsoft.Data.Sqlite;
using Toolkit.AI.CallLog;
using Toolkit.AI.Sqlite;

namespace Toolkit.AI.Tests;

/// <summary>Runs the call-log contract against SQLite, plus checks of the table itself.</summary>
public sealed class SqliteAiCallLogTests : AiCallLogContractTests, IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "toolkit-ai-tests", Guid.NewGuid().ToString("N"));

    protected override IAiCallLog CreateLog() => new SqliteAiCallLog(Path.Combine(_dir, $"calls-{Guid.NewGuid():N}.db"));

    [Fact]
    public async Task Creates_the_table_and_appends_records()
    {
        var log = new SqliteAiCallLog(Path.Combine(_dir, "calls.db"));

        await log.RecordAsync(new AiCallRecord(DateTimeOffset.UtcNow, "openrouter", null, null, null,
            TimeSpan.FromMilliseconds(120), null, Succeeded: false, Error: "429 Too Many Requests"));
        await log.RecordAsync(new AiCallRecord(DateTimeOffset.UtcNow, "ollama", "qwen2.5-coder:7b", 40, 13,
            TimeSpan.FromMilliseconds(2400), 0m, Succeeded: true, Error: null));

        await using var connection = new SqliteConnection($"Data Source={log.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT provider || ':' || succeeded FROM ai_calls ORDER BY id";
        var rows = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                rows.Add(reader.GetString(0));
            }
        }

        Assert.Equal(["openrouter:0", "ollama:1"], rows);
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

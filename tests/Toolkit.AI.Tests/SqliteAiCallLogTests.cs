using Microsoft.Data.Sqlite;
using Toolkit.AI.CallLog;

namespace Toolkit.AI.Tests;

public sealed class SqliteAiCallLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "toolkit-ai-tests", Guid.NewGuid().ToString("N"));

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

    [Fact]
    public async Task Sums_cost_from_a_given_time_onwards()
    {
        var log = new SqliteAiCallLog(Path.Combine(_dir, "calls.db"));
        var monthStart = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(13)); // midnight in NZ

        await log.RecordAsync(Call(monthStart.AddMinutes(-1), 4.00m)); // last month: not counted
        await log.RecordAsync(Call(monthStart, 0.25m));
        await log.RecordAsync(Call(monthStart.AddDays(10), 0.50m));
        await log.RecordAsync(Call(monthStart.AddDays(11), null)); // failed call, no cost

        Assert.Equal(0.75m, await log.GetCostSinceAsync(monthStart));
    }

    private static AiCallRecord Call(DateTimeOffset at, decimal? cost) =>
        new(at, "openrouter", "m", 1, 1, TimeSpan.FromSeconds(1), cost, cost is not null, null);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); // pooled connections keep the file locked on Windows
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}

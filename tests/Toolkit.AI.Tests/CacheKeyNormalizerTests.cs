using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Toolkit.AI.Caching;
using Toolkit.AI.Sqlite;

namespace Toolkit.AI.Tests;

public sealed class CacheKeyNormalizerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "toolkit-ai-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeChatClient _provider = new("answer");

    [Theory]
    [InlineData("What is SQLite?", "what is sqlite")]
    [InlineData("  what   is\tSQLite  ", "what is sqlite")]
    [InlineData("What is SQLite!?", "what is sqlite")]
    [InlineData("Line one\r\nline two.", "line one line two")]
    [InlineData("ＳＱＬｉｔｅ", "sqlite")] // full-width letters, as some keyboards and pastes produce
    [InlineData("Version 1.5 of .NET", "version 1.5 of .net")] // dots inside the text stay
    public void Normalizes_text(string input, string expected) =>
        Assert.Equal(expected, CacheKeyNormalizer.Normalize(input));

    [Fact]
    public async Task Reworded_prompts_share_one_cache_entry()
    {
        var chat = new ToolkitCachingChatClient(_provider, NewStore());

        await chat.GetResponseAsync("What is SQLite?");
        await chat.GetResponseAsync("what is sqlite");
        await chat.GetResponseAsync("  WHAT is   SQLite?? ");

        Assert.Equal(1, _provider.Calls);
    }

    [Fact]
    public async Task The_model_receives_the_original_text()
    {
        var chat = new ToolkitCachingChatClient(_provider, NewStore());

        await chat.GetResponseAsync("  What is SQLite?  ");

        Assert.Equal("  What is SQLite?  ", _provider.LastPrompt);
    }

    [Fact]
    public async Task Different_words_are_still_different_entries()
    {
        var chat = new ToolkitCachingChatClient(_provider, NewStore());

        await chat.GetResponseAsync("What is SQLite?");
        await chat.GetResponseAsync("What is Dapper?");

        Assert.Equal(2, _provider.Calls);
    }

    [Fact]
    public async Task Without_normalization_case_matters()
    {
        var chat = new ToolkitCachingChatClient(_provider, NewStore(), normalizeKeys: false);

        await chat.GetResponseAsync("What is SQLite?");
        await chat.GetResponseAsync("what is sqlite?");

        Assert.Equal(2, _provider.Calls);
    }

    private SqliteDistributedCache NewStore() =>
        new(Path.Combine(_dir, "cache.db"), TimeSpan.FromHours(1),
            new FakeTimeProvider(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<SqliteDistributedCache>.Instance);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Toolkit.AI.Caching;
using Toolkit.AI.Sqlite;

namespace Toolkit.AI.Tests;

/// <summary>The real caching layer from Microsoft.Extensions.AI over our SQLite store.</summary>
public sealed class ResponseCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "toolkit-ai-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeChatClient _provider = new("cached answer");

    private SqliteDistributedCache NewStore() =>
        new(Path.Combine(_dir, "cache.db"), TimeSpan.FromHours(1), _time, NullLogger<SqliteDistributedCache>.Instance);

    private IChatClient CachedPipeline(SqliteDistributedCache store) => new ToolkitCachingChatClient(_provider, store, normalizeKeys: false);

    [Fact]
    public async Task A_repeated_prompt_is_answered_from_the_cache()
    {
        var chat = CachedPipeline(NewStore());

        var first = await chat.GetResponseAsync("What is SQLite?");
        var second = await chat.GetResponseAsync("What is SQLite?");

        Assert.False(ToolkitCachingChatClient.IsCacheHit(first));
        Assert.True(ToolkitCachingChatClient.IsCacheHit(second));

        Assert.Equal(first.Text, second.Text);
        Assert.Equal(1, _provider.Calls);
    }

    [Fact]
    public async Task A_streamed_answer_from_the_cache_is_marked_as_a_hit()
    {
        var chat = CachedPipeline(NewStore());

        await foreach (var _ in chat.GetStreamingResponseAsync("What is SQLite?"))
        {
        }

        var replayed = new List<ChatResponseUpdate>();
        await foreach (var update in chat.GetStreamingResponseAsync("What is SQLite?"))
        {
            replayed.Add(update);
        }

        Assert.True(ToolkitCachingChatClient.IsCacheHit(replayed.ToChatResponse()));
        Assert.Equal(1, _provider.Calls);
    }

    [Fact]
    public async Task The_cache_survives_a_restart()
    {
        await CachedPipeline(NewStore()).GetResponseAsync("What is SQLite?");

        // A new store over the same file stands in for the app being started again.
        await CachedPipeline(NewStore()).GetResponseAsync("What is SQLite?");

        Assert.Equal(1, _provider.Calls);
    }

    [Fact]
    public async Task A_different_prompt_is_not_a_cache_hit()
    {
        var chat = CachedPipeline(NewStore());

        await chat.GetResponseAsync("What is SQLite?");
        await chat.GetResponseAsync("What is Dapper?");

        Assert.Equal(2, _provider.Calls);
    }

    [Fact]
    public async Task An_expired_answer_is_fetched_again()
    {
        var chat = CachedPipeline(NewStore());

        await chat.GetResponseAsync("What is SQLite?");
        _time.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
        await chat.GetResponseAsync("What is SQLite?");

        Assert.Equal(2, _provider.Calls);
    }

    [Fact]
    public async Task Sliding_expiration_is_extended_by_each_read()
    {
        var store = NewStore();
        var options = new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10) };
        await store.SetAsync("key", [1, 2, 3], options);

        _time.Advance(TimeSpan.FromMinutes(8));
        Assert.NotNull(await store.GetAsync("key")); // read at 8 min pushes expiry to 18 min
        _time.Advance(TimeSpan.FromMinutes(8));
        Assert.NotNull(await store.GetAsync("key")); // 16 min: still alive thanks to the read
        _time.Advance(TimeSpan.FromMinutes(11));
        Assert.Null(await store.GetAsync("key"));    // 27 min: 11 min since the last read
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}

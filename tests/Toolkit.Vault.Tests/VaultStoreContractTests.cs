using Microsoft.Extensions.Time.Testing;

namespace Toolkit.Vault.Tests;

/// <summary>
/// What every <see cref="IVaultStore"/> must do, whatever database is behind it. Each adapter gets
/// a small subclass that says how to create a fresh, empty store; xUnit then runs every test below
/// against it.
/// </summary>
/// <remarks>
/// Adding a provider (EF Core, SQL Server, Postgres): write the adapter, add a subclass of this
/// class, and make these tests pass. Behaviour only one engine has belongs in that adapter's own
/// test class, not here.
/// </remarks>
public abstract class VaultStoreContractTests
{
    protected FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero));

    private IVaultStore? _vault;

    /// <summary>A new, empty store that reads the clock from <paramref name="time"/>.</summary>
    protected abstract IVaultStore CreateStore(TimeProvider time);

    protected IVaultStore Vault => _vault ??= CreateStore(Time);

    private Task<long> Add(string title, string body, params string[] tags) =>
        Vault.AddAsync(new NewVaultEntry(title, body, "test", tags));

    private async Task<List<long>> Find(string text) => (await Vault.SearchAsync(text)).Select(r => r.Id).ToList();

    [Fact]
    public async Task Stores_and_returns_an_entry()
    {
        var id = await Vault.AddAsync(new NewVaultEntry("Retry policy", "  Use Polly.  ", "claude", ["DotNet, Resilience"]));

        var entry = await Vault.GetAsync(id);

        Assert.NotNull(entry);
        Assert.Equal("Retry policy", entry.Title);
        Assert.Equal("Use Polly.", entry.Body);
        Assert.Equal("claude", entry.Source);
        Assert.Equal(["dotnet", "resilience"], entry.Tags);
        Assert.Equal(Time.GetUtcNow(), entry.CreatedUtc);
    }

    [Fact]
    public async Task Uses_the_first_line_as_title_when_none_is_given()
    {
        var id = await Vault.AddAsync(new NewVaultEntry(null, "dotnet user-secrets set Key Value\nStores a secret outside the repo."));

        Assert.Equal("dotnet user-secrets set Key Value", (await Vault.GetAsync(id))!.Title);
    }

    [Fact]
    public async Task Rejects_an_empty_body()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Vault.AddAsync(new NewVaultEntry("Empty", "   ")));
    }

    [Fact]
    public async Task Finds_entries_by_title_body_or_tag()
    {
        var byTitle = await Add("Dapper basics", "Execute and query with parameters.");
        var byBody = await Add("Data access", "Dapper maps rows to objects.");
        var byTag = await Add("Mapping rows", "Turn columns into properties.", "dapper");

        Assert.Equivalent(new[] { byTitle, byBody, byTag }, await Find("dapper"));
    }

    [Fact]
    public async Task Every_word_must_match()
    {
        var both = await Add("SQLite full text search", "FTS5 virtual tables.");
        await Add("SQLite basics", "A file-based database.");

        Assert.Equal([both], await Find("sqlite search"));
    }

    [Fact]
    public async Task Matches_other_forms_of_a_word()
    {
        var id = await Add("Resilience", "Configure retries with exponential back-off.");

        Assert.Equal([id], await Find("retry"));
        Assert.Equal([id], await Find("retrying"));
    }

    [Fact]
    public async Task Matches_half_typed_words()
    {
        var id = await Add("Embedded database", "SQLite stores everything in one file.");

        Assert.Equal([id], await Find("sqli"));
    }

    [Fact]
    public async Task Ranks_title_matches_above_body_matches()
    {
        var bodyOnly = await Add("Configuration", "You can also cache values in memory.");
        var inTitle = await Add("Response cache", "Store answers by key.");

        Assert.Equal([inTitle, bodyOnly], await Find("cache"));
    }

    [Theory]
    [InlineData("C# records")]
    [InlineData("foo-bar")]
    [InlineData("\"unbalanced")]
    [InlineData("NOT OR AND")]
    [InlineData("title:*")]
    [InlineData("()^*")]
    [InlineData("")]
    public async Task Punctuation_and_operators_never_break_search(string text)
    {
        await Add("C# records", "Records give value equality. NOT OR AND foo bar title.");

        var exception = await Record.ExceptionAsync(() => Vault.SearchAsync(text));

        Assert.Null(exception);
    }

    [Fact]
    public async Task Highlights_matched_words_in_the_snippet()
    {
        await Add("Note", "The response cache stores answers for seven days.");

        var hit = Assert.Single(await Vault.SearchAsync("cache"));

        Assert.Contains($"{VaultSearchResult.HighlightStart}cache{VaultSearchResult.HighlightEnd}", hit.Snippet);
    }

    [Fact]
    public async Task A_removed_entry_no_longer_turns_up_in_search()
    {
        var id = await Add("Temporary", "Something to forget.");

        Assert.True(await Vault.RemoveAsync(id));

        Assert.Empty(await Find("forget"));
        Assert.Null(await Vault.GetAsync(id));
        Assert.False(await Vault.RemoveAsync(id));
    }

    [Fact]
    public async Task Lists_newest_first()
    {
        var first = await Add("First", "one");
        Time.Advance(TimeSpan.FromMinutes(1));
        var second = await Add("Second", "two");

        Assert.Equal([second, first], (await Vault.ListRecentAsync()).Select(e => e.Id));
        Assert.Equal(2, await Vault.CountAsync());
    }
}

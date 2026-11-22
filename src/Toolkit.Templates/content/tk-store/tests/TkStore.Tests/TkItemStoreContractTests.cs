namespace TkStore.Tests;

/// <summary>
/// What every <see cref="ITkItemStore"/> must do, whatever database is behind it. Each adapter gets a
/// small subclass saying how to create a fresh, empty store; xUnit runs every test here against it.
/// Adding a database means: write the adapter, add a subclass, make these pass.
/// </summary>
public abstract class TkItemStoreContractTests
{
    private ITkItemStore? _store;

    /// <summary>A new, empty store.</summary>
    protected abstract ITkItemStore CreateStore();

    protected ITkItemStore Store => _store ??= CreateStore();

    [Fact]
    public async Task Stores_and_returns_a_tkitem()
    {
        var id = await Store.AddAsync(new NewTkItem("  First  ", "Some text."));

        var item = await Store.GetAsync(id);

        Assert.NotNull(item);
        Assert.Equal("First", item.Title);
        Assert.Equal("Some text.", item.Body);
    }

    [Fact]
    public async Task Rejects_a_tkitem_without_a_title()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Store.AddAsync(new NewTkItem(" ", "body")));

        Assert.Equal(0, await Store.CountAsync());
    }

    [Fact]
    public async Task Lists_newest_first()
    {
        var first = await Store.AddAsync(new NewTkItem("First", "1"));
        var second = await Store.AddAsync(new NewTkItem("Second", "2"));

        Assert.Equal([second, first], (await Store.ListRecentAsync()).Select(i => i.Id));
    }

    [Fact]
    public async Task Removes_a_tkitem()
    {
        var id = await Store.AddAsync(new NewTkItem("Temporary", "gone soon"));

        Assert.True(await Store.RemoveAsync(id));
        Assert.Null(await Store.GetAsync(id));
        Assert.False(await Store.RemoveAsync(id));
    }
}

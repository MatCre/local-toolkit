using System.Collections.Concurrent;

namespace TkEndpoint.Api.TkItems;

/// <summary>
/// Keeps tkitems in memory, so the template runs with no setup. Replace it with a real store,
/// for example one made with `dotnet new tk-store`, behind the same methods.
/// </summary>
public sealed class TkItemStore
{
    private readonly ConcurrentDictionary<Guid, TkItem> _items = new();

    public IReadOnlyList<TkItem> All() => _items.Values.OrderBy(i => i.CreatedUtc).ToList();

    public TkItem? Find(Guid id) => _items.GetValueOrDefault(id);

    public TkItem Add(CreateTkItemRequest request)
    {
        var item = new TkItem(Guid.NewGuid(), request.Name.Trim(), request.Quantity, DateTimeOffset.UtcNow);
        _items[item.Id] = item;
        return item;
    }

    public bool Remove(Guid id) => _items.TryRemove(id, out _);
}

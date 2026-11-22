namespace TkStore;

/// <summary>One tkitem, as stored.</summary>
public sealed record TkItem(long Id, string Title, string Body, DateTimeOffset CreatedUtc);

/// <summary>What you supply to add a tkitem. The rules live here so every store applies them the same way.</summary>
public sealed record NewTkItem(string Title, string Body)
{
    /// <exception cref="ArgumentException">The title is empty.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            throw new ArgumentException("A tkitem needs a title.", nameof(Title));
        }
    }
}

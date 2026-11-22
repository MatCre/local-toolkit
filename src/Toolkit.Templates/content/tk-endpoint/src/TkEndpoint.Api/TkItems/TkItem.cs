namespace TkEndpoint.Api.TkItems;

/// <summary>One tkitem, as stored and returned by the API.</summary>
public sealed record TkItem(Guid Id, string Name, int Quantity, DateTimeOffset CreatedUtc);

using Microsoft.AspNetCore.Http.HttpResults;

namespace TkEndpoint.Api.TkItems;

/// <summary>
/// The /tkendpoint routes. TypedResults (rather than Results) put each handler's possible
/// responses in its signature, which keeps the code honest and documents the API.
/// </summary>
public static class TkItemEndpoints
{
    public static IEndpointRouteBuilder MapTkItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tkendpoint");

        group.MapGet("/", (TkItemStore store) => TypedResults.Ok(store.All()));

        group.MapGet("/{id:guid}", Results<Ok<TkItem>, NotFound> (Guid id, TkItemStore store) =>
            store.Find(id) is { } item ? TypedResults.Ok(item) : TypedResults.NotFound());

        group.MapPost("/", (CreateTkItemRequest request, TkItemStore store) =>
        {
            var item = store.Add(request);
            return TypedResults.Created($"/tkendpoint/{item.Id}", item);
        });

        group.MapDelete("/{id:guid}", Results<NoContent, NotFound> (Guid id, TkItemStore store) =>
            store.Remove(id) ? TypedResults.NoContent() : TypedResults.NotFound());

        return app;
    }
}

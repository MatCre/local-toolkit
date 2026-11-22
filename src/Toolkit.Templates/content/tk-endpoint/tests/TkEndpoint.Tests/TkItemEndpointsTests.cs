using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using TkEndpoint.Api.TkItems;

namespace TkEndpoint.Tests;

/// <summary>
/// Integration tests: WebApplicationFactory starts the real app in memory and the tests talk to it
/// over HTTP, so routing, validation, serialization and status codes are all exercised.
/// </summary>
public class TkItemEndpointsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Creating_a_valid_tkitem_returns_201_with_its_location()
    {
        var response = await _client.PostAsJsonAsync("/tkendpoint", new { name = "First", quantity = 2 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TkItem>();
        Assert.Equal("First", created!.Name);
        Assert.Equal($"/tkendpoint/{created.Id}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Invalid_input_is_rejected_with_400_and_an_error_per_field()
    {
        var response = await _client.PostAsJsonAsync("/tkendpoint", new { name = "", quantity = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains(problem!.Errors.Keys, key => key.Equals("Name", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(problem.Errors.Keys, key => key.Equals("Quantity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_created_tkitem_can_be_read_listed_and_deleted()
    {
        var created = await (await _client.PostAsJsonAsync("/tkendpoint", new { name = "Second", quantity = 5 }))
            .Content.ReadFromJsonAsync<TkItem>();

        Assert.Equal("Second", (await _client.GetFromJsonAsync<TkItem>($"/tkendpoint/{created!.Id}"))?.Name);
        Assert.Contains(await _client.GetFromJsonAsync<List<TkItem>>("/tkendpoint") ?? [], i => i.Id == created.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/tkendpoint/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/tkendpoint/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task An_unknown_id_returns_404()
    {
        var response = await _client.GetAsync($"/tkendpoint/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

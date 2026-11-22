using TkEndpoint.Api.TkItems;

var builder = WebApplication.CreateBuilder(args);

// Every error response gets the standard problem details JSON shape (RFC 9457).
builder.Services.AddProblemDetails();

// Built-in minimal API validation: DataAnnotations on endpoint parameters ([Required], [Range], ...)
// are checked before the handler runs, and a failure returns 400 with the errors per field.
builder.Services.AddValidation();

builder.Services.AddSingleton<TkItemStore>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapTkItemEndpoints();

app.Run();

/// <summary>Public so the tests can start the app with WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;

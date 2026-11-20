using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Toolkit.AI;

// The generic host gives us configuration, logging and DI, the same plumbing an
// ASP.NET Core or Worker Service app uses, so later projects wire up identically.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    // Read appsettings*.json from the build output folder, wherever `dotnet run` is invoked from.
    ContentRootPath = AppContext.BaseDirectory,
});

// Configuration layers, later wins: appsettings.json -> appsettings.{Environment}.json ->
// user secrets (Development only) -> environment variables -> command line.
// appsettings.Local.json is git-ignored and holds machine-specific overrides.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

builder.Services.AddToolkitAI(builder.Configuration);

using var host = builder.Build();

// From here on, this code only knows about IChatClient, not Ollama.
var chat = host.Services.GetRequiredService<IChatClient>();

var prompt = args.Length > 0
    ? string.Join(' ', args)
    : "In two sentences, explain what dependency injection is in .NET.";

Console.WriteLine($"> {prompt}\n");

var stopwatch = Stopwatch.StartNew();

// GetResponseAsync waits for the whole reply, so Trace logging writes one entry for the
// request and one for the complete response. GetStreamingResponseAsync would print tokens
// as they arrive, but logs one entry per chunk.
var response = await chat.GetResponseAsync(prompt);

stopwatch.Stop();
Console.WriteLine(response.Text);
Console.WriteLine($"\n[{stopwatch.Elapsed.TotalSeconds:F1}s, {response.ModelId}, " +
                  $"{response.Usage?.InputTokenCount ?? 0} in / {response.Usage?.OutputTokenCount ?? 0} out tokens]");

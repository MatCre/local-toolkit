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
UsageDetails? usage = null;

// Streaming prints tokens as the model produces them instead of waiting for the full reply.
await foreach (var update in chat.GetStreamingResponseAsync(prompt))
{
    Console.Write(update.Text);

    foreach (var usageContent in update.Contents.OfType<UsageContent>())
    {
        usage = usageContent.Details;
    }
}

stopwatch.Stop();
Console.WriteLine($"\n\n[{stopwatch.Elapsed.TotalSeconds:F1}s, " +
                  $"{usage?.InputTokenCount ?? 0} in / {usage?.OutputTokenCount ?? 0} out tokens]");

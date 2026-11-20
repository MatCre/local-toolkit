using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OllamaSharp;

namespace Toolkit.AI;

/// <summary>
/// The single entry point other projects use: <c>services.AddToolkitAI(config)</c>.
/// Callers then ask DI for <see cref="IChatClient"/> and never see which vendor answers.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddToolkitAI(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Bind the "ToolkitAI" config section to ToolkitAIOptions and fail fast at startup if it's unusable.
        services.AddOptions<ToolkitAIOptions>()
            .Bind(configuration.GetSection(ToolkitAIOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Ollama.Model), "ToolkitAI:Ollama:Model must be set.")
            .ValidateOnStart();

        // 2. Register the pipeline. AddChatClient takes the innermost client (the one that talks
        //    to a real model) and returns a ChatClientBuilder. Each .UseXxx() wraps it in a
        //    middleware layer, like ASP.NET Core middleware but for chat calls.
        //
        //    Request flow:   caller -> [Logging] -> OllamaApiClient -> HTTP -> Ollama
        //
        //    Session 2 swaps the inner client for a RoutingChatClient (Ollama + cloud).
        //    Session 3 adds .UseDistributedCache() and a BudgetChatClient layer here.
        services.AddChatClient(sp =>
            {
                var options = sp.GetRequiredService<IOptions<ToolkitAIOptions>>().Value.Ollama;
                return new OllamaApiClient(options.Endpoint, options.Model);
            })
            .UseLogging();

        return services;
    }
}

using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OpenAI;

namespace Toolkit.AI.Routing;

/// <summary>
/// Turns the "ToolkitAI:Providers" settings into real clients, in ProviderOrder.
/// This is the only place in the toolkit that knows vendor SDK types.
/// </summary>
internal static class ChatProviderFactory
{
    private static readonly Uri DefaultOllamaEndpoint = new("http://localhost:11434");

    public static List<ChatProvider> Create(ToolkitAIOptions options, ILogger logger)
    {
        var providers = new List<ChatProvider>();

        foreach (var name in options.ProviderOrder)
        {
            var settings = options.FindProvider(name)!; // the validator guarantees it exists

            if (settings.Type == ProviderType.OpenAICompatible && string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                logger.LogWarning(
                    "Skipping AI provider {Provider}: no API key. Set one with: dotnet user-secrets set ToolkitAI:Providers:{Provider}:ApiKey <key>",
                    name, name);
                continue;
            }

            providers.Add(new ChatProvider(
                name,
                CreateClient(settings),
                TimeSpan.FromSeconds(settings.TimeoutSeconds),
                settings.InputPricePerMillionTokens,
                settings.OutputPricePerMillionTokens,
                IsLocal: settings.Type == ProviderType.Ollama));
        }

        if (providers.Count == 0)
        {
            throw new InvalidOperationException(
                "No usable AI providers: every provider in ToolkitAI:ProviderOrder was skipped. Check the warnings above.");
        }

        return providers;
    }

    private static IChatClient CreateClient(ProviderOptions settings) => settings.Type switch
    {
        // OllamaSharp's default HttpClient gives up after 100 s. Turn that off so the router's
        // own per-provider timeout is the only one that applies.
        ProviderType.Ollama => new OllamaApiClient(
            new HttpClient
            {
                BaseAddress = settings.Endpoint ?? DefaultOllamaEndpoint,
                Timeout = Timeout.InfiniteTimeSpan,
            },
            settings.Model),

        // The official OpenAI SDK works with any OpenAI-compatible server once you change the endpoint.
        // Its built-in retries (with back-off on HTTP 429) are turned off: when a provider is
        // rate limited, falling back to the next provider is faster than waiting.
        ProviderType.OpenAICompatible => new OpenAIClient(
                new ApiKeyCredential(settings.ApiKey!),
                new OpenAIClientOptions
                {
                    Endpoint = settings.Endpoint,
                    RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
                })
            .GetChatClient(settings.Model)
            .AsIChatClient(),

        _ => throw new NotSupportedException($"Provider type {settings.Type} is not supported."),
    };
}

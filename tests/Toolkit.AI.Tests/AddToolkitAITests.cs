using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OllamaSharp;
using Toolkit.AI.CallLog;
using Toolkit.AI.Routing;

namespace Toolkit.AI.Tests;

// These tests check the wiring only. They never call a model, so they pass offline and in CI.
public class AddToolkitAITests
{
    private static readonly Dictionary<string, string?> TwoProviders = new()
    {
        ["ToolkitAI:ProviderOrder:0"] = "openrouter",
        ["ToolkitAI:ProviderOrder:1"] = "ollama",
        ["ToolkitAI:Providers:openrouter:Type"] = "OpenAICompatible",
        ["ToolkitAI:Providers:openrouter:Endpoint"] = "https://openrouter.ai/api/v1",
        ["ToolkitAI:Providers:openrouter:Model"] = "openrouter/free",
        ["ToolkitAI:Providers:ollama:Type"] = "Ollama",
        ["ToolkitAI:Providers:ollama:Model"] = "qwen2.5-coder:7b",
        ["ToolkitAI:Providers:ollama:TimeoutSeconds"] = "120",
    };

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAiCallLog>(new InMemoryCallLog()); // keep tests off the disk
        services.AddToolkitAI(config);
        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> With(params (string Key, string? Value)[] changes)
    {
        var settings = new Dictionary<string, string?>(TwoProviders);
        foreach (var (key, value) in changes)
        {
            settings[key] = value;
        }

        return settings;
    }

    [Fact]
    public void Binds_provider_order_and_settings()
    {
        using var provider = BuildProvider(TwoProviders);

        var options = provider.GetRequiredService<IOptions<ToolkitAIOptions>>().Value;

        Assert.Equal(["openrouter", "ollama"], options.ProviderOrder);
        Assert.Equal(ProviderType.OpenAICompatible, options.Providers["openrouter"].Type);
        Assert.Equal(120, options.Providers["ollama"].TimeoutSeconds);
    }

    [Fact]
    public void Skips_a_cloud_provider_with_no_api_key()
    {
        using var provider = BuildProvider(TwoProviders);

        var router = provider.GetRequiredService<IChatClient>().GetService<ToolkitRoutingChatClient>();

        Assert.NotNull(router);
        Assert.Equal(["ollama"], router.Providers.Select(p => p.Name));
    }

    [Fact]
    public void Routes_to_both_providers_in_order_once_a_key_is_set()
    {
        using var provider = BuildProvider(With(("ToolkitAI:Providers:openrouter:ApiKey", "test-key")));

        var chat = provider.GetRequiredService<IChatClient>();
        var router = chat.GetService<ToolkitRoutingChatClient>();

        // Pipeline order: logging outermost, the router inside it, the vendor clients inside that.
        Assert.IsType<LoggingChatClient>(chat);
        Assert.Equal(["openrouter", "ollama"], router!.Providers.Select(p => p.Name));
        Assert.NotNull(chat.GetService<OllamaApiClient>());
    }

    [Fact]
    public void Rejects_a_provider_order_that_names_a_missing_provider()
    {
        using var provider = BuildProvider(With(("ToolkitAI:ProviderOrder:2", "nowhere")));

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<ToolkitAIOptions>>().Value);

        Assert.Contains("'nowhere'", ex.Message);
    }

    [Fact]
    public void Rejects_an_empty_model_name()
    {
        using var provider = BuildProvider(With(("ToolkitAI:Providers:ollama:Model", "")));

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<ToolkitAIOptions>>().Value);
    }
}

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OllamaSharp;

namespace Toolkit.AI.Tests;

// These tests check the wiring only. They never call Ollama, so they pass offline and in CI.
public class AddToolkitAITests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolkitAI(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Binds_options_from_configuration()
    {
        using var provider = BuildProvider(new()
        {
            ["ToolkitAI:Ollama:Endpoint"] = "http://otherhost:1234",
            ["ToolkitAI:Ollama:Model"] = "llama3.2:3b",
        });

        var options = provider.GetRequiredService<IOptions<ToolkitAIOptions>>().Value;

        Assert.Equal(new Uri("http://otherhost:1234"), options.Ollama.Endpoint);
        Assert.Equal("llama3.2:3b", options.Ollama.Model);
    }

    [Fact]
    public void Resolves_a_pipeline_with_Ollama_innermost()
    {
        using var provider = BuildProvider(new());

        var chat = provider.GetRequiredService<IChatClient>();

        // The outer client is the logging middleware; GetService walks down the
        // pipeline until some layer can answer, so we can find the inner Ollama client.
        Assert.IsType<LoggingChatClient>(chat);
        Assert.NotNull(chat.GetService<OllamaApiClient>());
    }

    [Fact]
    public void Rejects_an_empty_model_name()
    {
        using var provider = BuildProvider(new() { ["ToolkitAI:Ollama:Model"] = "" });

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<ToolkitAIOptions>>().Value);
    }
}

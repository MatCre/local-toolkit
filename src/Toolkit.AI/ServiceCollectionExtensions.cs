using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Toolkit.AI.CallLog;
using Toolkit.AI.Routing;

namespace Toolkit.AI;

/// <summary>
/// The single entry point other projects use: <c>services.AddToolkitAI(config)</c>.
/// Callers then ask DI for <see cref="IChatClient"/> and never see which vendor answers.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddToolkitAI(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Bind the "ToolkitAI" section and check all of it when the app starts.
        services.AddOptions<ToolkitAIOptions>()
            .Bind(configuration.GetSection(ToolkitAIOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ToolkitAIOptions>, ToolkitAIOptionsValidator>());

        // 2. Supporting services. TryAdd lets an app or a test register its own version first.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAiCallLog>(sp =>
        {
            var callLog = sp.GetRequiredService<IOptions<ToolkitAIOptions>>().Value.CallLog;
            return callLog.Enabled ? new SqliteAiCallLog(callLog.DatabasePath) : NullAiCallLog.Instance;
        });

        // 3. The pipeline. AddChatClient takes the innermost client and each .UseXxx() wraps it.
        //
        //    Request flow:  caller -> [Logging] -> ToolkitRoutingChatClient -> openrouter | ollama
        //
        //    Session 3 adds .UseDistributedCache() and a BudgetChatClient layer here.
        services.AddChatClient(sp =>
            {
                var options = sp.GetRequiredService<IOptions<ToolkitAIOptions>>().Value;
                var logger = sp.GetRequiredService<ILogger<ToolkitRoutingChatClient>>();
                return new ToolkitRoutingChatClient(
                    ChatProviderFactory.Create(options, logger),
                    sp.GetRequiredService<IAiCallLog>(),
                    logger,
                    sp.GetRequiredService<TimeProvider>());
            })
            .UseLogging();

        return services;
    }
}

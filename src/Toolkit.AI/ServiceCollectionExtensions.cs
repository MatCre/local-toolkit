using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Toolkit.AI.Budget;
using Toolkit.AI.Caching;
using Toolkit.AI.CallLog;
using Toolkit.AI.Routing;

namespace Toolkit.AI;

/// <summary>
/// The single entry point other projects use: <c>services.AddToolkitAI(config)</c>.
/// Callers then ask DI for <see cref="IChatClient"/> and never see which vendor answers.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// DI key for the AI layer's own response cache. Keyed so it never collides with an
    /// <see cref="IDistributedCache"/> the app registers for something else.
    /// </summary>
    public const string ResponseCacheKey = "toolkit-ai-responses";

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
            var callLog = Options(sp).CallLog;
            return callLog.Enabled ? new SqliteAiCallLog(callLog.DatabasePath) : NullAiCallLog.Instance;
        });
        services.TryAddKeyedSingleton<IDistributedCache>(ResponseCacheKey, (sp, _) =>
        {
            var cache = Options(sp).Cache;
            return new SqliteDistributedCache(
                cache.DatabasePath,
                TimeSpan.FromHours(cache.ExpirationHours),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<SqliteDistributedCache>>());
        });

        // 3. The pipeline. AddChatClient takes the innermost client. Each .Use...() wraps what
        //    comes after it, so the first one listed is the outermost layer.
        //
        //    Request flow:  caller -> Logging -> Cache -> Budget -> Router -> openrouter | ollama
        //
        //    Logging sees everything, cache hits included. The cache sits outside the budget
        //    check, so a cached answer costs nothing and is never counted against the cap.
        //    Cache and budget are skipped when config turns them off.
        services.AddChatClient(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<ToolkitRoutingChatClient>>();
                return new ToolkitRoutingChatClient(
                    ChatProviderFactory.Create(Options(sp), logger),
                    sp.GetRequiredService<IAiCallLog>(),
                    logger,
                    sp.GetRequiredService<TimeProvider>());
            })
            .UseLogging()
            .Use((inner, sp) => CreateCache(inner, sp))
            .Use((inner, sp) => Options(sp).Budget.MonthlyCapUsd is { } cap
                ? new BudgetChatClient(
                    inner,
                    sp.GetRequiredService<IAiCallLog>(),
                    cap,
                    sp.GetRequiredService<ILogger<BudgetChatClient>>(),
                    sp.GetRequiredService<TimeProvider>())
                : inner);

        return services;
    }

    private static IChatClient CreateCache(IChatClient inner, IServiceProvider sp)
    {
        var cache = Options(sp).Cache;
        if (!cache.Enabled)
        {
            return inner;
        }

        var storage = sp.GetRequiredKeyedService<IDistributedCache>(ResponseCacheKey);
        return cache.NormalizeKeys
            ? new NormalizingCachingChatClient(inner, storage)
            : new DistributedCachingChatClient(inner, storage);
    }

    private static ToolkitAIOptions Options(IServiceProvider sp) =>
        sp.GetRequiredService<IOptions<ToolkitAIOptions>>().Value;
}

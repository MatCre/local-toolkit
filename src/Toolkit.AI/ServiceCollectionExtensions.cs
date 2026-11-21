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
/// The single entry point other projects use. Callers then ask DI for <see cref="IChatClient"/>
/// and never see which vendor answers or where data is stored.
/// </summary>
/// <example>
/// <code>
/// services.AddToolkitAI(configuration)   // the pipeline, routing, budget and cache logic
///         .AddToolkitAISqlite();         // where the call log and cache are stored
/// </code>
/// </example>
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
        //    Storage is not registered here: a storage adapter such as AddToolkitAISqlite() provides
        //    IAiCallLog and the response cache's IDistributedCache.
        services.TryAddSingleton(TimeProvider.System);

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
                    CallLog(sp),
                    logger,
                    sp.GetRequiredService<TimeProvider>());
            })
            .UseLogging()
            .Use((inner, sp) => CreateCache(inner, sp))
            .Use((inner, sp) => Options(sp).Budget.MonthlyCapUsd is { } cap
                ? new BudgetChatClient(
                    inner,
                    CallLog(sp),
                    cap,
                    sp.GetRequiredService<ILogger<BudgetChatClient>>(),
                    sp.GetRequiredService<TimeProvider>())
                : inner);

        return services;
    }

    /// <summary>
    /// The registered call log, or one that records nothing when ToolkitAI:CallLog:Enabled is false.
    /// A missing registration is an error, not a silent no-op: the budget cap reads its spending
    /// from the call log, so quietly logging nothing would also quietly switch the cap off.
    /// </summary>
    private static IAiCallLog CallLog(IServiceProvider sp)
    {
        if (!Options(sp).CallLog.Enabled)
        {
            return NullAiCallLog.Instance;
        }

        return sp.GetService<IAiCallLog>() ?? throw new InvalidOperationException(
            "The AI call log is enabled but no storage is registered. Add a storage adapter after " +
            "AddToolkitAI, e.g. services.AddToolkitAI(config).AddToolkitAISqlite(), or set ToolkitAI:CallLog:Enabled=false.");
    }

    private static IChatClient CreateCache(IChatClient inner, IServiceProvider sp)
    {
        var cache = Options(sp).Cache;
        if (!cache.Enabled)
        {
            return inner;
        }

        var storage = sp.GetKeyedService<IDistributedCache>(ResponseCacheKey) ?? throw new InvalidOperationException(
            "The AI response cache is enabled but no storage is registered. Add a storage adapter after " +
            "AddToolkitAI, e.g. services.AddToolkitAI(config).AddToolkitAISqlite(), or set ToolkitAI:Cache:Enabled=false.");

        return new ToolkitCachingChatClient(inner, storage, cache.NormalizeKeys);
    }

    private static ToolkitAIOptions Options(IServiceProvider sp) =>
        sp.GetRequiredService<IOptions<ToolkitAIOptions>>().Value;
}

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Toolkit.AI.CallLog;

namespace Toolkit.AI.Sqlite;

public static class SqliteStorageServiceCollectionExtensions
{
    /// <summary>
    /// Stores the AI layer's call log and response cache in SQLite files, at the paths in
    /// ToolkitAI:CallLog:DatabasePath and ToolkitAI:Cache:DatabasePath.
    /// </summary>
    /// <example><c>services.AddToolkitAI(config).AddToolkitAISqlite();</c></example>
    /// <remarks>
    /// This is one storage adapter. Another (EF Core, SQL Server) would provide its own
    /// Add... method registering its own <see cref="IAiCallLog"/> and keyed
    /// <see cref="IDistributedCache"/>, and the app would call that instead.
    /// </remarks>
    public static IServiceCollection AddToolkitAISqlite(this IServiceCollection services)
    {
        services.TryAddSingleton<IAiCallLog>(sp => new SqliteAiCallLog(Options(sp).CallLog.DatabasePath));

        services.TryAddKeyedSingleton<IDistributedCache>(ServiceCollectionExtensions.ResponseCacheKey, (sp, _) =>
        {
            var cache = Options(sp).Cache;
            return new SqliteDistributedCache(
                cache.DatabasePath,
                TimeSpan.FromHours(cache.ExpirationHours),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<SqliteDistributedCache>>());
        });

        return services;
    }

    private static ToolkitAIOptions Options(IServiceProvider sp) =>
        sp.GetRequiredService<IOptions<ToolkitAIOptions>>().Value;
}

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Toolkit.AI;

namespace Toolkit.Vault.AI;

public static class VaultCaptureServiceCollectionExtensions
{
    /// <summary>
    /// Saves every question and answer that goes through the AI layer into the vault. Needs an
    /// <see cref="IVaultStore"/> registered too, e.g. <c>AddVaultSqlite()</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddToolkitAI(config).AddToolkitAISqlite()
    ///         .AddVaultSqlite()
    ///         .AddVaultCapture();
    /// </code>
    /// </example>
    public static IServiceCollection AddVaultCapture(this IServiceCollection services)
    {
        services.AddSingleton<IToolkitChatLayer, VaultCaptureLayer>();
        return services;
    }

    private sealed class VaultCaptureLayer : IToolkitChatLayer
    {
        public IChatClient Wrap(IChatClient inner, IServiceProvider services) => new VaultCaptureChatClient(
            inner,
            services.GetService<IVaultStore>() ?? throw new InvalidOperationException(
                "AddVaultCapture needs a vault store. Register one as well, e.g. services.AddVaultSqlite()."),
            services.GetRequiredService<ILogger<VaultCaptureChatClient>>());
    }
}

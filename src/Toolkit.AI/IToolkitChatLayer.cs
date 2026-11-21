using Microsoft.Extensions.AI;

namespace Toolkit.AI;

/// <summary>
/// A pipeline layer contributed by another package, without Toolkit.AI having to know about it.
/// Register one in DI and <see cref="ServiceCollectionExtensions.AddToolkitAI"/> includes it.
/// </summary>
/// <remarks>
/// <para>
/// Extension layers sit inside the cache and outside the budget check:
/// <c>Logging -> Cache -> [extension layers] -> Budget -> Router</c>. So they see only real
/// calls (never cache hits) and every request that reaches a provider, local or cloud.
/// Several layers apply in registration order, the first registered being outermost.
/// </para>
/// <para>
/// This is how the vault's auto-capture plugs in: Toolkit.Vault.AI registers a layer, and the AI
/// core keeps no reference to the vault. It's the same dependency rule as the storage adapters:
/// references point inward, to the core.
/// </para>
/// </remarks>
public interface IToolkitChatLayer
{
    /// <summary>Returns a client that wraps <paramref name="inner"/>, usually a <see cref="DelegatingChatClient"/>.</summary>
    IChatClient Wrap(IChatClient inner, IServiceProvider services);
}

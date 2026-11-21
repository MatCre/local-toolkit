namespace Toolkit.AI.Routing;

/// <summary>
/// Keys an outer pipeline layer can put in <c>ChatOptions.AdditionalProperties</c> to steer
/// the router. The router removes them before the request reaches a provider.
/// </summary>
public static class RoutingHints
{
    /// <summary>Set to <c>true</c> to allow only providers that run on this machine.</summary>
    public const string LocalOnly = "toolkit.localOnly";
}

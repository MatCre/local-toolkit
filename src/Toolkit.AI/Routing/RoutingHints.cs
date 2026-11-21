namespace Toolkit.AI.Routing;

/// <summary>
/// Keys an outer pipeline layer can put in <c>ChatOptions.AdditionalProperties</c> to steer
/// the router. The router removes every key starting with <see cref="Prefix"/> before the request
/// reaches a provider, so hints from any layer (routing, vault capture, ...) never leak to a vendor.
/// </summary>
public static class RoutingHints
{
    /// <summary>All toolkit hints start with this, and none are sent to providers.</summary>
    public const string Prefix = "toolkit.";

    /// <summary>Set to <c>true</c> to allow only providers that run on this machine.</summary>
    public const string LocalOnly = "toolkit.localOnly";
}

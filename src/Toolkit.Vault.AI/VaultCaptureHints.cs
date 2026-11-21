using Microsoft.Extensions.AI;
using Toolkit.AI.Routing;

namespace Toolkit.Vault.AI;

/// <summary>Per-request control over vault capture.</summary>
public static class VaultCaptureHints
{
    /// <summary>
    /// Set to <c>true</c> in <c>ChatOptions.AdditionalProperties</c> to keep a request and its answer
    /// out of the vault, e.g. anything containing bank or personal data. Starts with the
    /// <see cref="RoutingHints.Prefix"/>, so the router strips it before the request reaches a provider.
    /// </summary>
    public const string Skip = RoutingHints.Prefix + "vault.skip";

    /// <summary>Returns options that tell the vault capture layer not to save this request.</summary>
    /// <example><c>await chat.GetResponseAsync(prompt, new ChatOptions().WithoutVaultCapture());</c></example>
    public static ChatOptions WithoutVaultCapture(this ChatOptions? options)
    {
        var copy = options?.Clone() ?? new ChatOptions();
        (copy.AdditionalProperties ??= new())[Skip] = true;
        return copy;
    }

    internal static bool IsSkipped(ChatOptions? options) =>
        options?.AdditionalProperties?.TryGetValue(Skip, out var skip) == true && skip is true;
}

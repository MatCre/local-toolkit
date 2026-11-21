using Microsoft.Extensions.AI;

namespace Toolkit.AI.Routing;

/// <summary>One entry in the router's list: a named client plus the settings the router needs around it.</summary>
/// <param name="IsLocal">True when the model runs on this machine. Only local providers are used once the budget is spent.</param>
public sealed record ChatProvider(
    string Name,
    IChatClient Client,
    TimeSpan Timeout,
    decimal InputPricePerMillionTokens = 0,
    decimal OutputPricePerMillionTokens = 0,
    bool IsLocal = false)
{
    /// <summary>Estimated USD cost from token counts. Null when the provider reported no usage.</summary>
    public decimal? EstimateCost(UsageDetails? usage)
    {
        if (usage is null)
        {
            return null;
        }

        return ((usage.InputTokenCount ?? 0) * InputPricePerMillionTokens
              + (usage.OutputTokenCount ?? 0) * OutputPricePerMillionTokens) / 1_000_000m;
    }
}

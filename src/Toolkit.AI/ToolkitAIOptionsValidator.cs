using Microsoft.Extensions.Options;

namespace Toolkit.AI;

/// <summary>
/// Checks the whole "ToolkitAI" section at startup and reports every problem at once,
/// instead of failing on the first call with a less helpful error.
/// </summary>
internal sealed class ToolkitAIOptionsValidator : IValidateOptions<ToolkitAIOptions>
{
    public ValidateOptionsResult Validate(string? name, ToolkitAIOptions options)
    {
        var errors = new List<string>();

        if (options.ProviderOrder.Count == 0)
        {
            errors.Add("ToolkitAI:ProviderOrder must list at least one provider.");
        }

        foreach (var providerName in options.ProviderOrder)
        {
            var provider = options.FindProvider(providerName);
            if (provider is null)
            {
                errors.Add($"ToolkitAI:ProviderOrder names '{providerName}', but there is no ToolkitAI:Providers:{providerName} section.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(provider.Model))
            {
                errors.Add($"ToolkitAI:Providers:{providerName}:Model must be set.");
            }

            if (provider.Type == ProviderType.OpenAICompatible && provider.Endpoint is null)
            {
                errors.Add($"ToolkitAI:Providers:{providerName}:Endpoint must be set for an OpenAI-compatible provider.");
            }

            if (provider.TimeoutSeconds <= 0)
            {
                errors.Add($"ToolkitAI:Providers:{providerName}:TimeoutSeconds must be greater than 0.");
            }
        }

        if (options.Budget.MonthlyCapUsd < 0)
        {
            errors.Add("ToolkitAI:Budget:MonthlyCapUsd cannot be negative.");
        }

        if (options.Cache.Enabled && options.Cache.ExpirationHours <= 0)
        {
            errors.Add("ToolkitAI:Cache:ExpirationHours must be greater than 0.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

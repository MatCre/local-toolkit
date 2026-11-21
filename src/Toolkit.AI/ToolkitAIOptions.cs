namespace Toolkit.AI;

/// <summary>
/// Strongly-typed settings for the AI layer, bound from the "ToolkitAI" configuration section.
/// </summary>
/// <example>
/// <code>
/// "ToolkitAI": {
///   "ProviderOrder": [ "openrouter", "ollama" ],
///   "Providers": {
///     "openrouter": { "Type": "OpenAICompatible", "Endpoint": "https://openrouter.ai/api/v1", "Model": "openrouter/free" },
///     "ollama":     { "Type": "Ollama", "Model": "qwen2.5-coder:7b", "TimeoutSeconds": 120 }
///   }
/// }
/// </code>
/// Secrets never go in appsettings.json. Set them per provider with
/// <c>dotnet user-secrets set ToolkitAI:Providers:openrouter:ApiKey &lt;key&gt;</c>.
/// </example>
public sealed class ToolkitAIOptions
{
    public const string SectionName = "ToolkitAI";

    /// <summary>Provider names to try, first to last. The router falls back down this list.</summary>
    public List<string> ProviderOrder { get; set; } = [];

    /// <summary>Provider settings keyed by name. Names are matched case-insensitively.</summary>
    public Dictionary<string, ProviderOptions> Providers { get; set; } = [];

    public CallLogOptions CallLog { get; set; } = new();

    public BudgetOptions Budget { get; set; } = new();

    public CacheOptions Cache { get; set; } = new();

    internal ProviderOptions? FindProvider(string name) =>
        Providers.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
}

public enum ProviderType
{
    /// <summary>A local Ollama server, via OllamaSharp.</summary>
    Ollama,

    /// <summary>Any server that speaks the OpenAI chat API: OpenRouter, OpenAI itself, LM Studio and others.</summary>
    OpenAICompatible,
}

public sealed class ProviderOptions
{
    public ProviderType Type { get; set; }

    /// <summary>Base URL. Optional for Ollama (defaults to http://localhost:11434), required otherwise.</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>Model ID as the provider names it, e.g. "qwen2.5-coder:7b" or "openrouter/free".</summary>
    public string Model { get; set; } = "";

    /// <summary>Required for OpenAI-compatible providers. Comes from user secrets or an environment variable.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// How long to wait before giving up and trying the next provider. For streaming calls this
    /// covers the wait for the first token only. Ollama needs a generous value: loading a model
    /// from disk took ~57 s on the first call.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>USD per million input tokens, for the call log's cost estimate. 0 for free and local models.</summary>
    public decimal InputPricePerMillionTokens { get; set; }

    /// <summary>USD per million output tokens, for the call log's cost estimate. 0 for free and local models.</summary>
    public decimal OutputPricePerMillionTokens { get; set; }
}

public sealed class CallLogOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Where the storage adapter keeps the call log. For the SQLite adapter, a file path; relative
    /// paths resolve against the current directory.
    /// </summary>
    public string DatabasePath { get; set; } = "data/toolkit-ai.db";
}

public sealed class BudgetOptions
{
    /// <summary>
    /// Estimated USD to spend on cloud providers per calendar month. Once this month's total in
    /// the call log reaches it, requests go to local providers only. Null means no cap.
    /// </summary>
    public decimal? MonthlyCapUsd { get; set; }
}

public sealed class CacheOptions
{
    /// <summary>When on, an identical request (same messages and options) is answered from the cache.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Where the storage adapter keeps cached responses. For the SQLite adapter, a file path: delete it to clear the cache.</summary>
    public string DatabasePath { get; set; } = "data/response-cache.db";

    /// <summary>How long a cached answer stays valid.</summary>
    public int ExpirationHours { get; set; } = 168;

    /// <summary>
    /// Ignore case, extra whitespace and trailing . ? ! when matching prompts, so
    /// "What is SQLite?" and "what is sqlite" share an entry. Turn off if prompts carry
    /// code where case or indentation changes the meaning.
    /// </summary>
    public bool NormalizeKeys { get; set; } = true;
}

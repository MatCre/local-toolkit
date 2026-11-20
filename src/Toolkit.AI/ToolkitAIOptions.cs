namespace Toolkit.AI;

/// <summary>
/// Strongly-typed settings for the AI layer, bound from the "ToolkitAI" section
/// of configuration (appsettings.json, appsettings.Local.json, user secrets, env vars).
/// </summary>
/// <remarks>
/// Session 1 only knows about Ollama. Session 2 adds a cloud provider and a
/// provider order; those become new properties here, not new code paths in callers.
/// </remarks>
public sealed class ToolkitAIOptions
{
    public const string SectionName = "ToolkitAI";

    public OllamaOptions Ollama { get; set; } = new();
}

public sealed class OllamaOptions
{
    /// <summary>Where the Ollama server listens. Default install uses port 11434.</summary>
    public Uri Endpoint { get; set; } = new("http://localhost:11434");

    /// <summary>A model you have already pulled, e.g. "qwen2.5-coder:7b".</summary>
    public string Model { get; set; } = "qwen2.5-coder:7b";
}

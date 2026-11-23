using System.Text.Json;

namespace Toolkit.Rules;

/// <summary>Reads and writes a <see cref="RuleSet"/> as a rules.json file, in priority order.</summary>
public static class RuleSetFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>An empty <see cref="RuleSet"/> if the file doesn't exist yet: there's nothing to add until the first save.</summary>
    public static RuleSet Load(string path)
    {
        if (!File.Exists(path))
        {
            return RuleSet.Empty;
        }

        var json = File.ReadAllText(path);
        var entries = JsonSerializer.Deserialize<List<RuleJson>>(json, JsonOptions) ?? [];
        return new RuleSet(entries.Select(e => new CategoryRule(e.Pattern, e.Category, e.IsRegex)).ToList());
    }

    public static void Save(string path, RuleSet ruleSet)
    {
        var entries = ruleSet.Rules.Select(r => new RuleJson(r.Pattern, r.Category, r.IsRegex));
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(entries, JsonOptions));
    }

    private sealed record RuleJson(string Pattern, string Category, bool IsRegex = false);
}

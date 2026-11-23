using System.Text.Json;
using System.Text.RegularExpressions;

namespace Toolkit.Rules;

/// <summary>Reads and writes a <see cref="RuleSet"/> as a rules.json file, in priority order.</summary>
public static class RuleSetFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// An empty <see cref="RuleSet"/> if the file doesn't exist yet: there's nothing to add until the first save.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The file isn't valid JSON, or a rule is missing its pattern or category, or has a regex that
    /// doesn't compile. The message names the file and the rule, so a hand edit is easy to fix.
    /// </exception>
    public static RuleSet Load(string path)
    {
        if (!File.Exists(path))
        {
            return RuleSet.Empty;
        }

        List<RuleJson?>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<RuleJson?>>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path} isn't valid rules JSON: {ex.Message}", ex);
        }

        var rules = new List<CategoryRule>();
        for (var i = 0; i < (entries?.Count ?? 0); i++)
        {
            rules.Add(Validate(entries![i], path, ruleNumber: i + 1));
        }

        return new RuleSet(rules);
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

    private static CategoryRule Validate(RuleJson? entry, string path, int ruleNumber)
    {
        string Problem(string what) => $"{path}, rule {ruleNumber}: {what}.";

        if (entry is null)
        {
            throw new InvalidDataException(Problem("is null; each rule needs a \"pattern\" and a \"category\""));
        }

        if (string.IsNullOrWhiteSpace(entry.Pattern))
        {
            throw new InvalidDataException(Problem("has no \"pattern\""));
        }

        if (string.IsNullOrWhiteSpace(entry.Category))
        {
            throw new InvalidDataException(Problem($"(\"{entry.Pattern}\") has no \"category\""));
        }

        if (entry.IsRegex)
        {
            try
            {
                _ = new Regex(entry.Pattern);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidDataException(Problem($"\"{entry.Pattern}\" isn't a valid regular expression ({ex.Message})"), ex);
            }
        }

        return new CategoryRule(entry.Pattern, entry.Category, entry.IsRegex);
    }

    // Nullable on purpose: a hand-edited file may leave a field out, and that should be reported, not crash.
    private sealed record RuleJson(string? Pattern, string? Category, bool IsRegex = false);
}

namespace Toolkit.Rules.Tests;

public class RuleSetFileTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rules-{Guid.NewGuid():N}.json");

    [Fact]
    public void Load_of_a_missing_file_is_an_empty_rule_set() =>
        Assert.Empty(RuleSetFile.Load(_path).Rules);

    [Fact]
    public void Save_then_load_round_trips_order_and_fields()
    {
        var original = new RuleSet([
            new CategoryRule("COUNTDOWN", "Groceries"),
            new CategoryRule(@"^UBER \*TRIP", "Transport", IsRegex: true),
        ]);

        RuleSetFile.Save(_path, original);
        var loaded = RuleSetFile.Load(_path);

        Assert.Equal(original.Rules, loaded.Rules);
    }

    [Fact]
    public void Save_creates_missing_parent_directories()
    {
        var nested = Path.Combine(Path.GetTempPath(), $"rules-dir-{Guid.NewGuid():N}", "rules.json");

        try
        {
            RuleSetFile.Save(nested, new RuleSet([new CategoryRule("KMART", "Shopping")]));

            Assert.True(File.Exists(nested));
        }
        finally
        {
            var directory = Path.GetDirectoryName(nested)!;
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("""[{"pattern": "COUNTDOWN", "category": "Groceries"},]""", "isn't valid rules JSON")]
    [InlineData("""[{"category": "Groceries"}]""", "rule 1: has no \"pattern\"")]
    [InlineData("""[{"pattern": "KMART", "category": "Shopping"}, {"pattern": "UBER"}]""", "rule 2: (\"UBER\") has no \"category\"")]
    [InlineData("""[{"pattern": "(COUNTDOWN", "category": "Groceries", "isRegex": true}]""", "isn't a valid regular expression")]
    [InlineData("""[null]""", "rule 1: is null")]
    public void A_broken_file_is_reported_with_the_file_and_rule(string json, string expected)
    {
        File.WriteAllText(_path, json);

        var ex = Assert.Throws<InvalidDataException>(() => RuleSetFile.Load(_path));

        Assert.Contains(_path, ex.Message);
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void A_plain_pattern_with_regex_characters_is_fine()
    {
        File.WriteAllText(_path, """[{"pattern": "UBER *TRIP (", "category": "Transport"}]""");

        Assert.Single(RuleSetFile.Load(_path).Rules); // only regex rules are compiled and checked
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}

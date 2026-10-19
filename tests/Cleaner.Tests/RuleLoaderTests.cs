using Cleaner.Core.Models;
using Cleaner.Core.Rules;
using Xunit;

namespace Cleaner.Tests;

public class RuleLoaderTests
{
    [Fact]
    public void Parses_rule_with_comments_and_enum()
    {
        const string json = """
            {
              // kommentti
              "schemaVersion": 1,
              "rules": [
                { "id": "a", "name": "A", "paths": ["%TEMP%"], "risk": "Medium", "minAgeHours": 5 }
              ]
            }
            """;

        var set = RuleLoader.LoadFromJson(json);

        var rule = Assert.Single(set.Rules);
        Assert.Equal("a", rule.Id);
        Assert.Equal(RiskLevel.Medium, rule.Risk);
        Assert.Equal(5, rule.MinAgeHours);
        Assert.True(rule.Recursive);
    }

    [Fact]
    public void Rejects_duplicate_ids()
    {
        const string json = """
            { "rules": [
              { "id": "x", "name": "1", "paths": ["a"] },
              { "id": "X", "name": "2", "paths": ["b"] }
            ] }
            """;
        Assert.Throws<InvalidDataException>(() => RuleLoader.LoadFromJson(json));
    }

    [Fact]
    public void Rejects_rule_without_paths()
    {
        const string json = """{ "rules": [ { "id": "x", "name": "1", "paths": [] } ] }""";
        Assert.Throws<InvalidDataException>(() => RuleLoader.LoadFromJson(json));
    }

    [Fact]
    public void Shipped_rule_files_are_valid()
    {
        // Varmistaa, ettei kukaan riko oikeita sääntötiedostoja.
        var dir = Path.Combine(AppContext.BaseDirectory, "rules");
        var files = Directory.GetFiles(dir, "*.json");
        Assert.NotEmpty(files);
        foreach (var f in files)
            RuleLoader.LoadFromFile(f);
    }
}

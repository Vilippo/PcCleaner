using Cleaner.Core.Models;
using Cleaner.Modules.Processes;
using Xunit;

namespace Cleaner.Tests;

public class ProcessRuleLoaderTests
{
    [Fact]
    public void Shipped_file_loads_with_at_least_six_rules()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "rules", "processes", "known.json");
        Assert.True(File.Exists(path), $"Sääntötiedostoa ei kopioitu tulosteeseen: {path}");

        var set = ProcessRuleLoader.LoadFromFile(path);

        Assert.Equal(1, set.SchemaVersion);
        Assert.True(set.Rules.Count >= 6, $"Sääntöjä oli {set.Rules.Count}");
        Assert.Contains(set.Rules, r => r.Id == "google-crash-handler" && r.Risk == RiskLevel.Medium && r.Matches("GoogleCrashHandler64.exe"));
        Assert.Contains(set.Rules, r => r.Id == "adobe-ipc-broker" && r.Matches("AdobeIPCBroker"));
        Assert.Contains(set.Rules, r => r.Id == "adobe-ccx-process" && r.Matches("CCXProcess"));
        Assert.Contains(set.Rules, r => r.Id == "adobe-collab-sync" && r.Matches("AdobeCollabSync"));
        Assert.Contains(set.Rules, r => r.Id == "steam-webhelper" && r.Risk == RiskLevel.Low && r.Matches("steamwebhelper"));
        Assert.Contains(set.Rules, r => r.Id == "discord-helper" && r.Risk == RiskLevel.Low && r.Matches("DiscordPTB"));
    }

    [Fact]
    public void Parses_comments_defaults_and_enum()
    {
        const string json = """
            {
              // kommentti
              "schemaVersion": 1,
              "rules": [
                { "id": "a", "name": "A", "match": ["a.exe"], },
                { "id": "b", "name": "B", "match": ["b"], "risk": "Low", "description": "kuvaus" }
              ]
            }
            """;

        var set = ProcessRuleLoader.LoadFromJson(json);

        Assert.Equal(2, set.Rules.Count);
        Assert.Equal(RiskLevel.Medium, set.Rules[0].Risk);
        Assert.Equal("", set.Rules[0].Description);
        Assert.Equal(RiskLevel.Low, set.Rules[1].Risk);
        Assert.Equal("kuvaus", set.Rules[1].Description);
    }

    [Fact]
    public void Rejects_duplicate_ids()
    {
        const string json = """
            { "rules": [
              { "id": "x", "name": "1", "match": ["a"] },
              { "id": "X", "name": "2", "match": ["b"] }
            ] }
            """;
        var ex = Assert.Throws<InvalidDataException>(() => ProcessRuleLoader.LoadFromJson(json, "testi"));
        Assert.Contains("testi", ex.Message);
        Assert.Contains("X", ex.Message);
    }

    [Fact]
    public void Rejects_empty_match()
    {
        const string json = """{ "rules": [ { "id": "x", "name": "1", "match": [] } ] }""";
        Assert.Throws<InvalidDataException>(() => ProcessRuleLoader.LoadFromJson(json));

        const string blank = """{ "rules": [ { "id": "x", "name": "1", "match": [" "] } ] }""";
        Assert.Throws<InvalidDataException>(() => ProcessRuleLoader.LoadFromJson(blank));
    }

    [Fact]
    public void Rejects_empty_id_and_name()
    {
        Assert.Throws<InvalidDataException>(() => ProcessRuleLoader.LoadFromJson(
            """{ "rules": [ { "id": "", "name": "1", "match": ["a"] } ] }"""));
        Assert.Throws<InvalidDataException>(() => ProcessRuleLoader.LoadFromJson(
            """{ "rules": [ { "id": "x", "name": " ", "match": ["a"] } ] }"""));
    }

    [Fact]
    public void Malformed_json_becomes_invalid_data()
    {
        Assert.Throws<InvalidDataException>(() => ProcessRuleLoader.LoadFromJson("{ \"rules\": [ { \"id\": "));
    }

    [Theory]
    [InlineData("GoogleCrashHandler.exe", "GoogleCrashHandler", true)]
    [InlineData("GoogleCrashHandler", "googlecrashhandler.exe", true)]
    [InlineData("GoogleCrashHandler.exe", "GOOGLECRASHHANDLER.EXE", true)]
    [InlineData("Discord*.exe", "DiscordPTB", true)]
    [InlineData("Discord*.exe", "Discord.exe", true)]
    [InlineData("Discord*", "discord", true)]
    [InlineData("steam?ebhelper", "steamwebhelper", true)]
    [InlineData("Discord*.exe", "NotDiscord.exe", false)]
    [InlineData("CCXProcess.exe", "CCX", false)]
    [InlineData("a.b", "aXb", false)]
    public void Matches_wildcards_and_optional_exe(string pattern, string name, bool expected)
    {
        var rule = new ProcessRule { Id = "r", Name = "R", Match = new List<string> { pattern } };
        Assert.Equal(expected, rule.Matches(name));
    }
}

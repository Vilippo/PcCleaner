using System.Text.Json;
using System.Text.Json.Serialization;
using Cleaner.Core.Localization;

namespace Cleaner.Core.Rules;

/// <summary>Lukee ja validoi sääntötiedostot.</summary>
public static class RuleLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static RuleSet LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(Loc.T($"Rule file not found: {path}", $"Sääntötiedostoa ei löydy: {path}"), path);

        var json = File.ReadAllText(path);
        return LoadFromJson(json, path);
    }

    public static RuleSet LoadFromJson(string json, string sourceName = "<json>")
    {
        var set = JsonSerializer.Deserialize<RuleSet>(json, Options)
                  ?? throw new InvalidDataException(Loc.T($"Rule file is empty: {sourceName}", $"Sääntötiedosto on tyhjä: {sourceName}"));

        Validate(set, sourceName);
        return set;
    }

    private static void Validate(RuleSet set, string sourceName)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in set.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id))
                throw new InvalidDataException(Loc.T($"{sourceName}: rule is missing an Id.", $"{sourceName}: säännöltä puuttuu Id."));
            if (!ids.Add(rule.Id))
                throw new InvalidDataException(Loc.T($"{sourceName}: rule Id '{rule.Id}' appears twice.", $"{sourceName}: sääntö-Id '{rule.Id}' esiintyy kahdesti."));
            if (rule.Paths.Count == 0)
                throw new InvalidDataException(Loc.T($"{sourceName}: rule '{rule.Id}' has no paths.", $"{sourceName}: säännöllä '{rule.Id}' ei ole polkuja."));
            if (rule.MinAgeHours < 0)
                throw new InvalidDataException(Loc.T($"{sourceName}: rule '{rule.Id}' MinAgeHours cannot be negative.", $"{sourceName}: säännön '{rule.Id}' MinAgeHours ei voi olla negatiivinen."));
        }
    }
}

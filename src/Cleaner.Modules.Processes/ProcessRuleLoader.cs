using System.Text.Json;
using System.Text.Json.Serialization;
using Cleaner.Core.Localization;

namespace Cleaner.Modules.Processes;

/// <summary>Lukee ja validoi prosessisääntöjen tiedoston. Samat JSON-asetukset kuin Cleaner.Core.Rules.RuleLoader.</summary>
public static class ProcessRuleLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static ProcessRuleSet LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(Loc.T($"Rule file not found: {path}", $"Sääntötiedostoa ei löydy: {path}"), path);

        var json = File.ReadAllText(path);
        return LoadFromJson(json, path);
    }

    /// <summary>Jäsentää ja validoi. Virheistä heitetään InvalidDataException, jonka viesti nimeää lähteen ja säännön.</summary>
    public static ProcessRuleSet LoadFromJson(string json, string sourceName = "<json>")
    {
        ProcessRuleSet? set;
        try
        {
            set = JsonSerializer.Deserialize<ProcessRuleSet>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{sourceName}: {ex.Message}", ex);
        }

        if (set is null)
            throw new InvalidDataException(Loc.T($"Rule file is empty: {sourceName}", $"Sääntötiedosto on tyhjä: {sourceName}"));

        Validate(set, sourceName);
        return set;
    }

    private static void Validate(ProcessRuleSet set, string sourceName)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in set.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id))
                throw new InvalidDataException(Loc.T($"{sourceName}: a rule is missing Id.", $"{sourceName}: säännöltä puuttuu Id."));
            if (!ids.Add(rule.Id))
                throw new InvalidDataException(Loc.T($"{sourceName}: rule Id '{rule.Id}' appears twice.", $"{sourceName}: sääntö-Id '{rule.Id}' esiintyy kahdesti."));
            if (string.IsNullOrWhiteSpace(rule.Name))
                throw new InvalidDataException(Loc.T($"{sourceName}: rule '{rule.Id}' is missing Name.", $"{sourceName}: säännöltä '{rule.Id}' puuttuu Name."));
            if (rule.Match is null || !rule.Match.Any(m => !string.IsNullOrWhiteSpace(m)))
                throw new InvalidDataException(Loc.T($"{sourceName}: rule '{rule.Id}' has no match patterns.", $"{sourceName}: säännöllä '{rule.Id}' ei ole yhtään match-kuviota."));
        }
    }
}

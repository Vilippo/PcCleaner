namespace Cleaner.Core.Rules;

/// <summary>JSON-tiedoston juurirakenne.</summary>
public sealed class RuleSet
{
    /// <summary>Tiedostomuodon versio tulevia muutoksia varten.</summary>
    public int SchemaVersion { get; init; } = 1;

    public List<CleanRule> Rules { get; init; } = new();
}

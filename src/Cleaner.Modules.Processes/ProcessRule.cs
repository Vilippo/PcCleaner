using System.Text.RegularExpressions;
using Cleaner.Core.Localization;
using Cleaner.Core.Models;

namespace Cleaner.Modules.Processes;

/// <summary>Yksi tunnetun taustaprosessin sääntö tiedostosta rules/processes/known.json.</summary>
public sealed class ProcessRule
{
    private Regex[]? _patterns;

    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Prosessinimen jokerikuviot (* ja ?). Kirjainkoko ei merkitse, .exe-pääte saa puuttua.</summary>
    public required List<string> Match { get; init; }

    public string Description { get; init; } = "";

    /// <summary>Suomenkielinen kuvaus (JSON descriptionFi). Puuttuessa käytetään <see cref="Description"/>-arvoa.</summary>
    public string? DescriptionFi { get; init; }

    /// <summary>Kuvaus nykyisellä kielellä.</summary>
    public string DisplayDescription => Loc.T(Description, DescriptionFi ?? Description);

    public RiskLevel Risk { get; init; } = RiskLevel.Medium;

    /// <summary>Osuuko prosessin nimi johonkin kuvioon. Nimi voi olla .exe-päätteellä tai ilman.</summary>
    public bool Matches(string processName)
    {
        if (string.IsNullOrEmpty(processName)) return false;
        var name = StripExe(processName);
        _patterns ??= Match.Where(m => !string.IsNullOrWhiteSpace(m)).Select(ToRegex).ToArray();
        return _patterns.Any(p => p.IsMatch(name));
    }

    private static Regex ToRegex(string pattern)
    {
        var escaped = Regex.Escape(StripExe(pattern.Trim())).Replace(@"\*", ".*").Replace(@"\?", ".");
        return new Regex("^" + escaped + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string StripExe(string s)
        => s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;
}

/// <summary>Sääntötiedoston juuri.</summary>
public sealed class ProcessRuleSet
{
    public int SchemaVersion { get; init; } = 1;
    public List<ProcessRule> Rules { get; init; } = new();
}

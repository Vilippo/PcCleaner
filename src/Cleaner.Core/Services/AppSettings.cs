using System.Text.Json;
using Cleaner.Core.Localization;

namespace Cleaner.Core.Services;

/// <summary>
/// Käyttäjän asetukset tiedostossa %LOCALAPPDATA%\PcCleaner\settings.json, esim. <c>{ "language": "en" }</c>.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>Kielikoodi "en" tai "fi".</summary>
    public string Language { get; set; } = "en";

    /// <summary>%LOCALAPPDATA%\PcCleaner\settings.json</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcCleaner", "settings.json");

    /// <summary>Puuttuva, rikkinäinen tai luvuton tiedosto palauttaa oletusarvot. Ei heitä koskaan.</summary>
    public static AppSettings Load(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            if (!File.Exists(file)) return new AppSettings();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), ReadOptions);
            if (settings is null) return new AppSettings();
            settings.Language ??= "en";   // JSONin "language": null
            return settings;
        }
        catch
        {
            // Asetukset eivät saa koskaan estää käynnistystä.
            return new AppSettings();
        }
    }

    /// <summary>Kirjoittaa asetukset JSONina ja luo kansion tarvittaessa. Saa heittää IO-poikkeuksen.</summary>
    public void Save(string? path = null)
    {
        var file = path ?? DefaultPath;
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(file, JsonSerializer.Serialize(this, WriteOptions));
    }

    /// <summary>Tunnettu kielikoodi palauttaa sen kielen, muuten <see cref="Localization.Language.En"/>.</summary>
    public Localization.Language ResolveLanguage() =>
        Loc.TryParse(Language, out var language) ? language : Localization.Language.En;
}

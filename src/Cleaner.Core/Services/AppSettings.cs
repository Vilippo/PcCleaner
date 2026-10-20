using System.Text.Json;
using Cleaner.Core.Localization;

namespace Cleaner.Core.Services;

/// <summary>
/// Käyttäjän asetukset tiedostossa %LOCALAPPDATA%\PcCleaner\settings.json, esim.
/// <c>{ "language": "en", "palette": "mint", "font": "bahnschrift" }</c>.
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

    /// <summary>Värimaailman tunnus, oletus "mint". Core ei validoi arvoa; UI ratkaisee tuntemattoman oletukseksi.</summary>
    public string Palette { get; set; } = "mint";

    /// <summary>Fontin tunnus, oletus "bahnschrift". Core ei validoi arvoa; UI ratkaisee tuntemattoman oletukseksi.</summary>
    public string Font { get; set; } = "bahnschrift";

    /// <summary>Tekoälyavustajan mallin tunnus sellaisenaan, oletus "claude-opus-5-5". Core ei validoi arvoa.</summary>
    public string AiModel { get; set; } = "claude-opus-5-5";

    /// <summary>Tekoälyavustajan Auto-tila: avustaja valitsee kohteet itse. Oletus pois.</summary>
    public bool AiAutoMode { get; set; }

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
            settings.Palette ??= "mint";
            settings.Font ??= "bahnschrift";
            settings.AiModel ??= "claude-opus-5-5";
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

    /// <summary>
    /// Lataa tiedoston, soveltaa muutoksen ja tallentaa, joten yhden asetuksen tallennus säilyttää muut.
    /// Saa heittää IO-poikkeuksen kuten <see cref="Save"/>.
    /// </summary>
    public static AppSettings Update(Action<AppSettings> change, string? path = null)
    {
        var settings = Load(path);
        change(settings);
        settings.Save(path);
        return settings;
    }

    /// <summary>Tunnettu kielikoodi palauttaa sen kielen, muuten <see cref="Localization.Language.En"/>.</summary>
    public Localization.Language ResolveLanguage() =>
        Loc.TryParse(Language, out var language) ? language : Localization.Language.En;
}

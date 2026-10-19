using Cleaner.Core.Models;

namespace Cleaner.Core.Rules;

/// <summary>
/// Yksi siivoussääntö JSON-tiedostosta. Kuvaa, MISTÄ etsitään ja MITÄ poistetaan.
/// Uusi siivouskohde lisätään kirjoittamalla uusi sääntö, ei koodia.
/// </summary>
public sealed class CleanRule
{
    /// <summary>Vakaa tunniste, esim. "windows-temp".</summary>
    public required string Id { get; init; }

    /// <summary>Käyttäjälle näkyvä nimi.</summary>
    public required string Name { get; init; }

    /// <summary>Ryhmä käyttöliittymässä, esim. "Windows", "Sovellukset".</summary>
    public string Category { get; init; } = "Yleinen";

    /// <summary>Miksi tämä on turvallista poistaa.</summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// Polut, joista etsitään. Ympäristömuuttujat sallittu: %TEMP%, %LOCALAPPDATA%, %WINDIR%.
    /// Polku voi myös sisältää * yhdessä kansion kohdassa, esim. "%LOCALAPPDATA%\Google\Chrome\User Data\*\Cache".
    /// </summary>
    public required List<string> Paths { get; init; }

    /// <summary>Tiedostonimisuodattimet, esim. ["*.tmp", "*.log"]. Tyhjä = kaikki tiedostot.</summary>
    public List<string> Patterns { get; init; } = new();

    /// <summary>Haetaanko myös alikansioista.</summary>
    public bool Recursive { get; init; } = true;

    /// <summary>
    /// Poistetaan vain tiedostot, joita ei ole muokattu näin moneen tuntiin.
    /// Suojaa käynnissä olevien ohjelmien tuoreita tiedostoja. 0 = ei rajaa.
    /// </summary>
    public int MinAgeHours { get; init; } = 24;

    /// <summary>Poistetaanko tyhjäksi jääneet alikansiot.</summary>
    public bool RemoveEmptyDirs { get; init; } = true;

    public RiskLevel Risk { get; init; } = RiskLevel.Low;

    public bool RequiresAdmin { get; init; }

    /// <summary>Onko sääntö oletuksena valittuna käyttöliittymässä.</summary>
    public bool EnabledByDefault { get; init; } = true;

    /// <summary>
    /// Jos jokin näistä prosesseista on käynnissä (ilman .exe), sääntö ohitetaan ja käyttäjälle kerrotaan.
    /// Esim. ["chrome"] selaimen välimuistille.
    /// </summary>
    public List<string> SkipIfProcessRunning { get; init; } = new();
}

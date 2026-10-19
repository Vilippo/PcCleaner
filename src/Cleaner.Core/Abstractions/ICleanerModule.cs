using Cleaner.Core.Models;

namespace Cleaner.Core.Abstractions;

/// <summary>
/// Jokainen siivousmoduuli (tiedostot, selaimet, käynnistys, rekisteri...) toteuttaa tämän.
/// Ydin ja käyttöliittymä puhuvat moduuleille vain tämän rajapinnan kautta.
/// </summary>
public interface ICleanerModule
{
    /// <summary>Vakaa tunniste, esim. "files", "browsers". Käytetään komentorivillä ja asetuksissa.</summary>
    string Id { get; }

    /// <summary>Käyttäjälle näytettävä nimi.</summary>
    string DisplayName { get; }

    /// <summary>Lyhyt kuvaus siitä, mitä moduuli tekee.</summary>
    string Description { get; }

    /// <summary>Moduulin korkein riskitaso (käyttöliittymän järjestelyä varten).</summary>
    RiskLevel Risk { get; }

    /// <summary>Tarvitseeko moduuli järjestelmänvalvojan oikeudet toimiakseen täysin.</summary>
    bool RequiresAdmin { get; }

    /// <summary>
    /// Otetaanko moduuli mukaan, kun käyttäjä ei rajaa moduuleja (CLI ilman --module, UI:n "skannaa kaikki").
    /// Epätosi moduuleille, joiden kohteet ovat käyttäjän omia valintoja (esim. käynnistysohjelmat):
    /// niitä ei pidä käsitellä kaikkia kerralla, vaan käyttäjä valitsee yksittäin.
    /// </summary>
    bool SelectedByDefault => true;

    /// <summary>Etsii poistettavat kohteet. EI muuta mitään.</summary>
    Task<ScanResult> ScanAsync(CleanerContext context, CancellationToken ct = default);

    /// <summary>
    /// Poistaa annetut kohteet. Kohteiden on oltava tämän moduulin ScanAsync-kutsusta saatuja.
    /// Jos context.DryRun on true, ei poista mitään vaan raportoi.
    /// </summary>
    Task<CleanResult> CleanAsync(IReadOnlyList<CleanItem> items, CleanerContext context, CancellationToken ct = default);
}

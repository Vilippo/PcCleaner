namespace Cleaner.Core.Models;

/// <summary>
/// Yksi skannauksessa löydetty poistettava kohde. Moduuli tuottaa näitä,
/// käyttäjä valitsee niistä osan ja moduuli poistaa valitut.
/// </summary>
/// <param name="Id">Yksilöllinen tunniste moduulin sisällä (yleensä polku).</param>
/// <param name="ModuleId">Moduuli, joka löysi kohteen ja osaa poistaa sen.</param>
/// <param name="Category">Ryhmä käyttöliittymässä, esim. "Windows Temp".</param>
/// <param name="Path">Tiedoston tai kansion täysi polku; rekisterikohteilla avaimen polku.</param>
/// <param name="SizeBytes">Koko tavuina. 0 jos ei sovellu (esim. rekisteriavain).</param>
/// <param name="Description">Lyhyt ihmisluettava selitys, miksi kohde on tarpeeton.</param>
/// <param name="Risk">Riskitaso.</param>
/// <param name="Preselected">
/// Kohteen oma esivalinta käyttöliittymässä: null = moduulin oletus (<c>SelectedByDefault</c>),
/// true/false = kohde on tai ei ole valittuna oletuksena. High-riskinen kohde ei ole koskaan esivalittu.
/// </param>
public sealed record CleanItem(
    string Id,
    string ModuleId,
    string Category,
    string Path,
    long SizeBytes,
    string Description,
    RiskLevel Risk,
    bool? Preselected = null);

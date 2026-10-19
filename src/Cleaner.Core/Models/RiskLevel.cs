namespace Cleaner.Core.Models;

/// <summary>
/// Kuinka riskialtis toimenpide on. Käyttöliittymä näyttää tämän värikoodilla
/// ja High-tason kohteet eivät ole koskaan oletuksena valittuina.
/// </summary>
public enum RiskLevel
{
    /// <summary>Turvallinen: esim. vanhat temp-tiedostot.</summary>
    Low = 0,

    /// <summary>Voi aiheuttaa harmia: esim. selaimen välimuisti (sivut latautuvat hitaammin).</summary>
    Medium = 1,

    /// <summary>Voi rikkoa jotain: esim. rekisterimuutokset, Windows.old.</summary>
    High = 2
}

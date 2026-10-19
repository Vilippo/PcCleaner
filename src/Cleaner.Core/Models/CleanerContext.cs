using Cleaner.Core.Logging;

namespace Cleaner.Core.Models;

/// <summary>
/// Yhteiset asetukset, jotka ydin antaa moduulille skannauksen ja puhdistuksen ajaksi.
/// </summary>
public sealed class CleanerContext
{
    /// <summary>
    /// Kun true, mitään ei oikeasti poisteta. Moduulit raportoivat mitä OLISI tapahtunut.
    /// Oletus on true, jotta vahinkoja ei satu.
    /// </summary>
    public bool DryRun { get; init; } = true;

    public required ICleanerLogger Logger { get; init; }

    /// <summary>Edistymisraportointi käyttöliittymälle (0–100 ja viesti). Voi olla null.</summary>
    public IProgress<(int Percent, string Message)>? Progress { get; init; }

    /// <summary>Onko prosessi käynnistetty järjestelmänvalvojana.</summary>
    public bool IsElevated { get; init; }

    public void Report(int percent, string message) => Progress?.Report((percent, message));
}

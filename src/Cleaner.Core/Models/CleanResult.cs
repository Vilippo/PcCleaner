namespace Cleaner.Core.Models;

/// <summary>Yhden moduulin puhdistuksen tulos.</summary>
public sealed class CleanResult
{
    public required string ModuleId { get; init; }

    /// <summary>Onnistuneesti poistetut kohteet (kuivassa ajossa: kohteet jotka OLISI poistettu).</summary>
    public List<CleanItem> Removed { get; } = new();

    /// <summary>Kohteet, joita ei voitu poistaa (lukittu, oikeudet puuttuvat), ja syy.</summary>
    public List<(CleanItem Item, string Reason)> Skipped { get; } = new();

    public List<string> Errors { get; } = new();

    public long FreedBytes => Removed.Sum(i => i.SizeBytes);
    public bool WasDryRun { get; init; }
}

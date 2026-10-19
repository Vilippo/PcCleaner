namespace Cleaner.Core.Models;

/// <summary>Yhden moduulin skannauksen tulos.</summary>
public sealed class ScanResult
{
    public required string ModuleId { get; init; }
    public List<CleanItem> Items { get; } = new();
    public List<string> Errors { get; } = new();

    public long TotalBytes => Items.Sum(i => i.SizeBytes);
    public int Count => Items.Count;
}

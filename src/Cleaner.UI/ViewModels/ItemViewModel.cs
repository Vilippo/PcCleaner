using Cleaner.Core.Models;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cleaner.UI.ViewModels;

/// <summary>Yksi löydös valintaruudulla.</summary>
public sealed partial class ItemViewModel : ObservableObject
{
    public CleanItem Item { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public ItemViewModel(CleanItem item, bool selected)
    {
        Item = item;
        IsSelected = selected;
    }

    public string Path => Item.Path;
    public long SizeBytes => Item.SizeBytes;
    public string SizeText => Item.SizeBytes > 0 ? ByteFormatter.Format(Item.SizeBytes) : "";
    public string Description => Item.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Item.Description);
    public RiskLevel Risk => Item.Risk;
}

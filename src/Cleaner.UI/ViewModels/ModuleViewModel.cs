using System.Collections.ObjectModel;
using Cleaner.Core.Abstractions;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cleaner.UI.ViewModels;

/// <summary>Yksi moduuli navigointilistassa ja sen viimeisin skannaustulos.</summary>
public sealed partial class ModuleViewModel : ObservableObject
{
    public ICleanerModule Module { get; }

    public string Id => Module.Id;
    public string DisplayName => Module.DisplayName;
    public string Description => Module.Description;
    public bool RequiresAdmin => Module.RequiresAdmin;
    public bool SelectedByDefault => Module.SelectedByDefault;

    public string RiskText => Module.Risk switch
    {
        RiskLevel.High => "Korkea riski",
        RiskLevel.Medium => "Keskiriski",
        _ => "Matala riski"
    };

    /// <summary>Segoe Fluent Icons -merkki navigointiin.</summary>
    public string Glyph => Module.Id switch
    {
        "files" => "",
        "browsers" => "",
        "recyclebin" => "",
        "startup" => "",
        "diskusage" => "",
        "registry" => "",
        _ => ""
    };

    public string AdminHint => RequiresAdmin
        ? "Osa kohteista vaatii järjestelmänvalvojan oikeudet."
        : "";

    public string OptInHint => SelectedByDefault
        ? ""
        : "Tämä moduuli ei kuulu \"Skannaa kaikki\" -ajoon. Kohteet valitaan yksitellen.";

    /// <summary>Matalan ja keskiriskin kategoriat.</summary>
    public ObservableCollection<CategoryViewModel> Categories { get; } = new();

    /// <summary>Korkean riskin kategoriat, näytetään erillisessä oletuksena suljetussa lohkossa.</summary>
    public ObservableCollection<CategoryViewModel> HighRiskCategories { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    public partial bool HasScanned { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    public partial string ErrorText { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    public partial int SelectedCount { get; set; }

    [ObservableProperty]
    public partial long SelectedBytes { get; set; }

    [ObservableProperty]
    public partial string SelectionText { get; set; }

    public bool HasErrors => ErrorText.Length > 0;
    public bool HasResults => HasScanned && (Categories.Count > 0 || HighRiskCategories.Count > 0);
    public bool HasHighRisk => HighRiskCategories.Count > 0;
    public bool IsEmpty => HasScanned && Categories.Count == 0 && HighRiskCategories.Count == 0;

    public ModuleViewModel(ICleanerModule module)
    {
        Module = module;
        ErrorText = "";
        StatusText = "Ei skannattu.";
        SelectionText = "";
        UpdateSelection();
    }

    /// <summary>Korvaa edellisen tuloksen. Oletusvalinta: moduuli oletuksena mukana ja riski alle High.</summary>
    public void LoadScan(ScanResult result)
    {
        foreach (var c in Categories.Concat(HighRiskCategories))
            c.SelectionChanged -= OnCategorySelectionChanged;
        Categories.Clear();
        HighRiskCategories.Clear();

        var groups = result.Items
            .GroupBy(i => i.Category)
            .OrderByDescending(g => g.Sum(i => i.SizeBytes))
            .ThenBy(g => g.Key);

        foreach (var group in groups)
        {
            var risk = group.Max(i => i.Risk);
            var selectByDefault = SelectedByDefault && risk != RiskLevel.High;
            var category = new CategoryViewModel(group.Key, risk, group.Select(i => new ItemViewModel(i, selectByDefault)), showTotal: SelectedByDefault);
            category.SelectionChanged += OnCategorySelectionChanged;
            (risk == RiskLevel.High ? HighRiskCategories : Categories).Add(category);
        }

        ErrorText = string.Join("\n", result.Errors);
        HasScanned = true;
        // Vain pyydettäessä ajettavien moduulien (levytila) kohteet voivat olla sisäkkäisiä: ei summaa.
        StatusText = result.Count == 0
            ? "Ei löydöksiä."
            : SelectedByDefault && result.TotalBytes > 0
                ? $"Löytyi {result.Count} kohdetta, {ByteFormatter.Format(result.TotalBytes)}."
                : $"Löytyi {result.Count} kohdetta.";

        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasHighRisk));
        OnPropertyChanged(nameof(IsEmpty));
        UpdateSelection();
    }

    public void ClearScan()
    {
        foreach (var c in Categories.Concat(HighRiskCategories))
            c.SelectionChanged -= OnCategorySelectionChanged;
        Categories.Clear();
        HighRiskCategories.Clear();
        HasScanned = false;
        ErrorText = "";
        StatusText = "Ei skannattu.";
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasHighRisk));
        OnPropertyChanged(nameof(IsEmpty));
        UpdateSelection();
    }

    public IReadOnlyList<CleanItem> SelectedItems() =>
        Categories.Concat(HighRiskCategories)
            .SelectMany(c => c.Items)
            .Where(i => i.IsSelected)
            .Select(i => i.Item)
            .ToList();

    private void OnCategorySelectionChanged(object? sender, EventArgs e) => UpdateSelection();

    private void UpdateSelection()
    {
        var all = Categories.Concat(HighRiskCategories).ToList();
        SelectedCount = all.Sum(c => c.SelectedCount);
        SelectedBytes = all.Sum(c => c.SelectedBytes);
        SelectionText = SelectedCount == 0
            ? "Ei valittuja kohteita."
            : SelectedBytes > 0
                ? $"Valittu {SelectedCount} kohdetta, {ByteFormatter.Format(SelectedBytes)}."
                : $"Valittu {SelectedCount} kohdetta.";
    }
}

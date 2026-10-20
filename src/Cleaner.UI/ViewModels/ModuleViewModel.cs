using System.Collections.ObjectModel;
using Cleaner.Core.Abstractions;
using Cleaner.Core.Localization;
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
        RiskLevel.High => Loc.T("High risk", "Korkea riski"),
        RiskLevel.Medium => Loc.T("Medium risk", "Keskiriski"),
        _ => Loc.T("Low risk", "Matala riski")
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
        "uninstall" => "",
        "processes" => "\uE9D9",
        _ => ""
    };

    public string AdminHint => RequiresAdmin
        ? Loc.T("Some items require administrator rights.", "Osa kohteista vaatii järjestelmänvalvojan oikeudet.")
        : "";

    public string OptInHint => SelectedByDefault
        ? ""
        : Loc.T("This module is not part of \"Scan all\". Items are selected one by one.",
                "Tämä moduuli ei kuulu \"Skannaa kaikki\" -ajoon. Kohteet valitaan yksitellen.");

    /// <summary>Matalan ja keskiriskin kategoriat.</summary>
    public ObservableCollection<CategoryViewModel> Categories { get; } = new();

    /// <summary>Korkean riskin kategoriat, näytetään erillisessä oletuksena suljetussa lohkossa.</summary>
    public ObservableCollection<CategoryViewModel> HighRiskCategories { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState), nameof(ShowTiles))]
    public partial bool HasScanned { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    public partial string ErrorText { get; set; }

    /// <summary>
    /// Tila, josta <see cref="StatusText"/> lasketaan nykyisellä kielellä. Teksti ei tallennu merkkijonona,
    /// jotta se voidaan kääntää uudelleen kielen vaihtuessa.
    /// </summary>
    public ModuleScanStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(ShowEmptyState));
            }
        }
    }

    private ModuleScanStatus _status = ModuleScanStatus.NotScanned;

    /// <summary>Viimeisimmän skannauksen kohdemäärä ja koko (0 = kokoa ei näytetä) tilaa Found varten.</summary>
    private int _foundCount;
    private long _foundBytes;

    public string StatusText => Status switch
    {
        ModuleScanStatus.NoFindings => Loc.T("Nothing found.", "Ei löydöksiä."),
        ModuleScanStatus.Found when _foundBytes > 0 => FoundWithSize(),
        ModuleScanStatus.Found => Loc.T($"Found {_foundCount} items.", $"Löytyi {_foundCount} kohdetta."),
        ModuleScanStatus.SelectProgram => Loc.T(
            "Select a program in the list and press Find leftovers or Uninstall program.",
            "Valitse ohjelma listasta ja paina Etsi jäänteet tai Poista ohjelma."),
        ModuleScanStatus.ScanCanceled => Loc.T("Scan canceled.", "Skannaus peruutettu."),
        _ => Loc.T("Not scanned.", "Ei skannattu.")
    };

    private string FoundWithSize()
    {
        var size = ByteFormatter.Format(_foundBytes);
        return Loc.T($"Found {_foundCount} items, {size}.", $"Löytyi {_foundCount} kohdetta, {size}.");
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionText), nameof(SelectedValue))]
    public partial int SelectedCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionText), nameof(SelectedValue))]
    public partial long SelectedBytes { get; set; }

    /// <summary>Lasketaan valinnasta nykyisellä kielellä.</summary>
    public string SelectionText
    {
        get
        {
            if (SelectedCount == 0) return Loc.T("No items selected.", "Ei valittuja kohteita.");
            if (SelectedBytes > 0)
            {
                var size = ByteFormatter.Format(SelectedBytes);
                return Loc.T($"Selected {SelectedCount} items, {size}.", $"Valittu {SelectedCount} kohdetta, {size}.");
            }
            return Loc.T($"Selected {SelectedCount} items.", $"Valittu {SelectedCount} kohdetta.");
        }
    }

    /// <summary>Löytyi-laatan arvo: koko, kun se on tiedossa, muuten kohdemäärä.</summary>
    public string FoundValue => _foundBytes > 0
        ? ByteFormatter.Format(_foundBytes)
        : _foundCount.ToString("N0", Loc.Culture);

    /// <summary>Valittu-laatan arvo: koko, kun se on tiedossa, muuten valittu määrä.</summary>
    public string SelectedValue => SelectedBytes > 0
        ? ByteFormatter.Format(SelectedBytes)
        : SelectedCount.ToString("N0", Loc.Culture);

    /// <summary>Löytyi- ja Valittu-laatat näkyvät skannauksen jälkeen.</summary>
    public bool ShowTiles => HasScanned;

    /// <summary>Tyhjä tila ennen ensimmäistä skannausta. Ohjelman poistolla on oma ohjelmalistansa.</summary>
    public bool ShowEmptyState => !HasScanned && Status == ModuleScanStatus.NotScanned && Id != "uninstall";

    public bool HasErrors => ErrorText.Length > 0;
    public bool HasResults => HasScanned && (Categories.Count > 0 || HighRiskCategories.Count > 0);
    public bool HasHighRisk => HighRiskCategories.Count > 0;
    public bool IsEmpty => HasScanned && Categories.Count == 0 && HighRiskCategories.Count == 0;

    public ModuleViewModel(ICleanerModule module)
    {
        Module = module;
        ErrorText = "";
        UpdateSelection();
    }

    /// <summary>Korvaa edellisen tuloksen. Oletusvalinta kohteittain: riski alle High ja (kohteen Preselected ?? moduulin SelectedByDefault).</summary>
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
            var items = group.Select(i => new ItemViewModel(i, i.Risk != RiskLevel.High && (i.Preselected ?? SelectedByDefault)));
            var category = new CategoryViewModel(group.Key, risk, items, showTotal: SelectedByDefault);
            category.SelectionChanged += OnCategorySelectionChanged;
            (risk == RiskLevel.High ? HighRiskCategories : Categories).Add(category);
        }

        ErrorText = string.Join("\n", result.Errors);
        HasScanned = true;
        // Vain pyydettäessä ajettavien moduulien (levytila) kohteet voivat olla sisäkkäisiä: ei summaa.
        _foundCount = result.Count;
        _foundBytes = SelectedByDefault ? result.TotalBytes : 0;
        Status = result.Count == 0 ? ModuleScanStatus.NoFindings : ModuleScanStatus.Found;
        // Sama tila uudella luvulla ei nosta tapahtumaa SetPropertyssä.
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(FoundValue));

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
        Status = ModuleScanStatus.NotScanned;
        OnPropertyChanged(nameof(FoundValue));
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
        OnPropertyChanged(nameof(SelectedValue));
    }

    /// <summary>
    /// Kielen vaihto: nimi, kuvaus, vihjeet ja tilatekstit luetaan uudelleen nykyisellä kielellä.
    /// Jo tuotetut tulokset (kategorioiden nimet, kohteiden kuvaukset, ErrorText) jäävät ennalleen.
    /// </summary>
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(RiskText));
        OnPropertyChanged(nameof(AdminHint));
        OnPropertyChanged(nameof(OptInHint));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(FoundValue));
        OnPropertyChanged(nameof(SelectedValue));
        foreach (var category in Categories.Concat(HighRiskCategories))
            category.RefreshLanguage();
    }
}

/// <summary>Moduulin skannaustila, josta tilarivin teksti lasketaan.</summary>
public enum ModuleScanStatus
{
    NotScanned,
    NoFindings,
    Found,
    SelectProgram,
    ScanCanceled
}

using System.ComponentModel;
using Cleaner.Core.Localization;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cleaner.UI.ViewModels;

/// <summary>
/// Yksi kategoria (esim. "Käyttäjän väliaikaistiedostot") ja sen kohteet.
/// Näytettävien rivien määrä on rajattu, jotta 14 000 tiedoston lista ei jäädytä käyttöliittymää;
/// kategorian valintaruutu koskee silti kaikkia kohteita, myös piilotettuja.
/// </summary>
public sealed partial class CategoryViewModel : ObservableObject
{
    public const int MaxVisibleItems = 500;

    public string Name { get; }
    public RiskLevel Risk { get; }
    public IReadOnlyList<ItemViewModel> Items { get; }
    public IReadOnlyList<ItemViewModel> VisibleItems { get; }
    public int HiddenCount => Items.Count - VisibleItems.Count;
    public bool HasHidden => HiddenCount > 0;
    public string HiddenText => HasHidden
        ? Loc.T($"... and {HiddenCount} more items. Selecting the category also applies to them.",
                $"... ja {HiddenCount} muuta kohdetta. Kategorian valinta koskee myös niitä.")
        : "";
    public long TotalBytes { get; }

    /// <summary>Lasketaan joka lukukerralla, jotta teksti seuraa kielen vaihtoa (<see cref="RefreshLanguage"/>).</summary>
    public string SummaryText
    {
        get
        {
            if (_showTotal && TotalBytes > 0)
            {
                var size = ByteFormatter.Format(TotalBytes);
                return Loc.T($"{Items.Count} items, {size}", $"{Items.Count} kpl, {size}");
            }
            return Loc.T($"{Items.Count} items", $"{Items.Count} kpl");
        }
    }

    public string RiskText => Risk switch
    {
        RiskLevel.High => Loc.T("high risk", "korkea riski"),
        RiskLevel.Medium => Loc.T("medium risk", "keskiriski"),
        _ => Loc.T("low risk", "matala riski")
    };

    public event EventHandler? SelectionChanged;

    private readonly bool _showTotal;
    private bool _suppress;

    /// <param name="showTotal">
    /// Näytetäänkö kategorian yhteiskoko. Epätosi levytilan analyysille, jonka kansiot ovat sisäkkäisiä
    /// ja summa olisi harhaanjohtava.
    /// </param>
    public CategoryViewModel(string name, RiskLevel risk, IEnumerable<ItemViewModel> items, bool showTotal = true)
    {
        Name = name;
        Risk = risk;
        Items = items.ToList();
        VisibleItems = Items.Take(MaxVisibleItems).ToList();
        TotalBytes = Items.Sum(i => i.SizeBytes);
        _showTotal = showTotal;

        foreach (var item in Items)
            item.PropertyChanged += OnItemChanged;
    }

    /// <summary>Kielen vaihto: kielestä riippuvat tekstit luetaan uudelleen. Kategorian nimi jää syntykieleensä.</summary>
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(RiskText));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HiddenText));
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppress || e.PropertyName != nameof(ItemViewModel.IsSelected)) return;
        OnPropertyChanged(nameof(IsChecked));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>true = kaikki valittu, false = ei mitään, null = osa.</summary>
    public bool? IsChecked
    {
        get
        {
            if (Items.Count == 0) return false;
            var selected = Items.Count(i => i.IsSelected);
            return selected == 0 ? false : selected == Items.Count ? true : null;
        }
        set
        {
            // x:Bindin TwoWay-kuuntelija kirjoittaa CheckBoxin arvon takaisin jokaisesta muutoksesta, myös
            // ohjelmallisesta. Kun yksi rivi rastitaan, getter palauttaa null (osittainen), sidonta asettaa
            // ruudun null-tilaan ja kuuntelija kirjoittaisi null-arvon tänne: ilman tätä ehtoa se nollaisi
            // kaikki rivit ja rivin rasti katoaisi heti. Sama arvo ohitetaan samasta syystä (ei kaksoiskäsittelyä).
            if (value is null || value == IsChecked) return;
            var target = value.Value;
            _suppress = true;
            foreach (var item in Items) item.IsSelected = target;
            _suppress = false;
            OnPropertyChanged(nameof(IsChecked));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public int SelectedCount => Items.Count(i => i.IsSelected);
    public long SelectedBytes => Items.Where(i => i.IsSelected).Sum(i => i.SizeBytes);
}

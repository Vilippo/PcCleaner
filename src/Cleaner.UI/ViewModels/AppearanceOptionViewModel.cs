using Cleaner.Core.Localization;
using Cleaner.UI.Theming;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Cleaner.UI.ViewModels;

/// <summary>
/// Yksi rivi asetussivun paletti- tai fonttilistassa. Paletilla on kolme näyteväriä, fontilla näyteteksti omalla fontillaan.
/// </summary>
public sealed partial class AppearanceOptionViewModel : ObservableObject
{
    private readonly LocalizedText _name;

    private AppearanceOptionViewModel(string id, LocalizedText name, bool isPalette)
    {
        Id = id;
        _name = name;
        IsPalette = isPalette;
    }

    public string Id { get; }

    /// <summary>Näyttönimi nykyisellä kielellä.</summary>
    public string Name => _name.ToString();

    public bool IsPalette { get; }

    // Paletin näytevärit: tumma kortti, vaalea kortti, tumma korostus. Fontille null.
    public SolidColorBrush? Swatch1 { get; private init; }
    public SolidColorBrush? Swatch2 { get; private init; }
    public SolidColorBrush? Swatch3 { get; private init; }

    public Visibility SwatchVisibility => IsPalette ? Visibility.Visible : Visibility.Collapsed;

    // Fontin näytetekstin fontit. Paletille null.
    public FontFamily? HeadingFontFamily { get; private init; }
    public FontFamily? BodyFontFamily { get; private init; }

    public string Sample => IsPalette
        ? ""
        : Loc.T("File cleanup · Found 1,204 items, 727 MB", "Tiedostosiivous · Löytyi 1 204 kohdetta, 727 MB");

    public Visibility SampleVisibility => IsPalette ? Visibility.Collapsed : Visibility.Visible;

    public static AppearanceOptionViewModel ForPalette(Palette p) => new(p.Id, p.Name, isPalette: true)
    {
        Swatch1 = new SolidColorBrush(p.Dark.CardBackground),
        Swatch2 = new SolidColorBrush(p.Light.CardBackground),
        Swatch3 = new SolidColorBrush(p.Dark.Accent),
    };

    public static AppearanceOptionViewModel ForFont(FontChoice f) => new(f.Id, f.Name, isPalette: false)
    {
        HeadingFontFamily = new FontFamily(f.HeadingFamily),
        BodyFontFamily = new FontFamily(f.BodyFamily),
    };

    /// <summary>Kielen vaihto: nimi ja näyteteksti luetaan uudelleen.</summary>
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Sample));
    }
}

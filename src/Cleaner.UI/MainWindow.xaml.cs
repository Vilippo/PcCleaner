using Cleaner.Core.Localization;
using Cleaner.Core.Services;
using Cleaner.UI.Services;
using Cleaner.UI.Theming;
using Cleaner.UI.ViewModels;
using Cleaner.UI.Views;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Cleaner.UI;

/// <summary>
/// Ikkunan kuori: Mica, oma otsikkopalkki, koko ja sijainti (muistetaan settings.json-tiedostossa) ja kuvake. Sisältö on <see cref="MainView"/>, jonka
/// <see cref="BuildView"/> luo ja luo uudelleen ulkoasun vaihdossa. Dialogit avataan tästä (IDialogService).
/// </summary>
public sealed partial class MainWindow : Window, IDialogService
{
    public MainViewModel ViewModel { get; }

    private readonly bool _micaAvailable;

    public MainWindow()
    {
        ViewModel = new MainViewModel(this, DispatcherQueue);
        InitializeComponent();

        Title = ViewModel.TitleText;

        // REQ-12: Mica-tausta ja oma otsikkopalkki. EDGE-4: ilman Micaa SystemBackdrop jätetään asettamatta
        // ja MainView maalaa juurensa AppContentBackgroundBrush-värillä.
        _micaAvailable = MicaController.IsSupported();
        if (_micaAvailable) SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = MinWidth;
            presenter.PreferredMinimumHeight = MinHeight;
        }
        var maximize = RestorePlacement();

        BuildView();
        Closed += (_, _) =>
        {
            SavePlacement();
            ViewModel.Dispose();
        };
        if (maximize && AppWindow.Presenter is OverlappedPresenter restored) restored.Maximize();

        // Kielen vaihto: ikkunan otsikko ja treemap nykyisellä kielellä (XAML-sidonnat päivittyvät itse).
        ViewModel.LanguageRefreshed += (_, _) =>
        {
            Title = ViewModel.TitleText;
            (Content as MainView)?.RefreshLanguage();
        };

        // REQ-6, REQ-11: ulkoasun vaihto luo näkymän uudelleen. Tapahtuma laukeaa asetussivun RadioButtons-
        // valitsimen SelectionChanged-käsittelyn sisältä (TwoWay-kirjoitus PaletteIndex/FontIndex-arvoon), joten
        // uudelleenluonti siirretään dispatcheriin: näkymää ei pureta kesken sen oman tapahtuman. Valittu sivu,
        // moduuli, tulokset, valinnat ja lokirivit ovat ViewModelissa ja säilyvät. EDGE-2: treemapin alikansiopino
        // on MainView'n tilaa ja nollautuu; TreemapRoot on ViewModelissa ja treemap piirtyy juuresta.
        ViewModel.AppearanceChanged += (_, _) => DispatcherQueue.TryEnqueue(BuildView);
    }

    // ---------- Ikkunan sijainti ja koko ----------

    private const int MinWidth = 960;
    private const int MinHeight = 640;

    /// <summary>
    /// Palauttaa edellisen istunnon sijainnin ja koon settings.json-tiedostosta, jos se osuu jollekin nykyiselle
    /// näytölle; muuten oletuskoko 1200 × 820. Palauttaa true, jos ikkuna oli maksimoitu.
    /// </summary>
    private bool RestorePlacement()
    {
        var saved = AppSettings.Load().Window;
        var areas = new List<(int X, int Y, int Width, int Height)>();
        try
        {
            // FindAll palauttaa projisoidun listan; indeksointi toimii kaikissa Windows App SDK -versioissa.
            var displays = DisplayArea.FindAll();
            for (var i = 0; i < displays.Count; i++)
            {
                var work = displays[i].WorkArea;
                areas.Add((work.X, work.Y, work.Width, work.Height));
            }
        }
        catch
        {
            // Näyttöjä ei voitu lukea: avataan oletuskokoon.
        }

        if (WindowPlacement.Fit(saved, areas, MinWidth, MinHeight) is { } fit)
        {
            // Ensin siirto, sitten koko: jos kohdenäytön skaalaus eroaa, siirto laukaisee DPI-muutoksen, joka skaalaisi
            // MoveAndResize-kutsun koon, ja koko kasvaisi joka käynnistyksellä. Erillinen Resize asettaa tallennetun koon.
            AppWindow.Move(new PointInt32(fit.X, fit.Y));
            AppWindow.Resize(new SizeInt32(fit.Width, fit.Height));
        }
        else
        {
            AppWindow.Resize(new SizeInt32(1200, 820));
            saved = null;
        }
        return saved?.Maximized == true;
    }

    /// <summary>
    /// Tallentaa sulkemisessa palautussijainnin: maksimoidulla, pienennetyllä ja Aero Snapilla napsautetulla
    /// ikkunalla se on tilaa edeltävä koko (<see cref="NativeWindowPlacement"/>). Epäonnistunut tallennus ei saa
    /// estää sulkemista.
    /// </summary>
    private void SavePlacement()
    {
        try
        {
            var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id);
            var placement = NativeWindowPlacement.Read(hwnd) ?? (
                AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height,
                AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized });
            AppSettings.Update(s => s.Window = new WindowPlacement
            {
                X = placement.X,
                Y = placement.Y,
                Width = placement.Width,
                Height = placement.Height,
                Maximized = placement.Maximized
            });
        }
        catch
        {
            // Asetustiedostoa ei voitu kirjoittaa: seuraava käynnistys avautuu edelliseen tallennettuun tai oletuskokoon.
        }
    }

    /// <summary>
    /// Luo sisältönäkymän. Vanha näkymä irrotetaan ensin ViewModelista (<see cref="MainView.Detach"/>, joka
    /// pysäyttää myös x:Bind-kuuntelijat), jotta se ei jää kuuntelemaan pitkäikäistä ViewModelia.
    /// </summary>
    private void BuildView()
    {
        if (Content is MainView old) old.Detach();

        var view = new MainView(this, ViewModel, _micaAvailable);
        Content = view;
        SetTitleBar(view.AppTitleBar);

        // Tehtäväpalkin kuvake seuraa palettia (REQ-7). Exe-resurssia ei käytetä automaattisesti pakkaamattomassa WinUI 3:ssa.
        var iconPath = IconPath();
        if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
    }

    /// <summary>Nykyisen paletin kuvake Assets-kansiosta exen vieressä; jos sitä ei ole, PcCleaner.ico.</summary>
    internal static string IconPath()
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
        var palettePath = Path.Combine(assets, Appearance.IconFileName(Appearance.CurrentPalette));
        return File.Exists(palettePath) ? palettePath : Path.Combine(assets, "PcCleaner.ico");
    }

    // ---------- IDialogService (XamlRoot = Content.XamlRoot toimii, koska Content on aina MainView) ----------

    public async Task<bool> ConfirmAsync(string title, string message, string primaryText, string? closeText = null)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText ?? Loc.T("Cancel", "Peruuta"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task<string?> PickAsync(string title, string message, IReadOnlyList<string> options, string primaryText)
    {
        var combo = new ComboBox { ItemsSource = options, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(combo);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = primaryText,
            CloseButtonText = Loc.T("Cancel", "Peruuta"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? combo.SelectedItem as string : null;
    }

    public async Task InfoAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            CloseButtonText = Loc.T("Close", "Sulje"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }
}

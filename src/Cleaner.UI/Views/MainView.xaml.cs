using Cleaner.Core.Localization;
using Cleaner.Core.Services;
using Cleaner.Modules.DiskUsage;
using Cleaner.UI.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace Cleaner.UI.Views;

/// <summary>
/// Ikkunan koko sisältö: otsikkopalkki, navigointi, moduulisivu, asetussivu, lokipaneeli ja alapalkki.
/// MainWindow luo tämän uudelleen ulkoasun vaihdossa (<see cref="MainViewModel.AppearanceChanged"/>), koska
/// TextBlock-tyylit eivät seuraa resurssisanakirjan vaihtoa. Kaikki säilyvä tila on ViewModelissa (REQ-11);
/// näkymän omaa tilaa ovat lokipaneelin auki-tila ja treemapin alikansiopino, jotka nollautuvat (EDGE-2).
/// </summary>
public sealed partial class MainView : UserControl
{
    private readonly MainWindow _window;

    public MainViewModel ViewModel { get; }

    /// <summary>Otsikkopalkin kuvake: nykyisen paletin .ico Assets-kansiosta (REQ-7).</summary>
    public BitmapImage IconSource { get; }

    public MainView(MainWindow window, MainViewModel viewModel, bool micaAvailable)
    {
        _window = window;
        ViewModel = viewModel;
        IconSource = new BitmapImage(new Uri("ms-appx:///Assets/" + Path.GetFileName(MainWindow.IconPath())));

        // EDGE-2: treemap piirtyy aina juuresta, alikansiopino on tyhjä uudessa näkymässä.
        _treemapCurrent = viewModel.TreemapRoot;

        InitializeComponent();

        // EDGE-4: Mican kanssa juuri on läpinäkyvä, jotta tausta näkyy otsikkopalkin ja paneelin läpi.
        if (micaAvailable) Root.Background = null;

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel.TreemapRoot is not null) DrawTreemap();
    }

    /// <summary>
    /// Irrottaa näkymän tilaukset pitkäikäiseen ViewModeliin ennen kuin MainWindow korvaa näkymän uudella.
    /// Ilman tätä vanha puu kuuntelisi edelleen ja piirtäisi treemappia irrotetulle Canvasille.
    /// Bindings on generoitu yksityinen kenttä, joten x:Bind-kuuntelijat pysäytetään täällä.
    /// </summary>
    public void Detach()
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Loaded -= OnLoaded;
        Nav.Loaded -= Nav_Loaded;
        Nav.SelectionChanged -= Nav_SelectionChanged;
        TreemapCanvas.Children.Clear();
        Bindings.StopTracking();

        // Kokoelmien kuuntelijat: ItemsSource ViewModelin pitkäikäiseen ObservableCollectioniin pitää vanhan puun
        // elossa ja päivittäisi irrotettuja listoja (esim. jokainen lokirivi). Nollaus irrottaa CollectionChanged-
        // tilaukset. Järjestys: StopTracking ensin, koska nollaus pudottaa RadioButtons.SelectedIndexin arvoon -1 ja
        // ProgramList.SelectedItemin nulliksi; generoitu UpdateTwoWay_* on `if (initialized)`-vartion takana, joka on
        // nyt false, joten ViewModeliin ei kirjoiteta (lisäksi PaletteIndex/FontIndex hylkäävät arvot < 0).
        // Nav.SelectionChanged on irrotettu edellä, joten MenuItemsSourcen nollaus ei muuta SelectedModulea.
        Nav.MenuItemsSource = null;
        LogList.ItemsSource = null;
        ProgramList.ItemsSource = null;
        CategoryList.ItemsSource = null;
        HighRiskList.ItemsSource = null;
        PaletteRadios.ItemsSource = null;
        FontRadios.ItemsSource = null;
    }

    /// <summary>Kielen vaihto: otsikkopalkin teksti päivittyy x:Bind-sidonnalla, treemap piirretään uudelleen.</summary>
    public void RefreshLanguage() => DrawTreemap();

    /// <summary>Lokipaneeli auki tai kiinni (REQ-18).</summary>
    public void ToggleLog() => LogSplitView.IsPaneOpen = !LogSplitView.IsPaneOpen;

    private void LogButton_Click(object sender, RoutedEventArgs e) => ToggleLog();

    private void CloseLog_Click(object sender, RoutedEventArgs e) => LogSplitView.IsPaneOpen = false;

    // x:Bind: moduulisivu näkyy, kun asetussivu ei ole valittuna (x:Bind kutsuu instanssimetodia).
    private Visibility Collapse(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.TreemapRoot))
        {
            _treemapStack.Clear();
            _treemapCurrent = ViewModel.TreemapRoot;
            DrawTreemap();
        }
    }

    // ---------- Navigointi ----------

    /// <summary>
    /// Alkuvalinta vasta Loaded-tapahtumassa: Nav.SettingsItem on null ennen kuin NavigationView on soveltanut
    /// mallinsa. Valinta palautetaan ViewModelista, joten asetussivu pysyy auki uudelleenluonnissa (REQ-11).
    /// Asetussivun valinta ei käy moduulikohteen kautta, joten IsSettingsSelected ei välähdä falseksi.
    /// </summary>
    private void Nav_Loaded(object sender, RoutedEventArgs e)
    {
        Nav.SelectedItem = ViewModel.IsSettingsSelected
            ? Nav.SettingsItem
            : ViewModel.SelectedModule ?? ViewModel.Modules.FirstOrDefault();
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ViewModel.IsSettingsSelected = true;
        }
        else if (args.SelectedItem is ModuleViewModel module)
        {
            ViewModel.IsSettingsSelected = false;
            ViewModel.SelectedModule = module;
        }
        // SelectedItem null (esim. mallin soveltamisen aikana): ei muutosta.
    }

    private async void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");
            // Pakkaamaton WinUI 3 -sovellus: valitsin pitää sitoa ikkunaan käsin.
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null) ViewModel.DiskUsagePath = folder.Path;
        }
        catch (Exception ex)
        {
            // Windowsin valitsimet eivät toimi järjestelmänvalvojana ajettavassa prosessissa.
            await _window.InfoAsync(Loc.T("Folder picker is not available", "Kansiovalitsin ei ole käytettävissä"), Loc.T(
                $"Type the path in the field manually.\n\nReason: {ex.Message}",
                $"Kirjoita polku kenttään käsin.\n\nSyy: {ex.Message}"));
        }
    }

    // ---------- Treemap ----------

    private readonly Stack<DirectoryNode> _treemapStack = new();
    private DirectoryNode? _treemapCurrent;

    private static readonly Color[] Palette =
    {
        Color.FromArgb(255, 66, 133, 244), Color.FromArgb(255, 52, 168, 83), Color.FromArgb(255, 251, 188, 4),
        Color.FromArgb(255, 234, 67, 53), Color.FromArgb(255, 155, 89, 182), Color.FromArgb(255, 26, 188, 156),
        Color.FromArgb(255, 230, 126, 34), Color.FromArgb(255, 127, 140, 141),
    };

    private void TreemapCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawTreemap();

    private void TreemapUp_Click(object sender, RoutedEventArgs e)
    {
        if (_treemapStack.Count == 0) return;
        _treemapCurrent = _treemapStack.Pop();
        DrawTreemap();
    }

    private void DrawTreemap()
    {
        TreemapCanvas.Children.Clear();
        var node = _treemapCurrent;
        TreemapUpButton.IsEnabled = _treemapStack.Count > 0;
        TreemapPathText.Text = node is null ? "" : $"{ViewModel.TreemapNodeLabel(node)}  ({ByteFormatter.Format(node.TotalBytes)}, {FilesText(node.FileCount)})";
        if (node is null) return;

        double width = TreemapCanvas.ActualWidth, height = TreemapCanvas.ActualHeight;
        if (width < 10 || height < 10) return;

        // Lapset suurin ensin, lisäksi kansion omat tiedostot yhtenä lohkona. Enintään 60 lohkoa.
        var blocks = node.Children
            .Where(c => c.TotalBytes > 0)
            .Select(c => (Node: (DirectoryNode?)c, Bytes: c.TotalBytes, Label: NameOf(c.Path)))
            .ToList();
        if (node.OwnBytes > 0) blocks.Add((null, node.OwnBytes, Loc.T("(files in this folder)", "(tiedostot tässä kansiossa)")));
        blocks.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
        if (blocks.Count > 60)
        {
            var rest = blocks.Skip(59).Sum(b => b.Bytes);
            blocks = blocks.Take(59).ToList();
            blocks.Add((null, rest, Loc.T("(other)", "(muut)")));
        }
        if (blocks.Count == 0) return;

        var rects = Treemap.Squarify(blocks.Select(b => (double)b.Bytes).ToList(), 0, 0, width, height);

        for (int i = 0; i < rects.Count; i++)
        {
            var rect = rects[i];
            var block = blocks[i];
            if (rect.Width < 1 || rect.Height < 1) continue;

            var color = Palette[i % Palette.Length];
            var border = new Border
            {
                Width = rect.Width,
                Height = rect.Height,
                Background = new SolidColorBrush(Color.FromArgb(block.Node is null ? (byte)90 : (byte)170, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(Colors.Black),
                BorderThickness = new Thickness(1),
                Tag = block.Node
            };

            if (rect.Width > 48 && rect.Height > 20)
            {
                border.Child = new TextBlock
                {
                    Text = rect.Height > 36 ? $"{block.Label}\n{ByteFormatter.Format(block.Bytes)}" : block.Label,
                    FontSize = 11,
                    Margin = new Thickness(4, 2, 4, 2),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    Foreground = new SolidColorBrush(Colors.White)
                };
            }

            var tip = block.Node is null
                ? $"{block.Label}\n{ByteFormatter.Format(block.Bytes)}"
                : $"{block.Node.Path}\n{ByteFormatter.Format(block.Node.TotalBytes)}, {FilesText(block.Node.FileCount)}" +
                  (block.Node.HadErrors ? Loc.T("\nsome subfolders could not be read", "\nosa alikansioista ei luettavissa") : "") +
                  (block.Node.Children.Count > 0 ? Loc.T("\nClick to open", "\nNapsauta avataksesi") : "");
            ToolTipService.SetToolTip(border, tip);

            if (block.Node is { Children.Count: > 0 })
                border.Tapped += TreemapBlock_Tapped;

            Canvas.SetLeft(border, rect.X);
            Canvas.SetTop(border, rect.Y);
            TreemapCanvas.Children.Add(border);
        }
    }

    private void TreemapBlock_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is not Border { Tag: DirectoryNode child } || _treemapCurrent is null) return;
        _treemapStack.Push(_treemapCurrent);
        _treemapCurrent = child;
        DrawTreemap();
    }

    private static string FilesText(int count) => Loc.T($"{count} files", $"{count} tiedostoa");

    private static string NameOf(string path)
    {
        var trimmed = path.TrimEnd('\\');
        var idx = trimmed.LastIndexOf('\\');
        return idx < 0 ? path : trimmed[(idx + 1)..];
    }
}

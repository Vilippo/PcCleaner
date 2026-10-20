using System.Collections.Specialized;
using System.ComponentModel;
using Cleaner.Core.Localization;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using Cleaner.Modules.DiskUsage;
using Cleaner.UI.Theming;
using Cleaner.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;
using Windows.UI;

namespace Cleaner.UI.Views;

/// <summary>
/// Ikkunan koko sisältö: otsikkopalkki, navigointi, moduulisivu, asetussivu, lokipaneeli ja alapalkki.
/// MainWindow luo tämän uudelleen ulkoasun vaihdossa (<see cref="MainViewModel.AppearanceChanged"/>), koska
/// TextBlock-tyylit eivät seuraa resurssisanakirjan vaihtoa. Kaikki säilyvä tila on ViewModelissa (REQ-11);
/// näkymän omaa tilaa ovat loki- ja avustajapaneelin auki-tila ja treemapin alikansiopino, jotka nollautuvat
/// (EDGE-2, EDGE-10).
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
        // REQ-20: viestilista vierittää uusimman viestin näkyviin. Uudelleenluonnissa (EDGE-10) jo olemassa olevat
        // viestit tilataan myös, jotta kesken suoratoiston kasvava vastaus vierittää uudessa näkymässä.
        ViewModel.Ai.Messages.CollectionChanged += OnAiMessagesChanged;
        foreach (var message in ViewModel.Ai.Messages) TrackAiMessage(message);
        Loaded += OnLoaded;
        // REQ-20: treemapin tausta ja lohkotekstin väri riippuvat Windowsin teemasta.
        ActualThemeChanged += OnActualThemeChanged;
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args) => DrawTreemap();

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
        // REQ-21: viestit elävät ViewModelissa, joten kaikki niiden tilaukset irrotetaan.
        ViewModel.Ai.Messages.CollectionChanged -= OnAiMessagesChanged;
        UntrackAllAiMessages();
        Loaded -= OnLoaded;
        ActualThemeChanged -= OnActualThemeChanged;
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
        AiMessageList.ItemsSource = null;
        ProgramList.ItemsSource = null;
        CategoryList.ItemsSource = null;
        HighRiskList.ItemsSource = null;
        PaletteRadios.ItemsSource = null;
        FontRadios.ItemsSource = null;
    }

    /// <summary>Kielen vaihto: otsikkopalkin teksti päivittyy x:Bind-sidonnalla, treemap ja asetuskohde päivitetään tässä.</summary>
    public void RefreshLanguage()
    {
        LocalizeSettingsItem();
        DrawTreemap();
    }

    /// <summary>Lokipaneeli auki tai kiinni (REQ-18).</summary>
    public void ToggleLog() => LogSplitView.IsPaneOpen = !LogSplitView.IsPaneOpen;

    private void LogButton_Click(object sender, RoutedEventArgs e) => ToggleLog();

    private void CloseLog_Click(object sender, RoutedEventArgs e) => LogSplitView.IsPaneOpen = false;

    // ---------- Tekoälyavustaja (REQ-20) ----------

    private readonly HashSet<AiMessageViewModel> _trackedAiMessages = new();

    /// <summary>Avustajapaneeli auki tai kiinni alapalkin napista.</summary>
    private void AiButton_Click(object sender, RoutedEventArgs e) => AiSplitView.IsPaneOpen = !AiSplitView.IsPaneOpen;

    private void CloseAi_Click(object sender, RoutedEventArgs e) => AiSplitView.IsPaneOpen = false;

    /// <summary>Enter lähettää kysymyksen; Question on ajan tasalla, koska sidonta päivittyy jokaisella näppäilyllä.</summary>
    private void AiQuestionBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        if (ViewModel.Ai.AskCommand.CanExecute(null)) ViewModel.Ai.AskCommand.Execute(null);
        e.Handled = true;
    }

    // x:Bind: InfoBar näkyy, kun avustaja ei ole käytettävissä (EDGE-1).
    private bool Not(bool v) => !v;

    /// <summary>
    /// Viestien tilaukset seuraavat kokoelmaa: lisätty viesti tilataan ja lista vieritetään heti, poistettu irrotetaan.
    /// Uusi keskustelu tyhjentää kokoelman Reset-toiminnolla, joka ei kerro poistettuja, joten silloin kaikki irrotetaan.
    /// </summary>
    private void OnAiMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            UntrackAllAiMessages();
            foreach (var message in ViewModel.Ai.Messages) TrackAiMessage(message);
            return;
        }
        if (e.OldItems is not null)
            foreach (AiMessageViewModel message in e.OldItems) UntrackAiMessage(message);
        if (e.NewItems is not null)
        {
            foreach (AiMessageViewModel message in e.NewItems) TrackAiMessage(message);
            ScrollAiToEnd();
        }
    }

    private void TrackAiMessage(AiMessageViewModel message)
    {
        if (_trackedAiMessages.Add(message)) message.PropertyChanged += OnAiMessagePropertyChanged;
    }

    private void UntrackAiMessage(AiMessageViewModel message)
    {
        if (_trackedAiMessages.Remove(message)) message.PropertyChanged -= OnAiMessagePropertyChanged;
    }

    private void UntrackAllAiMessages()
    {
        foreach (var message in _trackedAiMessages) message.PropertyChanged -= OnAiMessagePropertyChanged;
        _trackedAiMessages.Clear();
    }

    // Suoratoistossa vastauksen teksti kasvaa: uusin viesti pidetään näkyvissä.
    private void OnAiMessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AiMessageViewModel.Text)) ScrollAiToEnd();
    }

    private void ScrollAiToEnd()
    {
        var messages = ViewModel.Ai.Messages;
        if (messages.Count > 0) AiMessageList.ScrollIntoView(messages[^1]);
    }

    // x:Bind: moduulisivu näkyy, kun asetussivu ei ole valittuna (x:Bind kutsuu instanssimetodia).
    private Visibility Collapse(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    // x:Bind (REQ-15): tyhjä tila näkyy vain, kun moduulia ei ole skannattu eikä sovellus ole kiireinen.
    // Molemmat parametrit ovat sidontapolkuja, joten arvo päivittyy kumman tahansa muuttuessa.
    private Visibility EmptyStateVisibility(bool showEmptyState, bool isIdle) =>
        showEmptyState && isIdle ? Visibility.Visible : Visibility.Collapsed;

    // x:Bind (REQ-13, REQ-16): riskimerkki, kortin raita ja riskiteksti ovat kolmena kiinteävärisenä elementtinä,
    // joista yksi näkyy. Staattisina, jotta DataTemplate voi kutsua niitä polulla local:MainView.IsLow(Risk).
    public static Visibility IsLow(RiskLevel risk) => risk == RiskLevel.Low ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility IsMedium(RiskLevel risk) => risk == RiskLevel.Medium ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility IsHigh(RiskLevel risk) => risk == RiskLevel.High ? Visibility.Visible : Visibility.Collapsed;

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
        LocalizeSettingsItem();
    }

    /// <summary>
    /// REQ-21: WinUI antaa asetuskohteen tekstin ja ruudunlukijan nimen Windowsin näyttökielellä, joten ne korvataan
    /// sovelluksen kielellä. Työkaluvihje korvataan vain, jos WinUI on asettanut sen (paneeli kiinni); auki olevalla
    /// paneelilla sitä ei ole. Ennen mallin soveltamista SettingsItem on null, jolloin ei tehdä mitään.
    /// </summary>
    private void LocalizeSettingsItem()
    {
        if (Nav.SettingsItem is not NavigationViewItem settings) return;
        settings.Content = ViewModel.T.SettingsTitle;
        AutomationProperties.SetName(settings, ViewModel.T.SettingsTitle);
        if (ToolTipService.GetToolTip(settings) is not null)
            ToolTipService.SetToolTip(settings, ViewModel.T.SettingsTitle);
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

    // Lohkon alpha: kansiot 170, kansion omat tiedostot ja "(muut)" 90.
    private const byte FolderAlpha = 170;
    private const byte FilesAlpha = 90;

    private static readonly Color LabelLight = Color.FromArgb(255, 0xFF, 0xFF, 0xFF);
    private static readonly Color LabelDark = Color.FromArgb(255, 0x1A, 0x1A, 0x1A);

    /// <summary>
    /// Treemapin taustaväri piirtohetken teeman mukaan. Luetaan paletista eikä Application.Current.Resources-
    /// sanakirjasta, joka antaisi hakuhetken teeman arvon ja jäätyisi siihen.
    /// </summary>
    private Color TreemapBackground() => ActualTheme == ElementTheme.Light
        ? Appearance.CurrentPalette.Light.ContentBackground
        : Appearance.CurrentPalette.Dark.ContentBackground;

    /// <summary>Näkyvä väri, kun <paramref name="fg"/> piirretään alphalla <paramref name="alpha"/> värin <paramref name="bg"/> päälle: c = a·fg + (1 − a)·bg.</summary>
    internal static Color Composite(Color fg, byte alpha, Color bg)
    {
        var a = alpha / 255.0;
        byte Mix(byte f, byte b) => (byte)Math.Round(a * f + (1 - a) * b);
        return Color.FromArgb(255, Mix(fg.R, bg.R), Mix(fg.G, bg.G), Mix(fg.B, bg.B));
    }

    /// <summary>WCAG-suhteellinen luminanssi: sRGB-linearisoitu 0.2126 R' + 0.7152 G' + 0.0722 B'.</summary>
    internal static double Luminance(Color c)
    {
        static double Linear(byte channel)
        {
            var s = channel / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }

    /// <summary>WCAG-kontrastisuhde (L_vaaleampi + 0.05) / (L_tummempi + 0.05).</summary>
    internal static double Contrast(double l1, double l2) =>
        (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);

    /// <summary>Lohkotekstin väri: #FFFFFF tai #1A1A1A, kumpi antaa suuremman kontrastin näkyvään väriin (REQ-20).</summary>
    internal static Color LabelColorFor(Color visible)
    {
        var l = Luminance(visible);
        return Contrast(l, Luminance(LabelLight)) >= Contrast(l, Luminance(LabelDark)) ? LabelLight : LabelDark;
    }

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

        // REQ-20: paletin kategoriavärit, lohkon reuna taustan värinen, tekstin väri kontrastin mukaan.
        var palette = Appearance.CurrentPalette.Treemap;
        var background = TreemapBackground();
        var borderBrush = new SolidColorBrush(background);

        for (int i = 0; i < rects.Count; i++)
        {
            var rect = rects[i];
            var block = blocks[i];
            if (rect.Width < 1 || rect.Height < 1) continue;

            var color = palette[i % palette.Count];
            var alpha = block.Node is null ? FilesAlpha : FolderAlpha;
            var border = new Border
            {
                Width = rect.Width,
                Height = rect.Height,
                Background = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)),
                BorderBrush = borderBrush,
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
                    Foreground = new SolidColorBrush(LabelColorFor(Composite(color, alpha, background)))
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

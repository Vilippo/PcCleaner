using Cleaner.Core.Services;
using Cleaner.Modules.DiskUsage;
using Cleaner.UI.Services;
using Cleaner.UI.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace Cleaner.UI;

public sealed partial class MainWindow : Window, IDialogService
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        ViewModel = new MainViewModel(this, DispatcherQueue);
        InitializeComponent();

        Title = ViewModel.TitleText;
        AppWindow.Resize(new SizeInt32(1200, 820));

        // Otsikkopalkin ja tehtäväpalkin kuvake. Exe-resurssia ei käytetä automaattisesti pakkaamattomassa WinUI 3:ssa.
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "PcCleaner.ico");
        if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);

        Nav.SelectedItem = ViewModel.Modules.FirstOrDefault();
        Closed += (_, _) => ViewModel.Dispose();

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.TreemapRoot))
            {
                _treemapStack.Clear();
                _treemapCurrent = ViewModel.TreemapRoot;
                DrawTreemap();
            }
        };
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        ViewModel.SelectedModule = args.SelectedItem as ModuleViewModel;
    }

    private async void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");
            // Pakkaamaton WinUI 3 -sovellus: valitsin pitää sitoa ikkunaan käsin.
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null) ViewModel.DiskUsagePath = folder.Path;
        }
        catch (Exception ex)
        {
            // Windowsin valitsimet eivät toimi järjestelmänvalvojana ajettavassa prosessissa.
            await InfoAsync("Kansiovalitsin ei ole käytettävissä",
                $"Kirjoita polku kenttään käsin.\n\nSyy: {ex.Message}");
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
        TreemapPathText.Text = node is null ? "" : $"{node.Path}  ({ByteFormatter.Format(node.TotalBytes)}, {node.FileCount} tiedostoa)";
        if (node is null) return;

        double width = TreemapCanvas.ActualWidth, height = TreemapCanvas.ActualHeight;
        if (width < 10 || height < 10) return;

        // Lapset suurin ensin, lisäksi kansion omat tiedostot yhtenä lohkona. Enintään 60 lohkoa.
        var blocks = node.Children
            .Where(c => c.TotalBytes > 0)
            .Select(c => (Node: (DirectoryNode?)c, Bytes: c.TotalBytes, Label: NameOf(c.Path)))
            .ToList();
        if (node.OwnBytes > 0) blocks.Add((null, node.OwnBytes, "(tiedostot tässä kansiossa)"));
        blocks.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
        if (blocks.Count > 60)
        {
            var rest = blocks.Skip(59).Sum(b => b.Bytes);
            blocks = blocks.Take(59).ToList();
            blocks.Add((null, rest, "(muut)"));
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
                : $"{block.Node.Path}\n{ByteFormatter.Format(block.Node.TotalBytes)}, {block.Node.FileCount} tiedostoa" +
                  (block.Node.HadErrors ? "\nosa alikansioista ei luettavissa" : "") +
                  (block.Node.Children.Count > 0 ? "\nNapsauta avataksesi" : "");
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

    private static string NameOf(string path)
    {
        var trimmed = path.TrimEnd('\\');
        var idx = trimmed.LastIndexOf('\\');
        return idx < 0 ? path : trimmed[(idx + 1)..];
    }

    // ---------- IDialogService ----------

    public async Task<bool> ConfirmAsync(string title, string message, string primaryText, string closeText = "Peruuta")
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText,
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
            CloseButtonText = "Peruuta",
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
            CloseButtonText = "Sulje",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }
}

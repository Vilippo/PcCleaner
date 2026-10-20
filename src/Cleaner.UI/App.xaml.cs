using Cleaner.Core.Localization;
using Cleaner.Core.Services;
using Cleaner.UI.Theming;
using Microsoft.UI.Xaml;

namespace Cleaner.UI;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Kieli, paletti ja fontti luetaan ennen ikkunaa. Puuttuva, rikkinäinen tai tuntematon asetus → oletus,
        // tiedostoa ei kirjoiteta.
        var settings = AppSettings.Load();
        Loc.Current = settings.ResolveLanguage();
        Appearance.Apply(Appearance.ResolvePalette(settings.Palette), Appearance.ResolveFont(settings.Font));
        _window = new MainWindow();
        _window.Activate();
    }
}

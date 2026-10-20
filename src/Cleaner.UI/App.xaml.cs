using Cleaner.Core.Localization;
using Cleaner.Core.Services;
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
        // Kieli luetaan ennen ikkunaa. Puuttuva, rikkinäinen tai tuntematon asetus → englanti, tiedostoa ei kirjoiteta.
        Loc.Current = AppSettings.Load().ResolveLanguage();
        _window = new MainWindow();
        _window.Activate();
    }
}

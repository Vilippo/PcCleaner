using Cleaner.Core.Services;
using Xunit;

namespace Cleaner.Tests;

/// <summary>
/// Testaa pääikkunan sijainnin tallennusta settings.json-tiedostoon ja sovitusta nykyisiin näyttöihin.
/// Asetustiedostot kirjoitetaan väliaikaiseen kansioon %TEMP%\pccleaner-tests\&lt;guid&gt;.
/// </summary>
public sealed class WindowPlacementTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pccleaner-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static readonly (int X, int Y, int Width, int Height)[] OneScreen = { (0, 0, 1920, 1040) };

    [Fact]
    public void Placement_round_trips_and_is_kept_by_other_updates()
    {
        var path = Path.Combine(_root, "settings.json");

        AppSettings.Update(s => s.Window = new WindowPlacement { X = 100, Y = 50, Width = 1300, Height = 900, Maximized = true }, path);
        AppSettings.Update(s => s.Palette = "indigo", path);
        var loaded = AppSettings.Load(path).Window;

        Assert.NotNull(loaded);
        Assert.Equal((100, 50, 1300, 900, true), (loaded.X, loaded.Y, loaded.Width, loaded.Height, loaded.Maximized));
        Assert.Contains("\"window\":", File.ReadAllText(path));
    }

    [Fact]
    public void Old_file_without_placement_gives_null()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, "{ \"language\": \"fi\" }");

        Assert.Null(AppSettings.Load(path).Window);
    }

    [Fact]
    public void Visible_placement_is_restored_as_saved()
    {
        var saved = new WindowPlacement { X = 200, Y = 100, Width = 1300, Height = 900 };
        Assert.Equal((200, 100, 1300, 900), WindowPlacement.Fit(saved, OneScreen, 960, 640));
    }

    [Fact]
    public void Placement_on_a_second_screen_is_restored()
    {
        var screens = new[] { (0, 0, 1920, 1040), (1920, 0, 2560, 1400) };
        var saved = new WindowPlacement { X = 2100, Y = 80, Width = 1400, Height = 1000 };
        Assert.Equal((2100, 80, 1400, 1000), WindowPlacement.Fit(saved, screens, 960, 640));
    }

    [Fact]
    public void Placement_on_a_disconnected_screen_gives_null()
    {
        var saved = new WindowPlacement { X = 2100, Y = 80, Width = 1400, Height = 1000 };
        Assert.Null(WindowPlacement.Fit(saved, OneScreen, 960, 640));
    }

    [Fact]
    public void Title_bar_above_the_screen_gives_null()
    {
        var saved = new WindowPlacement { X = 100, Y = -500, Width = 1200, Height = 820 };
        Assert.Null(WindowPlacement.Fit(saved, OneScreen, 960, 640));
    }

    [Fact]
    public void Size_is_clamped_to_the_screen_and_the_minimum()
    {
        var tooBig = new WindowPlacement { X = 0, Y = 0, Width = 4000, Height = 3000 };
        Assert.Equal((0, 0, 1920, 1040), WindowPlacement.Fit(tooBig, OneScreen, 960, 640));

        var tooSmall = new WindowPlacement { X = 10, Y = 10, Width = 300, Height = 200 };
        Assert.Equal((10, 10, 960, 640), WindowPlacement.Fit(tooSmall, OneScreen, 960, 640));
    }

    [Fact]
    public void Missing_or_invalid_placement_gives_null()
    {
        Assert.Null(WindowPlacement.Fit(null, OneScreen, 960, 640));
        Assert.Null(WindowPlacement.Fit(new WindowPlacement { X = 0, Y = 0, Width = 0, Height = 800 }, OneScreen, 960, 640));
        Assert.Null(WindowPlacement.Fit(new WindowPlacement { X = 0, Y = 0, Width = 1200, Height = 800 },
            Array.Empty<(int, int, int, int)>(), 960, 640));
    }
}

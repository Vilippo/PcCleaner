namespace Cleaner.Core.Services;

/// <summary>
/// Pääikkunan sijainti ja koko edellisestä istunnosta fyysisinä pikseleinä (AppWindow-koordinaatit).
/// Tallennetaan <c>settings.json</c>-kenttään <c>window</c>. Maksimoidulle ikkunalle tallennetaan sen
/// palautuskoko ja <see cref="Maximized"/> = true.
/// </summary>
public sealed class WindowPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }

    /// <summary>Otsikkopalkin korkeus, jonka täytyy osua näytölle, jotta ikkunan voi tarttua ja siirtää.</summary>
    private const int TitleBarHeight = 40;

    /// <summary>Kuinka paljon otsikkopalkista täytyy näkyä vaakasuunnassa ja pystysuunnassa.</summary>
    private const int MinVisibleWidth = 100;
    private const int MinVisibleHeight = 20;

    /// <summary>
    /// Sovittaa tallennetun sijainnin nykyisiin näyttöihin. Palauttaa null, jos tallennettua sijaintia ei ole, se on
    /// kelvoton tai sen otsikkopalkki ei osu millekään näytön työalueelle (esim. näyttö on irrotettu); silloin
    /// ikkuna avataan oletuskokoon. Koko rajataan vähintään minimikokoon ja enintään osuvan näytön työalueeseen.
    /// </summary>
    public static (int X, int Y, int Width, int Height)? Fit(WindowPlacement? saved,
        IReadOnlyList<(int X, int Y, int Width, int Height)> workAreas, int minWidth, int minHeight)
    {
        if (saved is null || saved.Width <= 0 || saved.Height <= 0) return null;

        foreach (var area in workAreas)
        {
            var overlapWidth = Math.Min(saved.X + saved.Width, area.X + area.Width) - Math.Max(saved.X, area.X);
            var overlapHeight = Math.Min(saved.Y + TitleBarHeight, area.Y + area.Height) - Math.Max(saved.Y, area.Y);
            if (overlapWidth < Math.Min(MinVisibleWidth, saved.Width) || overlapHeight < MinVisibleHeight) continue;

            var width = Math.Max(minWidth, Math.Min(saved.Width, area.Width));
            var height = Math.Max(minHeight, Math.Min(saved.Height, area.Height));
            return (saved.X, saved.Y, width, height);
        }
        return null;
    }
}

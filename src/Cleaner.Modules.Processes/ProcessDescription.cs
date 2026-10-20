namespace Cleaner.Modules.Processes;

/// <summary>Kategorioiden nimet ja kohteen kuvaus suomeksi.</summary>
public static class ProcessDescription
{
    private const string Separator = " · ";

    public static string CategoryName(ProcessCategory category) => category switch
    {
        ProcessCategory.MissingExe => "Ohjelmatiedosto puuttuu levyltä",
        ProcessCategory.Orphan => "Orvot apuprosessit",
        ProcessCategory.Known => "Tunnetut taustaprosessit",
        ProcessCategory.Windowed => "Avoimet ohjelmat",
        ProcessCategory.IdleBackground => "Taustaprosessit, ei käyttöä",
        ProcessCategory.ActiveBackground => "Taustaprosessit, aktiivisia",
        _ => category.ToString()
    };

    /// <summary>Osat järjestyksessä syy, suorittimen käyttö, ikä, aliprosessit, käynnistäjä; tyhjät pois.</summary>
    public static string Build(ClassifiedProcess item, ProcessSnapshot snapshot)
    {
        var parts = new List<string>();
        var root = item.Root;

        // (a) syy
        switch (item.Category)
        {
            case ProcessCategory.MissingExe:
                parts.Add("Ohjelmatiedostoa ei löydy");
                break;
            case ProcessCategory.Orphan:
                parts.Add($"Käynnistäjä (PID {root.ParentPid}) on päättynyt");
                break;
            case ProcessCategory.Known when item.Rule is not null:
                parts.Add(string.IsNullOrWhiteSpace(item.Rule.Description)
                    ? item.Rule.Name
                    : $"{item.Rule.Name}: {item.Rule.Description}");
                break;
            case ProcessCategory.Windowed:
                parts.Add("Näkyvä ikkuna");
                if (root.ExeMissing) parts.Add("ohjelmatiedostoa ei löydy");
                if (item.Rule is not null) parts.Add(item.Rule.Name);
                break;
        }

        // (b) suorittimen käyttö, vain kun mitattiin
        if (snapshot.SampleInterval > TimeSpan.Zero)
        {
            parts.Add(item.IsIdle
                ? $"ei suorittimen käyttöä {(int)Math.Ceiling(snapshot.SampleInterval.TotalSeconds)} s aikana"
                : "käyttää suoritinta");
        }

        // (c) ikä
        if (root.StartFileTime > 0)
        {
            var age = snapshot.TakenAt.UtcDateTime - DateTime.FromFileTimeUtc(root.StartFileTime);
            if (age < TimeSpan.Zero) age = TimeSpan.Zero;
            parts.Add($"käynnistetty {FormatAge(age)}");
        }

        // (d) aliprosessit
        if (item.Children.Count > 0)
            parts.Add($"{item.Children.Count} aliprosessia");

        // (e) käynnistäjä
        if (item.Parent is not null)
            parts.Add($"käynnistäjä {item.Parent.Name} (PID {item.Parent.Pid})");

        return string.Join(Separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} min";
        if (age < TimeSpan.FromDays(1)) return $"{(int)age.TotalHours} h {age.Minutes} min";
        return $"{(int)age.TotalDays} d {age.Hours} h";
    }
}

using Cleaner.Core.Localization;

namespace Cleaner.Modules.Processes;

/// <summary>Kategorioiden nimet ja kohteen kuvaus suomeksi.</summary>
public static class ProcessDescription
{
    private const string Separator = " · ";

    public static string CategoryName(ProcessCategory category) => category switch
    {
        ProcessCategory.MissingExe => Loc.T("Program file missing from disk", "Ohjelmatiedosto puuttuu levyltä"),
        ProcessCategory.Orphan => Loc.T("Orphaned helper processes", "Orvot apuprosessit"),
        ProcessCategory.Known => Loc.T("Known background processes", "Tunnetut taustaprosessit"),
        ProcessCategory.Windowed => Loc.T("Open programs", "Avoimet ohjelmat"),
        ProcessCategory.IdleBackground => Loc.T("Background processes, idle", "Taustaprosessit, ei käyttöä"),
        ProcessCategory.ActiveBackground => Loc.T("Background processes, active", "Taustaprosessit, aktiivisia"),
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
                parts.Add(Loc.T("Program file not found", "Ohjelmatiedostoa ei löydy"));
                break;
            case ProcessCategory.Orphan:
                parts.Add(Loc.T($"Parent (PID {root.ParentPid}) has exited", $"Käynnistäjä (PID {root.ParentPid}) on päättynyt"));
                break;
            case ProcessCategory.Known when item.Rule is not null:
                var ruleDescription = item.Rule.DisplayDescription;
                parts.Add(string.IsNullOrWhiteSpace(ruleDescription)
                    ? item.Rule.Name
                    : $"{item.Rule.Name}: {ruleDescription}");
                break;
            case ProcessCategory.Windowed:
                parts.Add(Loc.T("Visible window", "Näkyvä ikkuna"));
                if (root.ExeMissing) parts.Add(Loc.T("program file not found", "ohjelmatiedostoa ei löydy"));
                if (item.Rule is not null) parts.Add(item.Rule.Name);
                break;
        }

        // (b) suorittimen käyttö, vain kun mitattiin
        if (snapshot.SampleInterval > TimeSpan.Zero)
        {
            var seconds = (int)Math.Ceiling(snapshot.SampleInterval.TotalSeconds);
            parts.Add(item.IsIdle
                ? Loc.T(string.Create(Loc.Culture, $"no CPU usage in {seconds} s"), string.Create(Loc.Culture, $"ei suorittimen käyttöä {seconds} s aikana"))
                : Loc.T("using CPU", "käyttää suoritinta"));
        }

        // (c) ikä
        if (root.StartFileTime > 0)
        {
            var age = snapshot.TakenAt.UtcDateTime - DateTime.FromFileTimeUtc(root.StartFileTime);
            if (age < TimeSpan.Zero) age = TimeSpan.Zero;
            parts.Add(Loc.T($"started {FormatAge(age)} ago", $"käynnistetty {FormatAge(age)}"));
        }

        // (d) aliprosessit
        if (item.Children.Count > 0)
            parts.Add(Loc.T(item.Children.Count == 1 ? "1 child process" : $"{item.Children.Count} child processes",$"{item.Children.Count} aliprosessia"));

        // (e) käynnistäjä
        if (item.Parent is not null)
            parts.Add(Loc.T($"parent {item.Parent.Name} (PID {item.Parent.Pid})", $"käynnistäjä {item.Parent.Name} (PID {item.Parent.Pid})"));

        return string.Join(Separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static string FormatAge(TimeSpan age)
    {
        // Yksiköt min, h ja d ovat samat molemmilla kielillä.
        var c = Loc.Culture;
        if (age < TimeSpan.FromHours(1)) return string.Create(c, $"{(int)age.TotalMinutes} {Loc.T("min", "min")}");
        if (age < TimeSpan.FromDays(1)) return string.Create(c, $"{(int)age.TotalHours} {Loc.T("h", "h")} {age.Minutes} {Loc.T("min", "min")}");
        return string.Create(c, $"{(int)age.TotalDays} {Loc.T("d", "d")} {age.Hours} {Loc.T("h", "h")}");
    }
}

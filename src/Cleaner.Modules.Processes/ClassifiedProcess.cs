using Cleaner.Core.Models;

namespace Cleaner.Modules.Processes;

/// <summary>Luokiteltu prosessiryhmä: juuri ja siihen sulautetut saman exen lapset.</summary>
/// <param name="Root">Ryhmän juuri; vain juuri tuottaa CleanItem-kohteen.</param>
/// <param name="Children">Juureen sulautetut lapset PID-järjestyksessä.</param>
/// <param name="Category">Kategoria (ensimmäinen osuva sääntö).</param>
/// <param name="Risk">Kohteen riski.</param>
/// <param name="Preselected">Rastitaanko kohde oletuksena.</param>
/// <param name="Rule">Sääntöosuma juuren nimelle, myös kun kategoria on muu kuin Known (kuvausta varten).</param>
/// <param name="Parent">Elossa oleva oikea käynnistäjä (myös suojattu) tai null.</param>
public sealed record ClassifiedProcess(
    ProcessInfo Root,
    IReadOnlyList<ProcessInfo> Children,
    ProcessCategory Category,
    RiskLevel Risk,
    bool Preselected,
    ProcessRule? Rule,
    ProcessInfo? Parent)
{
    /// <summary>Juuren ja lasten yksityinen muisti yhteensä.</summary>
    public long TotalPrivateBytes => Root.PrivateBytes + Children.Sum(c => c.PrivateBytes);

    /// <summary>Jollakin ryhmän jäsenellä on näkyvä ikkuna.</summary>
    public bool HasVisibleWindow => Root.HasVisibleWindow || Children.Any(c => c.HasVisibleWindow);

    /// <summary>Kaikki ryhmän jäsenet ovat joutilaita.</summary>
    public bool IsIdle => Root.IsIdle && Children.All(c => c.IsIdle);
}

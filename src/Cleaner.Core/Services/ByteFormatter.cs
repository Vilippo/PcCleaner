using Cleaner.Core.Localization;

namespace Cleaner.Core.Services;

public static class ByteFormatter
{
    private static readonly string[] UnitsEn = { "B", "KB", "MB", "GB", "TB" };
    private static readonly string[] UnitsFi = { "t", "kt", "Mt", "Gt", "Tt" };

    /// <summary>
    /// Muotoilee tavumäärän luettavaksi nykyisellä kielellä ja sen kulttuurilla (<see cref="Loc.Culture"/>),
    /// esim. 1 536 000 → "1.46 MB" (englanti) tai "1,46 Mt" (suomi). Ei riipu käyttöjärjestelmän kulttuurista.
    /// </summary>
    public static string Format(long bytes)
    {
        var units = Loc.Current == Language.Fi ? UnitsFi : UnitsEn;
        if (bytes <= 0) return $"0 {units[0]}";
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var culture = Loc.Culture;
        return unit == 0
            ? $"{bytes.ToString(culture)} {units[0]}"
            : $"{value.ToString("0.##", culture)} {units[unit]}";
    }
}

namespace Cleaner.Modules.Startup;

/// <summary>
/// Explorerin StartupApproved-avainten arvomuoto. Tehtävienhallinta kirjoittaa jokaiselle
/// käynnistyskohteelle 12 tavun binääriarvon: 4 tavua tila + 8 tavua FILETIME (käytöstäpoiston hetki).
/// Tila 0x02 = käytössä, 0x03 = pois käytöstä. Myös arvoja 0x06/0x07 esiintyy; alin bitti ratkaisee.
/// </summary>
public static class StartupApproved
{
    public const byte EnabledState = 0x02;
    public const byte DisabledState = 0x03;
    public const int ValueLength = 12;

    /// <summary>Onko kohde poistettu käytöstä. Puuttuva arvo tarkoittaa, että kohde on käytössä.</summary>
    public static bool IsDisabled(byte[]? value) =>
        value is { Length: >= 1 } && (value[0] & 0x01) == 0x01;

    /// <summary>Rakentaa arvon, joka merkitsee kohteen pois käytöstä annetulla hetkellä.</summary>
    public static byte[] BuildDisabledValue(DateTime utcNow)
    {
        var bytes = new byte[ValueLength];
        bytes[0] = DisabledState;
        BitConverter.TryWriteBytes(bytes.AsSpan(4), utcNow.ToFileTimeUtc());
        return bytes;
    }

    /// <summary>Rakentaa arvon, joka merkitsee kohteen käyttöön (palautus).</summary>
    public static byte[] BuildEnabledValue()
    {
        var bytes = new byte[ValueLength];
        bytes[0] = EnabledState;
        return bytes;
    }
}

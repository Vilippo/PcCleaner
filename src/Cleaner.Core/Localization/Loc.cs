using System.Globalization;

namespace Cleaner.Core.Localization;

/// <summary>Käyttöliittymän kieli.</summary>
public enum Language { En, Fi }

/// <summary>
/// Sovelluksen nykyinen kieli ja kielivalinta merkkijonoille. Käännökset kirjoitetaan
/// koodiin pareina <c>Loc.T("englanti", "suomi")</c>, ei resurssitiedostoihin.
/// </summary>
public static class Loc
{
    // int-kenttä, jotta vaihto voidaan tehdä atomisesti Interlocked.Exchange-kutsulla.
    private static int _current = (int)Language.En;

    /// <summary>
    /// Nykyinen kieli, oletus <see cref="Language.En"/>. Setteri nostaa <see cref="Changed"/>-tapahtuman
    /// vain, kun arvo todella muuttuu.
    /// </summary>
    public static Language Current
    {
        get => (Language)Volatile.Read(ref _current);
        set
        {
            var previous = Interlocked.Exchange(ref _current, (int)value);
            if (previous != (int)value)
                Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>Laukeaa kerran jokaisesta todellisesta kielen muutoksesta.</summary>
    public static event EventHandler? Changed;

    /// <summary>Nykyisen kielen kulttuuri lukujen muotoiluun: en-US tai fi-FI.</summary>
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Current == Language.Fi ? "fi-FI" : "en-US");

    /// <summary>Palauttaa <paramref name="en"/> tai <paramref name="fi"/> nykyisen kielen mukaan.</summary>
    public static string T(string en, string fi) => Current == Language.Fi ? fi : en;

    /// <summary>Kielen koodi asetustiedostoon: "en" tai "fi".</summary>
    public static string ToCode(Language language) => language == Language.Fi ? "fi" : "en";

    /// <summary>
    /// Jäsentää kielikoodin "en" tai "fi" kirjainkoosta ja ympäröivistä välilyönneistä riippumatta.
    /// Null ja muut arvot palauttavat false ja kielen <see cref="Language.En"/>.
    /// </summary>
    public static bool TryParse(string? code, out Language language)
    {
        switch (code?.Trim().ToLowerInvariant())
        {
            case "en":
                language = Language.En;
                return true;
            case "fi":
                language = Language.Fi;
                return true;
            default:
                language = Language.En;
                return false;
        }
    }
}

namespace Cleaner.Modules.Processes;

/// <summary>Prosessiryhmän kategoria. Enum-järjestys on myös listausjärjestys.</summary>
public enum ProcessCategory
{
    /// <summary>Ohjelmatiedosto puuttuu levyltä.</summary>
    MissingExe,

    /// <summary>Käynnistäjä on päättynyt tai sen PID on kierrätetty.</summary>
    Orphan,

    /// <summary>Sääntötiedoston tunnistama taustaprosessi.</summary>
    Known,

    /// <summary>Ryhmällä on näkyvä ikkuna.</summary>
    Windowed,

    /// <summary>Ei suorittimen käyttöä mittausjakson aikana.</summary>
    IdleBackground,

    /// <summary>Käyttää suoritinta.</summary>
    ActiveBackground
}

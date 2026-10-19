using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Cleaner.Modules.Startup;

public enum StartupSourceKind
{
    /// <summary>Rekisterin Run-avain: arvon nimi = kohde, arvo = komento.</summary>
    RegistryRun,

    /// <summary>Käynnistyskansio: jokainen tiedosto (yleensä .lnk) on kohde.</summary>
    Folder
}

/// <summary>
/// Yksi käynnistyskohteiden lähde ja sitä vastaava StartupApproved-avain, johon
/// käytöstäpoisto kirjataan. Avain on aina samassa hivessä kuin lähde.
/// </summary>
/// <param name="Key">Vakaa tunniste, osa CleanItem.Id:tä (esim. "hkcu-run").</param>
/// <param name="DisplayName">Käyttäjälle näytettävä kategoria.</param>
/// <param name="Location">Run-avaimen polku hiven alla tai käynnistyskansion täysi polku.</param>
/// <param name="ApprovedSubKey">StartupApproved-aliavaimen polku hiven alla.</param>
/// <param name="RequiresAdmin">Vaatiiko käytöstäpoisto järjestelmänvalvojan oikeudet (HKLM).</param>
[SupportedOSPlatform("windows")]
public sealed record StartupSource(
    string Key,
    string DisplayName,
    StartupSourceKind Kind,
    RegistryHive Hive,
    string Location,
    string ApprovedSubKey,
    bool RequiresAdmin)
{
    public const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string Run32SubKey = @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run";
    public const string ApprovedRunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    public const string ApprovedRun32SubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    public const string ApprovedFolderSubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    /// <summary>Windowsin vakiolähteet samassa järjestyksessä kuin Tehtävienhallinta ne näyttää.</summary>
    public static IReadOnlyList<StartupSource> Defaults() => new[]
    {
        new StartupSource("hkcu-run", "Rekisteri, käyttäjä (HKCU Run)",
            StartupSourceKind.RegistryRun, RegistryHive.CurrentUser, RunSubKey, ApprovedRunSubKey, RequiresAdmin: false),
        new StartupSource("hklm-run", "Rekisteri, kaikki käyttäjät (HKLM Run)",
            StartupSourceKind.RegistryRun, RegistryHive.LocalMachine, RunSubKey, ApprovedRunSubKey, RequiresAdmin: true),
        new StartupSource("hklm-run32", "Rekisteri, 32-bittiset (HKLM Wow6432Node Run)",
            StartupSourceKind.RegistryRun, RegistryHive.LocalMachine, Run32SubKey, ApprovedRun32SubKey, RequiresAdmin: true),
        new StartupSource("folder-user", "Käynnistyskansio, käyttäjä",
            StartupSourceKind.Folder, RegistryHive.CurrentUser,
            Environment.GetFolderPath(Environment.SpecialFolder.Startup), ApprovedFolderSubKey, RequiresAdmin: false),
        new StartupSource("folder-common", "Käynnistyskansio, kaikki käyttäjät",
            StartupSourceKind.Folder, RegistryHive.LocalMachine,
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), ApprovedFolderSubKey, RequiresAdmin: true),
    };

    public string HiveLabel => Hive switch
    {
        RegistryHive.CurrentUser => "HKCU",
        RegistryHive.LocalMachine => "HKLM",
        _ => Hive.ToString()
    };
}

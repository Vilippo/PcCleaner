using System.Runtime.Versioning;
using Cleaner.Core.Services;

namespace Cleaner.Modules.RegistryCleaner;

/// <summary>
/// Skannattavat avaimet ja varmuuskopiokansio. Testit osoittavat nämä omaan testiavaimeensa.
/// Nimi ei ole RegistryOptions, koska Microsoft.Win32.RegistryOptions on jo olemassa.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed record RegistryScanOptions
{
    public const string UninstallHklm = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    public const string UninstallHklm32 = @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
    public const string UninstallHkcu = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall";
    public const string ClassesHkcu = @"HKCU\Software\Classes";
    public const string ClassesHklm = @"HKLM\SOFTWARE\Classes";
    public const string MuiCacheDefault = @"HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";

    private const string Explorer = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer";
    public static readonly string[] MruDefaults =
    {
        Explorer + @"\RecentDocs",
        Explorer + @"\RunMRU",
        Explorer + @"\ComDlg32\OpenSavePidlMRU",
        Explorer + @"\ComDlg32\LastVisitedPidlMRU",
        Explorer + @"\TypedPaths",
    };

    /// <summary>Uninstall-avaimet, joiden aliavaimet tarkistetaan.</summary>
    public IReadOnlyList<string> UninstallKeys { get; init; } = new[] { UninstallHklm, UninstallHklm32, UninstallHkcu };

    /// <summary>Classes-juuret (.ext- ja ProgId-avaimet). ProgId-viittaus tarkistetaan kaikista juurista.</summary>
    public IReadOnlyList<string> ClassesKeys { get; init; } = new[] { ClassesHkcu, ClassesHklm };

    public string MuiCacheKey { get; init; } = MuiCacheDefault;

    /// <summary>MRU-avaimet, joiden sisältö tyhjennetään; avain itse säilyy.</summary>
    public IReadOnlyList<string> MruKeys { get; init; } = MruDefaults;

    public string BackupRoot { get; init; } = RegistryBackup.DefaultRoot;

    public bool IncludeUninstall { get; init; } = true;
    public bool IncludeAssociations { get; init; } = true;
    public bool IncludeMuiCache { get; init; } = true;
    public bool IncludeMru { get; init; } = true;

    /// <summary>Kaikki juuret, joiden alle poistot saavat osua (RegistryGuard).</summary>
    public IEnumerable<string> AllowedRoots =>
        UninstallKeys.Concat(ClassesKeys).Append(MuiCacheKey).Concat(MruKeys);
}

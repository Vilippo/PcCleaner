using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Cleaner.Modules.RegistryCleaner;

/// <summary>
/// Rekisteripolkujen muoto tässä moduulissa: "HKCU\Software\..." tai "HKLM\SOFTWARE\...".
/// Polku kertoo aina konkreettisen hiven; HKCR-yhdistelmänäkymää ei käytetä, jotta varmuuskopio
/// ja palautus osuvat oikeaan paikkaan.
/// </summary>
[SupportedOSPlatform("windows")]
public static class RegistryPaths
{
    public static bool TryParse(string path, out RegistryHive hive, out string subKey)
    {
        hive = default;
        subKey = "";
        if (string.IsNullOrWhiteSpace(path)) return false;

        var trimmed = path.Trim().TrimEnd('\\');
        var idx = trimmed.IndexOf('\\');
        var prefix = idx < 0 ? trimmed : trimmed[..idx];
        subKey = idx < 0 ? "" : trimmed[(idx + 1)..];

        switch (prefix.ToUpperInvariant())
        {
            case "HKCU":
            case "HKEY_CURRENT_USER":
                hive = RegistryHive.CurrentUser; return true;
            case "HKLM":
            case "HKEY_LOCAL_MACHINE":
                hive = RegistryHive.LocalMachine; return true;
            case "HKCR":
            case "HKEY_CLASSES_ROOT":
                hive = RegistryHive.ClassesRoot; return true;
            case "HKU":
            case "HKEY_USERS":
                hive = RegistryHive.Users; return true;
            default:
                return false;
        }
    }

    public static string Label(RegistryHive hive) => hive switch
    {
        RegistryHive.CurrentUser => "HKCU",
        RegistryHive.LocalMachine => "HKLM",
        RegistryHive.ClassesRoot => "HKCR",
        RegistryHive.Users => "HKU",
        _ => hive.ToString()
    };

    /// <summary>Pitkä nimi reg.exe-komentoa varten (HKEY_CURRENT_USER jne.).</summary>
    public static string LongName(RegistryHive hive) => hive switch
    {
        RegistryHive.CurrentUser => "HKEY_CURRENT_USER",
        RegistryHive.LocalMachine => "HKEY_LOCAL_MACHINE",
        RegistryHive.ClassesRoot => "HKEY_CLASSES_ROOT",
        RegistryHive.Users => "HKEY_USERS",
        _ => hive.ToString()
    };

    public static string Combine(RegistryHive hive, string subKey) =>
        string.IsNullOrEmpty(subKey) ? Label(hive) : $"{Label(hive)}\\{subKey}";

    public static string Combine(string keyPath, string child) => $"{keyPath.TrimEnd('\\')}\\{child}";

    /// <summary>"HKCU\Software\X" → "HKEY_CURRENT_USER\Software\X". Virheellinen polku → null.</summary>
    public static string? ToLongForm(string path) =>
        TryParse(path, out var hive, out var sub) ? (sub.Length == 0 ? LongName(hive) : $"{LongName(hive)}\\{sub}") : null;

    /// <summary>Avaa avaimen vain lukua varten. Palauttaa null, jos avainta ei ole tai polku on virheellinen.</summary>
    public static RegistryKey? OpenReadOnly(string path)
    {
        if (!TryParse(path, out var hive, out var sub)) return null;
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        return sub.Length == 0 ? RegistryKey.OpenBaseKey(hive, RegistryView.Default) : baseKey.OpenSubKey(sub, writable: false);
    }

    public static RegistryKey? OpenWritable(string path)
    {
        if (!TryParse(path, out var hive, out var sub) || sub.Length == 0) return null;
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        return baseKey.OpenSubKey(sub, writable: true);
    }

    public static bool KeyExists(string path)
    {
        using var key = OpenReadOnly(path);
        return key is not null;
    }
}

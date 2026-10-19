using System.Runtime.Versioning;
using Cleaner.Core.Services;
using Microsoft.Win32;

namespace Cleaner.Modules.Uninstall;

/// <summary>Lukee asennetut ohjelmat Uninstall-rekisterijuurista (REQ-1).</summary>
[SupportedOSPlatform("windows")]
public static class InstalledProgramReader
{
    public const string UninstallHklm = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    public const string UninstallHklm32 = @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
    public const string UninstallHkcu = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly string[] DefaultRoots = { UninstallHklm, UninstallHklm32, UninstallHkcu };

    /// <summary>
    /// Ohjelmat nimen mukaan järjestettynä. Pois jäävät SystemComponent = 1, tyhjä DisplayName
    /// ja ParentKeyName-arvolliset (päivitykset). Juurta, jota ei voi avata, ei lueta.
    /// </summary>
    public static IReadOnlyList<InstalledProgram> List(IEnumerable<string>? uninstallRoots = null)
    {
        var result = new List<InstalledProgram>();
        foreach (var root in uninstallRoots ?? DefaultRoots)
        {
            using var rootKey = RegistryPaths.OpenReadOnly(root);
            if (rootKey is null) continue;

            string[] names;
            try { names = rootKey.GetSubKeyNames(); }
            catch (Exception) { continue; }
            Array.Sort(names, StringComparer.OrdinalIgnoreCase);

            foreach (var name in names)
            {
                try
                {
                    using var key = rootKey.OpenSubKey(name, writable: false);
                    if (key is null) continue;
                    var program = Read(key, RegistryPaths.Combine(root, name), name);
                    if (program is not null) result.Add(program);
                }
                catch (Exception)
                {
                    // Avain, jota ei voi lukea (oikeudet), ohitetaan.
                }
            }
        }
        return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static InstalledProgram? Read(RegistryKey key, string keyPath, string keyName)
    {
        var displayName = GetString(key, "DisplayName");
        if (string.IsNullOrWhiteSpace(displayName)) return null;
        if (key.GetValue("SystemComponent") is int sys && sys == 1) return null;
        if (key.GetValue("ParentKeyName") is not null) return null;

        var uninstall = GetString(key, "UninstallString");
        var sizeBytes = key.GetValue("EstimatedSize") is int kb ? kb * 1024L : 0L;
        var isMsi = (key.GetValue("WindowsInstaller") is int wi && wi == 1)
            || UninstallCommandLine.FileNameLower(UninstallCommandLine.ExtractExecutable(uninstall) ?? "") == "msiexec.exe";

        return new InstalledProgram(
            KeyPath: keyPath,
            KeyName: keyName,
            Name: displayName.Trim(),
            Version: GetString(key, "DisplayVersion"),
            Publisher: GetString(key, "Publisher"),
            InstallLocation: CleanLocation(GetString(key, "InstallLocation")),
            UninstallString: uninstall,
            QuietUninstallString: GetString(key, "QuietUninstallString"),
            DisplayIcon: GetString(key, "DisplayIcon"),
            EstimatedSizeBytes: sizeBytes,
            IsMsi: isMsi);
    }

    private static string? GetString(RegistryKey key, string name) => key.GetValue(name) as string;

    private static string? CleanLocation(string? location)
    {
        if (location is null) return null;
        var text = location.Trim().Trim('"').Trim().TrimEnd('\\');
        return text.Length == 0 ? null : text;
    }
}

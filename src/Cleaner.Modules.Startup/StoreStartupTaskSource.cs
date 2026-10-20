using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Cleaner.Core.Localization;
using Microsoft.Win32;

namespace Cleaner.Modules.Startup;

/// <summary>
/// Pakattujen sovellusten (Store/MSIX) StartupTask-kohteet. Sovellus ilmoittaa tehtävän paketin manifestissa,
/// ja Windows pitää sen tilaa avaimessa
/// HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\&lt;pakettiperhe&gt;\&lt;tehtävä&gt;,
/// DWORD-arvo "State" (Windows.ApplicationModel.StartupTaskState). Tehtävienhallinta näyttää nämä
/// Käynnistys-välilehdellä muiden kohteiden joukossa ja kirjoittaa käytöstä poistossa tilan DisabledByUser.
///
/// Näyttönimi haetaan Repository\Packages-avaimesta ja ms-resource-viittaus ratkaistaan SHLoadIndirectString-kutsulla.
/// Avainpolut ovat vaihdettavia testejä varten (testit käyttävät omaa HKCU\Software\PcCleanerTests-avainta).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StoreStartupTaskSource
{
    public const string Key = "storetask";
    public static string Category => Loc.T("Store apps (StartupTask)", "Store-sovellukset (StartupTask)");
    public const string StateValueName = "State";
    public const string DefaultSystemAppDataSubKey =
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";
    public const string DefaultPackagesSubKey =
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    /// <summary>Windows.ApplicationModel.StartupTaskState.</summary>
    public enum TaskState { Disabled = 0, DisabledByUser = 1, Enabled = 2, DisabledByPolicy = 3, EnabledByPolicy = 4 }

    /// <param name="PackageFamily">Pakettiperhe, esim. Microsoft.YourPhone_8wekyb3d8bbwe.</param>
    /// <param name="TaskId">Manifestin TaskId, esim. YourPhone.Start.</param>
    /// <param name="DisplayName">Sovelluksen nimi tai paketin nimiosa, jos nimeä ei saatu.</param>
    public sealed record StoreTask(string PackageFamily, string TaskId, string DisplayName, TaskState State)
    {
        public string Name => MakeName(PackageFamily, TaskId);
    }

    private readonly string _systemAppDataSubKey;
    private readonly string _packagesSubKey;

    public static StoreStartupTaskSource Default { get; } = new(DefaultSystemAppDataSubKey, DefaultPackagesSubKey);

    public StoreStartupTaskSource(string systemAppDataSubKey, string packagesSubKey)
    {
        _systemAppDataSubKey = systemAppDataSubKey;
        _packagesSubKey = packagesSubKey;
    }

    /// <summary>Rekisteripolku CleanItem.Pathia varten.</summary>
    public string RegistryPath(string packageFamily, string taskId) => $@"HKCU\{_systemAppDataSubKey}\{packageFamily}\{taskId}";

    // ---------- Nimi ----------

    public static string MakeName(string packageFamily, string taskId) => $"{packageFamily}|{taskId}";

    public static bool TryParseName(string name, out string packageFamily, out string taskId)
    {
        var bar = name.IndexOf('|');
        if (bar > 0 && bar < name.Length - 1 && name.IndexOf('|', bar + 1) < 0
            && !name.Contains('\\') && !name.Contains('/'))
        {
            packageFamily = name[..bar];
            taskId = name[(bar + 1)..];
            return true;
        }
        packageFamily = "";
        taskId = "";
        return false;
    }

    // ---------- Luku ----------

    /// <summary>Käytössä olevat tehtävät (Enabled tai EnabledByPolicy) pakettiperheen ja tehtävän mukaan järjestettynä.</summary>
    public List<StoreTask> ListEnabled()
    {
        var list = new List<StoreTask>();
        using var root = Registry.CurrentUser.OpenSubKey(_systemAppDataSubKey, writable: false);
        if (root is null) return list;

        foreach (var family in root.GetSubKeyNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            using var familyKey = TryOpen(root, family);
            if (familyKey is null) continue;
            string? displayName = null;

            foreach (var taskId in familyKey.GetSubKeyNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                using var taskKey = TryOpen(familyKey, taskId);
                var state = ReadState(taskKey);
                if (state is not (TaskState.Enabled or TaskState.EnabledByPolicy)) continue;

                displayName ??= ResolveDisplayName(family);
                list.Add(new StoreTask(family, taskId, displayName, state.Value));
            }
        }
        return list;
    }

    /// <summary>Tehtävän nykytila tai null, jos avainta tai State-arvoa ei ole.</summary>
    public TaskState? GetState(string packageFamily, string taskId)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{_systemAppDataSubKey}\{packageFamily}\{taskId}", writable: false);
        return ReadState(key);
    }

    private static TaskState? ReadState(RegistryKey? taskKey)
    {
        if (taskKey is null) return null;
        try
        {
            if (taskKey.GetValueKind(StateValueName) != RegistryValueKind.DWord) return null;
        }
        catch (IOException) { return null; } // arvoa ei ole
        return taskKey.GetValue(StateValueName) is int state ? (TaskState)state : null;
    }

    private static RegistryKey? TryOpen(RegistryKey parent, string name)
    {
        try { return parent.OpenSubKey(name, writable: false); }
        catch (Exception) { return null; }
    }

    // ---------- Kirjoitus ----------

    /// <summary>Kirjoittaa tilan DisabledByUser täsmälleen kuten Tehtävienhallinta. Avaimen on oltava olemassa.</summary>
    public void Disable(string packageFamily, string taskId) => WriteState(packageFamily, taskId, TaskState.DisabledByUser);

    /// <summary>Palautus: tila takaisin Enabled.</summary>
    public void Enable(string packageFamily, string taskId) => WriteState(packageFamily, taskId, TaskState.Enabled);

    private void WriteState(string packageFamily, string taskId, TaskState state)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{_systemAppDataSubKey}\{packageFamily}\{taskId}", writable: true)
            ?? throw new IOException(Loc.T("Key does not exist.", "Avainta ei ole."));
        key.SetValue(StateValueName, (int)state, RegistryValueKind.DWord);
    }

    // ---------- Näyttönimi ----------

    /// <summary>
    /// Repository\Packages\&lt;täysnimi&gt;\DisplayName, jossa täysnimi on Nimi_Versio_Arkkitehtuuri_ResurssiId_JulkaisijaId
    /// ja perhe on Nimi_JulkaisijaId. ms-resource-viittaus ratkaistaan; jos mikään ei onnistu, palautetaan paketin nimiosa.
    /// </summary>
    public string ResolveDisplayName(string packageFamily)
    {
        var underscore = packageFamily.LastIndexOf('_');
        var name = underscore > 0 ? packageFamily[..underscore] : packageFamily;
        var publisherId = underscore > 0 ? packageFamily[(underscore + 1)..] : "";

        try
        {
            using var packages = Registry.CurrentUser.OpenSubKey(_packagesSubKey, writable: false);
            if (packages is null) return name;

            foreach (var fullName in packages.GetSubKeyNames())
            {
                if (!fullName.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase)
                    || !fullName.EndsWith("_" + publisherId, StringComparison.OrdinalIgnoreCase)) continue;

                using var package = TryOpen(packages, fullName);
                if (package?.GetValue("DisplayName") is not string raw || string.IsNullOrWhiteSpace(raw)) continue;

                var resolved = ResolveIndirect(raw, fullName, name);
                if (!string.IsNullOrWhiteSpace(resolved)) return resolved.Trim();
            }
        }
        catch (Exception) { }
        return name;
    }

    /// <summary>Selväkielinen nimi sellaisenaan; "@{...}" ja "ms-resource:..." SHLoadIndirectString-kutsulla.</summary>
    public static string? ResolveIndirect(string raw, string packageFullName, string packageName)
    {
        if (raw.StartsWith('@')) return LoadIndirectString(raw);
        if (!raw.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) return raw;

        var resource = raw["ms-resource:".Length..];
        var candidates = raw.StartsWith("ms-resource://", StringComparison.OrdinalIgnoreCase)
            ? new[] { $"@{{{packageFullName}?{raw}}}" }
            : new[]
            {
                $"@{{{packageFullName}?ms-resource://{packageName}/Resources/{resource}}}",
                $"@{{{packageFullName}?ms-resource://{packageName}/{resource}}}",
            };
        foreach (var candidate in candidates)
        {
            var text = LoadIndirectString(candidate);
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return null;
    }

    private static string? LoadIndirectString(string source)
    {
        var buffer = new StringBuilder(512);
        try
        {
            return SHLoadIndirectString(source, buffer, buffer.Capacity, IntPtr.Zero) == 0 ? buffer.ToString() : null;
        }
        catch (Exception) { return null; }
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);
}

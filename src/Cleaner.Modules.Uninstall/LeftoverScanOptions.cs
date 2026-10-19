using System.Runtime.Versioning;

namespace Cleaner.Modules.Uninstall;

public enum BaseFolderKind { ProgramFiles, ProgramData, LocalPrograms, UserData }

/// <summary>Peruskansio, jonka välittömiä lapsia verrataan ohjelman nimeen ("Matching rules").</summary>
public sealed record BaseFolder(string Path, BaseFolderKind Kind);

/// <summary>
/// Pikakuvakekansio. Käynnistä-valikon Programs-kansiot luetaan rekursiivisesti ja niissä myös
/// kansionimi voi osua; työpöydiltä luetaan vain .lnk-tiedostot.
/// </summary>
public sealed record ShortcutFolder(string Path, bool IsStartMenu);

/// <summary>Jäännösskannauksen juuret. Oletukset ovat oikean koneen kansiot ja avaimet; testit antavat omansa.</summary>
[SupportedOSPlatform("windows")]
public sealed record LeftoverScanOptions
{
    public IReadOnlyList<BaseFolder> BaseFolders { get; init; } = DefaultBaseFolders();
    public IReadOnlyList<ShortcutFolder> ShortcutFolders { get; init; } = DefaultShortcutFolders();

    public IReadOnlyList<string> RegistryRoots { get; init; } =
        new[] { @"HKCU\Software", @"HKLM\SOFTWARE", @"HKLM\SOFTWARE\WOW6432Node" };

    public IReadOnlyList<string> UninstallRoots { get; init; } =
        new[] { InstalledProgramReader.UninstallHklm, InstalledProgramReader.UninstallHklm32, InstalledProgramReader.UninstallHkcu };

    public IReadOnlyList<string> AppPathsRoots { get; init; } = new[]
    {
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths"
    };

    public IReadOnlyList<string> RunKeys { get; init; } = new[]
    {
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
    };

    public IReadOnlyList<string> ClassesRoots { get; init; } =
        new[] { @"HKCU\Software\Classes", @"HKLM\SOFTWARE\Classes" };

    /// <summary>Rekisteriarvojen rekursion enimmäissyvyys juuresta laskettuna.</summary>
    public int MaxRegistryDepth { get; init; } = 6;

    private static IReadOnlyList<BaseFolder> DefaultBaseFolders()
    {
        var list = new List<BaseFolder>();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Add(list, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), BaseFolderKind.ProgramFiles);
        Add(list, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), BaseFolderKind.ProgramFiles);
        Add(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), BaseFolderKind.ProgramData);
        Add(list, local, BaseFolderKind.UserData);
        Add(list, local.Length == 0 ? "" : Path.Combine(local, "Programs"), BaseFolderKind.LocalPrograms);
        Add(list, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), BaseFolderKind.UserData);
        Add(list, local.Length == 0 ? "" : Path.GetFullPath(Path.Combine(local, "..", "LocalLow")), BaseFolderKind.UserData);
        return list;
    }

    private static IReadOnlyList<ShortcutFolder> DefaultShortcutFolders()
    {
        var list = new List<ShortcutFolder>();
        AddShortcut(list, Environment.GetFolderPath(Environment.SpecialFolder.Programs), isStartMenu: true);
        AddShortcut(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), isStartMenu: true);
        AddShortcut(list, Environment.GetFolderPath(Environment.SpecialFolder.Desktop), isStartMenu: false);
        AddShortcut(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), isStartMenu: false);
        return list;
    }

    private static void Add(List<BaseFolder> list, string path, BaseFolderKind kind)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
        if (list.Any(b => b.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
        list.Add(new BaseFolder(path, kind));
    }

    private static void AddShortcut(List<ShortcutFolder> list, string path, bool isStartMenu)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
        if (list.Any(s => s.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
        list.Add(new ShortcutFolder(path, isStartMenu));
    }
}

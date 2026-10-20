namespace Cleaner.Modules.Uninstall;

/// <summary>Yksi Uninstall-rekisteriavaimesta luettu asennettu ohjelma.</summary>
public sealed record InstalledProgram(
    string KeyPath,            // esim. HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Foo
    string KeyName,            // avaimen oma nimi (MSI: {GUID})
    string Name,               // DisplayName
    string? Version,           // DisplayVersion
    string? Publisher,
    string? InstallLocation,   // trimmattu: lainausmerkit ja loppukeno pois, tyhjä → null
    string? UninstallString,
    string? QuietUninstallString,
    string? DisplayIcon,
    long EstimatedSizeBytes,   // EstimatedSize (KB) * 1024, 0 jos puuttuu
    bool IsMsi,                // WindowsInstaller == 1 tai UninstallString-exe on msiexec.exe
    DateOnly? InstallDate = null); // InstallDate-arvo (yyyyMMdd), null jos puuttuu tai ei jäsenny

/// <summary>Ohjelmalistan järjestys (UI ja CLI käyttävät samaa).</summary>
public enum ProgramSortOrder
{
    /// <summary>Nimen mukaan, kirjainkoosta riippumatta.</summary>
    Name,
    /// <summary>Asennuspäivä uusin ensin; ohjelmat ilman päivää listan loppuun nimen mukaan.</summary>
    InstallDateDescending
}

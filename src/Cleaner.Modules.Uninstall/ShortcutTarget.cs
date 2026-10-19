using System.Runtime.Versioning;

namespace Cleaner.Modules.Uninstall;

/// <summary>
/// Lukee .lnk-pikakuvakkeen kohdepolun WScript.Shell-COM-olion kautta. Logiikka on kopioitu
/// Startup-moduulin ShortcutResolverista, jotta moduulit eivät viittaa toisiinsa.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ShortcutTarget
{
    /// <summary>Kohdepolku tai null, jos tiedosto ei ole .lnk, kohde on tyhjä tai luku epäonnistuu.</summary>
    public static string? Resolve(string lnkPath)
    {
        if (!lnkPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return null;

        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return null;

            dynamic shell = Activator.CreateInstance(type)!;
            dynamic shortcut = shell.CreateShortcut(lnkPath);
            string target = shortcut.TargetPath ?? "";
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

using System.Runtime.Versioning;

namespace Cleaner.Modules.Startup;

/// <summary>Selvittää .lnk-pikakuvakkeen kohteen WScript.Shell-COM-olion kautta. Virhe → null.</summary>
[SupportedOSPlatform("windows")]
internal static class ShortcutResolver
{
    /// <summary>Palauttaa pikakuvakkeen kohteen ja argumentit, tai itse tiedostopolun jos ei ole .lnk tai luku epäonnistuu.</summary>
    public static string Describe(string filePath)
    {
        if (!filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return filePath;

        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return filePath;

            dynamic shell = Activator.CreateInstance(type)!;
            dynamic shortcut = shell.CreateShortcut(filePath);
            string target = shortcut.TargetPath ?? "";
            string args = shortcut.Arguments ?? "";
            if (string.IsNullOrWhiteSpace(target)) return filePath;
            return string.IsNullOrWhiteSpace(args) ? target : $"{target} {args}";
        }
        catch (Exception)
        {
            return filePath;
        }
    }
}

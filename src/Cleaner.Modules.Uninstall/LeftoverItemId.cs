namespace Cleaner.Modules.Uninstall;

public enum LeftoverKind { Dir, File, Key, Value }

/// <summary>
/// Jäännöskohteiden tunnisteet: uninstall:dir:&lt;polku&gt;, uninstall:file:&lt;polku&gt;,
/// uninstall:key:&lt;avain&gt;, uninstall:value:&lt;avain&gt;|&lt;arvonimi&gt;. Erotin '|' kuten RegistryModule.MakeItemId.
/// </summary>
public static class LeftoverItemId
{
    public const string Prefix = "uninstall";
    public const char ValueSeparator = '|';

    public static string MakeDir(string path) => $"{Prefix}:dir:{path}";
    public static string MakeFile(string path) => $"{Prefix}:file:{path}";
    public static string MakeKey(string keyPath) => $"{Prefix}:key:{keyPath}";
    public static string MakeValue(string keyPath, string valueName) => $"{Prefix}:value:{keyPath}{ValueSeparator}{valueName}";

    public static bool TryParse(string id, out LeftoverKind kind, out string path, out string? valueName)
    {
        kind = default; path = ""; valueName = null;
        if (string.IsNullOrEmpty(id)) return false;

        var parts = id.Split(':', 3);
        if (parts.Length != 3 || parts[0] != Prefix) return false;

        switch (parts[1])
        {
            case "dir": kind = LeftoverKind.Dir; break;
            case "file": kind = LeftoverKind.File; break;
            case "key": kind = LeftoverKind.Key; break;
            case "value": kind = LeftoverKind.Value; break;
            default: return false;
        }

        var rest = parts[2];
        if (kind == LeftoverKind.Value)
        {
            var sep = rest.IndexOf(ValueSeparator);
            if (sep <= 0) return false;
            path = rest[..sep];
            valueName = rest[(sep + 1)..];
            return true;
        }

        if (rest.Length == 0) return false;
        path = rest;
        return true;
    }
}

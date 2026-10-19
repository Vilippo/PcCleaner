using System.Diagnostics;
using System.Runtime.Versioning;
using Cleaner.Core.Logging;

namespace Cleaner.Modules.RegistryCleaner;

/// <summary>
/// .reg-varmuuskopiot reg.exe-työkalulla. Yksi kansio per puhdistusajo:
/// %LOCALAPPDATA%\PcCleaner\registry-backups\&lt;aikaleima&gt;\NNN-&lt;avain&gt;.reg + manifest.txt.
/// Palautus tuo kansion kaikki .reg-tiedostot takaisin (reg import).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryBackup
{
    public sealed record BackupInfo(string Timestamp, string Directory, int FileCount, string Summary);
    public sealed record RestoreResult(int Imported, int Failed, IReadOnlyList<string> Messages);

    private readonly string _root;
    private readonly ICleanerLogger _log;
    private readonly HashSet<string> _exported = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _manifest = new();
    private int _counter;

    public string Timestamp { get; }
    public string Directory { get; }
    public bool HasFiles => _counter > 0;

    public RegistryBackup(string root, ICleanerLogger log)
    {
        _root = root;
        _log = log;
        Timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Directory = Path.Combine(root, Timestamp);
    }

    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcCleaner", "registry-backups");

    /// <summary>Vie avaimen (koko alipuu) .reg-tiedostoon. Sama avain viedään vain kerran per ajo.</summary>
    public bool TryExport(string keyPath, out string error)
    {
        error = "";
        if (_exported.Contains(keyPath)) return true;

        var longPath = RegistryPaths.ToLongForm(keyPath);
        if (longPath is null)
        {
            error = "Virheellinen rekisteripolku.";
            return false;
        }

        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var file = Path.Combine(Directory, $"{++_counter:000}-{Sanitize(keyPath)}.reg");

            var (exitCode, output) = RunReg("export", longPath, file, "/y");
            if (exitCode != 0 || !File.Exists(file))
            {
                _counter--;
                error = $"reg export epäonnistui ({exitCode}): {output}";
                return false;
            }

            _exported.Add(keyPath);
            _manifest.Add($"{Path.GetFileName(file)}\t{keyPath}");
            _log.Debug($"Varmuuskopio: {keyPath} → {file}");
            return true;
        }
        catch (Exception ex)
        {
            error = $"Varmuuskopio epäonnistui: {ex.Message}";
            return false;
        }
    }

    /// <summary>Kirjoittaa manifest.txt:n: mitä vietiin ja mitä kohteita käsiteltiin.</summary>
    public void WriteManifest(IEnumerable<string> processedItems)
    {
        if (!HasFiles) return;
        var lines = new List<string>
        {
            $"PC Cleaner rekisterivarmuuskopio {Timestamp}",
            $"Palautus: pccleaner restore {Timestamp}",
            "",
            "Viedyt avaimet:"
        };
        lines.AddRange(_manifest);
        lines.Add("");
        lines.Add("Käsitellyt kohteet:");
        lines.AddRange(processedItems);
        File.WriteAllLines(Path.Combine(Directory, "manifest.txt"), lines);
    }

    public static IReadOnlyList<BackupInfo> List(string? root = null)
    {
        root ??= DefaultRoot;
        if (!System.IO.Directory.Exists(root)) return Array.Empty<BackupInfo>();

        var list = new List<BackupInfo>();
        foreach (var dir in System.IO.Directory.GetDirectories(root).OrderByDescending(d => d))
        {
            var files = System.IO.Directory.GetFiles(dir, "*.reg").Length;
            if (files == 0) continue;
            var manifest = Path.Combine(dir, "manifest.txt");
            var summary = "";
            if (File.Exists(manifest))
            {
                var lines = File.ReadAllLines(manifest);
                var items = lines.SkipWhile(l => l != "Käsitellyt kohteet:").Skip(1).Count(l => l.Length > 0);
                summary = $"{items} kohdetta";
            }
            list.Add(new BackupInfo(Path.GetFileName(dir), dir, files, summary));
        }
        return list;
    }

    /// <summary>Tuo kansion kaikki .reg-tiedostot takaisin. HKLM-avaimet vaativat järjestelmänvalvojan.</summary>
    public static RestoreResult Restore(string timestamp, ICleanerLogger log, string? root = null)
    {
        root ??= DefaultRoot;
        var dir = Path.Combine(root, timestamp);
        var messages = new List<string>();

        if (!System.IO.Directory.Exists(dir))
        {
            messages.Add($"Varmuuskopiota ei ole: {dir}");
            return new RestoreResult(0, 0, messages);
        }

        int ok = 0, failed = 0;
        foreach (var file in System.IO.Directory.GetFiles(dir, "*.reg").OrderBy(f => f))
        {
            var (exitCode, output) = RunReg("import", file);
            if (exitCode == 0)
            {
                ok++;
                log.Info($"Palautettu: {Path.GetFileName(file)}");
            }
            else
            {
                failed++;
                var msg = $"{Path.GetFileName(file)}: reg import epäonnistui ({exitCode}) {output}";
                messages.Add(msg);
                log.Warning(msg);
            }
        }

        return new RestoreResult(ok, failed, messages);
    }

    private static (int ExitCode, string Output) RunReg(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "reg.exe"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(60_000))
        {
            try { process.Kill(); } catch { }
            return (-1, "aikakatkaisu");
        }
        var output = (stdout + " " + stderr).Replace("\r", " ").Replace("\n", " ").Trim();
        return (process.ExitCode, output);
    }

    private static string Sanitize(string keyPath)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = keyPath.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray();
        var s = new string(chars);
        return s.Length <= 100 ? s : s[^100..];
    }
}

using System.Runtime.Versioning;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using Microsoft.Win32;

namespace Cleaner.Modules.Uninstall;

/// <summary>
/// Hakee poistetun (tai poistettavan) ohjelman jäänteet suunnitelman "Matching rules" -taulukon mukaan (REQ-3).
/// Vain lukua: ei luo, muuta eikä poista mitään. Sama polku tai avain tuotetaan enintään kerran;
/// ensimmäinen osuva sääntö voittaa (EDGE-9).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LeftoverScanner
{
    public const string InstallFolderCategory = "Asennuskansio";
    public const string ProgramFoldersCategory = "Ohjelmakansiot";
    public const string UserDataFoldersCategory = "Käyttäjän datakansiot";
    public const string ShortcutsCategory = "Pikakuvakkeet";
    public const string RegistryKeysCategory = "Rekisteriavaimet";
    public const string RegistryValuesCategory = "Rekisteriarvot";

    /// <summary>Juuren lapset, joihin ei laskeuduta nimihaussa eikä arvohaussa (ForbiddenSegments lisäksi).</summary>
    private static readonly string[] SkippedSegments = { "Classes", "Microsoft", "Policies" };

    /// <summary>Juuren lapset, joita ei verrata ohjelman nimeen avainsäännössä.</summary>
    private static readonly string[] SkippedKeyChildren = { "WOW6432Node", "Clients", "RegisteredApplications" };

    private static readonly EnumerationOptions Enumeration = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    private static readonly EnumerationOptions RecursiveEnumeration = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = true
    };

    private readonly LeftoverScanOptions _o;

    public LeftoverScanner(LeftoverScanOptions options) => _o = options;

    public LeftoverScanOptions Options => _o;

    public IReadOnlyList<CleanItem> Scan(InstalledProgram program, bool keyRemoved, string moduleId,
        IProgress<(int Percent, string Message)>? progress, CancellationToken ct)
    {
        var run = new ScanRun(_o, program, keyRemoved, moduleId, progress, ct);
        return run.Execute();
    }

    /// <summary>Yhden skannauksen tila: löydetyt kohteet ja päällekkäisyyksien esto.</summary>
    private sealed class ScanRun
    {
        private readonly LeftoverScanOptions _o;
        private readonly InstalledProgram _p;
        private readonly bool _keyRemoved;
        private readonly string _moduleId;
        private readonly IProgress<(int Percent, string Message)>? _progress;
        private readonly CancellationToken _ct;

        private readonly List<CleanItem> _items = new();
        private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _folders = new();      // lisätyt kansiot (etuliitetarkistus)
        private readonly List<string> _ruleFolders = new();  // kansiosääntöjen kansiot (pikakuvakkeiden kohteet)
        private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _keyList = new();
        private readonly HashSet<string> _values = new(StringComparer.OrdinalIgnoreCase);

        private readonly string? _install;

        public ScanRun(LeftoverScanOptions o, InstalledProgram p, bool keyRemoved, string moduleId,
            IProgress<(int Percent, string Message)>? progress, CancellationToken ct)
        {
            _o = o; _p = p; _keyRemoved = keyRemoved; _moduleId = moduleId; _progress = progress; _ct = ct;
            _install = EffectiveInstallLocation();
        }

        public IReadOnlyList<CleanItem> Execute()
        {
            Report(0, "Kansiot");
            _ct.ThrowIfCancellationRequested();
            ScanInstallFolder();
            ScanUninstallerFolder();
            ScanBaseFolders();

            Report(30, "Pikakuvakkeet");
            _ct.ThrowIfCancellationRequested();
            ScanShortcuts();

            Report(50, "Rekisteriavaimet");
            _ct.ThrowIfCancellationRequested();
            ScanNamedKeys();
            ScanUninstallKey();
            ScanAppPaths();
            ScanProgIds();

            Report(70, "Rekisteriarvot");
            ScanValues();

            Report(100, "Valmis");
            return _items;
        }

        private void Report(int percent, string message) => _progress?.Report((percent, message));

        // ---------- Asennuskansio (EDGE-2) ----------

        /// <summary>
        /// InstallLocation, kun se on olemassa, ei ole peruskansio tai pikakuvakekansio, ei peruskansion yläkansio
        /// ja PathGuard sallii sen. Muuten null, jolloin InstallLocation-säännöt ohitetaan.
        /// </summary>
        private string? EffectiveInstallLocation()
        {
            var raw = _p.InstallLocation?.Trim().Trim('"').Trim().TrimEnd('\\');
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var path = NormalizePath(raw);
            if (path is null || !Directory.Exists(path)) return null;
            if (NameMatcher.IsDeniedFolderName(Path.GetFileName(path))) return null; // esim. Common Files, Microsoft

            foreach (var b in _o.BaseFolders)
            {
                var bp = NormalizePath(b.Path);
                if (bp is null) continue;
                if (bp.Equals(path, StringComparison.OrdinalIgnoreCase)) return null;
                if (IsUnder(bp, path)) return null; // peruskansion yläkansio
            }
            foreach (var s in _o.ShortcutFolders)
            {
                var sp = NormalizePath(s.Path);
                if (sp is not null && sp.Equals(path, StringComparison.OrdinalIgnoreCase)) return null;
            }

            return PathGuard.IsSafeToDelete(path, out _) ? path : null;
        }

        private void ScanInstallFolder()
        {
            if (_install is null) return;
            AddFolder(_install, InstallFolderCategory, RiskLevel.Medium, preselected: true,
                $"Asennuskansio (InstallLocation) ohjelmalle '{_p.Name}'", ruleFolder: true);
        }

        /// <summary>DisplayIcon- tai UninstallString-exen kansio Program Files- tai LocalAppData\Programs-kansion alla.</summary>
        private void ScanUninstallerFolder()
        {
            foreach (var exe in new[] { IconExecutable(_p.DisplayIcon), UninstallCommandLine.ExtractExecutable(_p.UninstallString) })
            {
                if (exe is null) continue;
                string? dir;
                try { dir = NormalizePath(Path.GetDirectoryName(exe)); }
                catch (Exception) { continue; }
                if (dir is null || !Directory.Exists(dir)) continue;

                var underAllowedBase = _o.BaseFolders.Any(b =>
                    b.Kind is BaseFolderKind.ProgramFiles or BaseFolderKind.LocalPrograms
                    && NormalizePath(b.Path) is { } bp
                    && IsUnder(dir, bp));
                if (!underAllowedBase || IsBaseOrShortcutFolder(dir)) continue;
                if (!PathGuard.IsSafeToDelete(dir, out _)) continue;

                var match = NameMatcher.Match(Path.GetFileName(dir), _p.Name, _p.Publisher);
                if (match is MatchKind.None or MatchKind.Publisher) continue; // julkaisijakansio on liian laaja

                AddFolder(dir, InstallFolderCategory, RiskLevel.Medium, preselected: true,
                    "Poisto-ohjelman kansio, " + MatchReason(match), ruleFolder: true);
            }
        }

        /// <summary>"C:\X\app.exe,0" → C:\X\app.exe; lainausmerkit ja kuvakeindeksi pois.</summary>
        private static string? IconExecutable(string? displayIcon)
        {
            if (string.IsNullOrWhiteSpace(displayIcon)) return null;
            var text = displayIcon.Trim();
            var comma = text.LastIndexOf(',');
            if (comma > 0 && int.TryParse(text[(comma + 1)..].Trim(), out _)) text = text[..comma];
            text = Environment.ExpandEnvironmentVariables(text.Trim().Trim('"').Trim());
            return text.Length == 0 || text.Contains('%') ? null : text;
        }

        // ---------- Ohjelmakansiot ja käyttäjän datakansiot ----------

        private void ScanBaseFolders()
        {
            foreach (var b in _o.BaseFolders)
            {
                _ct.ThrowIfCancellationRequested();
                var basePath = NormalizePath(b.Path);
                if (basePath is null || !Directory.Exists(basePath)) continue;

                var userData = b.Kind == BaseFolderKind.UserData;
                var category = userData ? UserDataFoldersCategory : ProgramFoldersCategory;
                var risk = userData ? RiskLevel.High : RiskLevel.Medium;

                foreach (var child in SortedDirectories(basePath))
                {
                    _ct.ThrowIfCancellationRequested();
                    var match = NameMatcher.Match(Path.GetFileName(child), _p.Name, _p.Publisher);
                    switch (match)
                    {
                        case MatchKind.Exact:
                        case MatchKind.Tokens:
                            AddFolder(child, category, risk, preselected: !userData && match == MatchKind.Exact,
                                "Kansion " + MatchReason(match), ruleFolder: true);
                            break;
                        case MatchKind.Publisher:
                            foreach (var grandchild in SortedDirectories(child))
                            {
                                var gm = NameMatcher.Match(Path.GetFileName(grandchild), _p.Name, null);
                                if (gm is MatchKind.Exact or MatchKind.Tokens)
                                    AddFolder(grandchild, category, risk, preselected: false,
                                        $"Julkaisijan '{_p.Publisher}' kansion alikansio, " + MatchReason(gm), ruleFolder: true);
                            }
                            break;
                    }
                }
            }
        }

        // ---------- Pikakuvakkeet ----------

        private void ScanShortcuts()
        {
            foreach (var s in _o.ShortcutFolders)
            {
                _ct.ThrowIfCancellationRequested();
                var root = NormalizePath(s.Path);
                if (root is null || !Directory.Exists(root)) continue;

                if (s.IsStartMenu)
                {
                    foreach (var dir in SortedDirectories(root, recursive: true))
                    {
                        var match = NameMatcher.Match(Path.GetFileName(dir), _p.Name, null);
                        if (match is MatchKind.Exact or MatchKind.Tokens)
                            AddFolder(dir, ShortcutsCategory, RiskLevel.Low, preselected: true,
                                "Käynnistä-valikon kansio, " + MatchReason(match), ruleFolder: false);
                    }
                }

                foreach (var lnk in SortedFiles(root, "*.lnk", s.IsStartMenu))
                {
                    _ct.ThrowIfCancellationRequested();
                    if (IsUnderAddedFolder(lnk)) continue;
                    var target = NormalizePath(ShortcutTarget.Resolve(lnk));
                    if (target is null) continue;

                    var folder = TargetFolder(target);
                    if (folder is null) continue;
                    AddFile(lnk, ShortcutsCategory, RiskLevel.Low, preselected: true, $"Pikakuvake osoittaa kansioon {folder}");
                }
            }
        }

        /// <summary>Asennuskansio tai kansiosäännön kansio, jonka alla pikakuvakkeen kohde on.</summary>
        private string? TargetFolder(string target)
        {
            if (_install is not null && IsUnder(target, _install)) return _install;
            return _ruleFolders.FirstOrDefault(f => IsUnder(target, f));
        }

        // ---------- Rekisteriavaimet ----------

        /// <summary>&lt;juuri&gt;\&lt;Nimi&gt; ja &lt;juuri&gt;\&lt;Julkaisija&gt;\&lt;Nimi&gt; jokaiselle RegistryRoots-juurelle.</summary>
        private void ScanNamedKeys()
        {
            foreach (var root in _o.RegistryRoots)
            {
                _ct.ThrowIfCancellationRequested();
                using var rootKey = OpenRoot(root);
                if (rootKey is null) continue;

                foreach (var child in SortedSubKeys(rootKey))
                {
                    _ct.ThrowIfCancellationRequested();
                    if (IsSkippedSegment(child) || SkippedKeyChildren.Contains(child, StringComparer.OrdinalIgnoreCase)) continue;

                    var match = NameMatcher.Match(child, _p.Name, _p.Publisher);
                    var childPath = RegistryPaths.Combine(root, child);
                    if (match == MatchKind.Exact)
                    {
                        AddKey(childPath, $"Rekisteriavain, nimi täsmää ohjelman nimeen '{_p.Name}'");
                    }
                    else if (match == MatchKind.Publisher)
                    {
                        using var pubKey = TryOpen(rootKey, child);
                        if (pubKey is null) continue;
                        foreach (var grandchild in SortedSubKeys(pubKey))
                        {
                            if (IsSkippedSegment(grandchild)) continue;
                            if (NameMatcher.Match(grandchild, _p.Name, null) == MatchKind.Exact)
                                AddKey(RegistryPaths.Combine(childPath, grandchild),
                                    $"Rekisteriavain julkaisijan '{_p.Publisher}' alla, nimi täsmää ohjelman nimeen '{_p.Name}'");
                        }
                    }
                }
            }
        }

        private void ScanUninstallKey()
        {
            if (_keyRemoved || string.IsNullOrEmpty(_p.KeyPath)) return;
            using var key = OpenRoot(_p.KeyPath);
            if (key is null) return;
            AddKey(_p.KeyPath, "Ohjelman Uninstall-avain ei poistunut poisto-ohjelman ajossa");
        }

        private void ScanAppPaths()
        {
            if (_install is null) return;
            foreach (var root in _o.AppPathsRoots)
            {
                _ct.ThrowIfCancellationRequested();
                using var rootKey = OpenRoot(root);
                if (rootKey is null) continue;

                foreach (var child in SortedSubKeys(rootKey))
                {
                    _ct.ThrowIfCancellationRequested();
                    using var key = TryOpen(rootKey, child);
                    if (key is null) continue;
                    if (PointsToInstall(ReadString(key, "")) || PointsToInstall(ReadString(key, "Path")))
                        AddKey(RegistryPaths.Combine(root, child), $"App Paths -avain viittaa asennuskansioon {_install}");
                }
            }
        }

        private void ScanProgIds()
        {
            if (_install is null) return;
            foreach (var root in _o.ClassesRoots)
            {
                _ct.ThrowIfCancellationRequested();
                using var rootKey = OpenRoot(root);
                if (rootKey is null) continue;

                foreach (var child in SortedSubKeys(rootKey))
                {
                    _ct.ThrowIfCancellationRequested();
                    if (child.StartsWith('.') || IsForbidden(child) || NameMatcher.IsDeniedFolderName(child)) continue;

                    using var command = TryOpen(rootKey, child + @"\shell\open\command");
                    if (command is null) continue;
                    var raw = ReadString(command, "");
                    var exe = UninstallCommandLine.ExtractExecutable(raw) ?? raw;
                    if (PointsToInstall(exe))
                        AddKey(RegistryPaths.Combine(root, child), $"ProgId-avaimen avauskomento viittaa asennuskansioon {_install}");
                }
            }
        }

        // ---------- Rekisteriarvot (EDGE-7) ----------

        private void ScanValues()
        {
            if (_install is null) return;

            foreach (var root in _o.RegistryRoots)
            {
                using var rootKey = OpenRoot(root);
                if (rootKey is null) continue;
                ScanValuesRecursive(rootKey, root.TrimEnd('\\'), depth: 0);
            }

            foreach (var runKey in _o.RunKeys)
            {
                using var key = OpenRoot(runKey);
                if (key is null) continue;
                ScanKeyValues(key, runKey.TrimEnd('\\'));
            }
        }

        private void ScanValuesRecursive(RegistryKey key, string keyPath, int depth)
        {
            _ct.ThrowIfCancellationRequested();
            // Juuren (syvyys 0) omat arvot eivät ole kohteita: vartija hylkää ne, koska juuren vienti veisi koko haaran.
            if (depth >= 1) ScanKeyValues(key, keyPath);
            if (depth >= _o.MaxRegistryDepth) return;

            foreach (var child in SortedSubKeys(key))
            {
                if (IsSkippedSegment(child)) continue;
                var childPath = RegistryPaths.Combine(keyPath, child);
                // Toinen juuri (esim. WOW6432Node) käsitellään omana juurenaan, ei kahdesti.
                if (_o.RegistryRoots.Any(r => r.TrimEnd('\\').Equals(childPath, StringComparison.OrdinalIgnoreCase))) continue;

                using var sub = TryOpen(key, child);
                if (sub is null) continue;
                ScanValuesRecursive(sub, childPath, depth + 1);
            }
        }

        private void ScanKeyValues(RegistryKey key, string keyPath)
        {
            _ct.ThrowIfCancellationRequested();
            string[] names;
            try { names = key.GetValueNames(); }
            catch (Exception) { return; }
            Array.Sort(names, StringComparer.OrdinalIgnoreCase);

            foreach (var name in names)
            {
                string? text;
                try
                {
                    var kind = key.GetValueKind(name);
                    if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString)) continue;
                    text = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                }
                catch (Exception) { continue; }

                if (PointsToInstall(text))
                    AddValue(keyPath, name, $"Arvo viittaa asennuskansioon {_install}");
            }
        }

        /// <summary>Laajennettu, lainausmerkitön arvo alkaa asennuskansiolla ja jatkuu '\':lla tai loppuu.</summary>
        private bool PointsToInstall(string? raw)
        {
            if (_install is null || string.IsNullOrWhiteSpace(raw)) return false;
            string text;
            try { text = Environment.ExpandEnvironmentVariables(raw).Replace("\"", "").Trim(); }
            catch (Exception) { return false; }
            if (!text.StartsWith(_install, StringComparison.OrdinalIgnoreCase)) return false;
            return text.Length == _install.Length || text[_install.Length] == '\\';
        }

        // ---------- Kohteiden lisäys ja päällekkäisyys (EDGE-9) ----------

        private void AddFolder(string path, string category, RiskLevel risk, bool preselected, string description, bool ruleFolder)
        {
            var full = NormalizePath(path);
            if (full is null || _paths.Contains(full) || IsUnderAddedFolder(full) || IsParentOfAddedFolder(full)) return;

            _paths.Add(full);
            _folders.Add(full);
            if (ruleFolder) _ruleFolders.Add(full);
            _items.Add(new CleanItem(LeftoverItemId.MakeDir(full), _moduleId, category, full, DirectorySize(full),
                description, risk, preselected));
        }

        private void AddFile(string path, string category, RiskLevel risk, bool preselected, string description)
        {
            var full = NormalizePath(path);
            if (full is null || _paths.Contains(full) || IsUnderAddedFolder(full)) return;

            _paths.Add(full);
            long size = 0;
            try { size = new FileInfo(full).Length; } catch (Exception) { }
            _items.Add(new CleanItem(LeftoverItemId.MakeFile(full), _moduleId, category, full, size, description, risk, preselected));
        }

        private void AddKey(string keyPath, string description)
        {
            var path = keyPath.TrimEnd('\\');
            if (_keys.Contains(path) || IsUnderAddedKey(path) || IsParentOfAddedKey(path)) return;

            _keys.Add(path);
            _keyList.Add(path);
            _items.Add(new CleanItem(LeftoverItemId.MakeKey(path), _moduleId, RegistryKeysCategory, path, 0,
                description, RiskLevel.High, false));
        }

        private void AddValue(string keyPath, string valueName, string description)
        {
            var path = keyPath.TrimEnd('\\');
            if (_keys.Contains(path) || IsUnderAddedKey(path)) return;
            var id = LeftoverItemId.MakeValue(path, valueName);
            if (!_values.Add(id)) return;

            var shown = valueName.Length == 0 ? "(oletus)" : valueName;
            _items.Add(new CleanItem(id, _moduleId, RegistryValuesCategory, $@"{path}\{shown}", 0,
                description, RiskLevel.High, false));
        }

        private bool IsUnderAddedFolder(string full) => _folders.Any(f => IsUnder(full, f));
        private bool IsParentOfAddedFolder(string full) => _folders.Any(f => IsUnder(f, full));
        private bool IsUnderAddedKey(string path) => _keyList.Any(k => IsUnder(path, k));
        private bool IsParentOfAddedKey(string path) => _keyList.Any(k => IsUnder(k, path));

        private bool IsBaseOrShortcutFolder(string full) =>
            _o.BaseFolders.Any(b => full.Equals(NormalizePath(b.Path), StringComparison.OrdinalIgnoreCase))
            || _o.ShortcutFolders.Any(s => full.Equals(NormalizePath(s.Path), StringComparison.OrdinalIgnoreCase));

        // ---------- Apurit ----------

        private static bool IsUnder(string path, string parent) =>
            path.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase);

        private static bool IsForbidden(string segment) =>
            RegistryGuard.ForbiddenSegments.Contains(segment, StringComparer.OrdinalIgnoreCase);

        private static bool IsSkippedSegment(string segment) =>
            IsForbidden(segment) || SkippedSegments.Contains(segment, StringComparer.OrdinalIgnoreCase);

        private string MatchReason(MatchKind match) => match switch
        {
            MatchKind.Exact => $"nimi täsmää ohjelman nimeen '{_p.Name}'",
            MatchKind.Tokens => $"nimi sisältää ohjelman nimen sanat '{string.Join(' ', NameMatcher.SignificantTokens(_p.Name))}'",
            MatchKind.Publisher => $"nimi täsmää julkaisijaan '{_p.Publisher}'",
            _ => "ei osumaa"
        };

        /// <summary>Täysi polku ilman loppukenoa; suhteellinen tai virheellinen polku → null.</summary>
        private static string? NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                var trimmed = path.Trim().Trim('"').Trim();
                if (!Path.IsPathFullyQualified(trimmed)) return null;
                var full = Path.GetFullPath(trimmed).TrimEnd('\\');
                return full.Length == 0 ? null : full;
            }
            catch (Exception) { return null; }
        }

        private static string[] SortedDirectories(string path, bool recursive = false)
        {
            string[] dirs;
            try { dirs = Directory.EnumerateDirectories(path, "*", recursive ? RecursiveEnumeration : Enumeration).ToArray(); }
            catch (Exception) { return Array.Empty<string>(); }
            Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
            return dirs;
        }

        private static string[] SortedFiles(string path, string pattern, bool recursive)
        {
            string[] files;
            try { files = Directory.EnumerateFiles(path, pattern, recursive ? RecursiveEnumeration : Enumeration).ToArray(); }
            catch (Exception) { return Array.Empty<string>(); }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            return files;
        }

        private static string[] SortedSubKeys(RegistryKey key)
        {
            string[] names;
            try { names = key.GetSubKeyNames(); }
            catch (Exception) { return Array.Empty<string>(); }
            Array.Sort(names, StringComparer.OrdinalIgnoreCase);
            return names;
        }

        /// <summary>Avaa polun vain lukua varten; avaamisvirhe (oikeudet, virheellinen polku) → null.</summary>
        private static RegistryKey? OpenRoot(string path)
        {
            try { return RegistryPaths.OpenReadOnly(path); }
            catch (Exception) { return null; }
        }

        private static RegistryKey? TryOpen(RegistryKey parent, string subKey)
        {
            try { return parent.OpenSubKey(subKey, writable: false); }
            catch (Exception) { return null; }
        }

        private static string? ReadString(RegistryKey key, string name)
        {
            try { return key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string; }
            catch (Exception) { return null; }
        }

        private static long DirectorySize(string path)
        {
            long total = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", RecursiveEnumeration))
                {
                    try { total += new FileInfo(file).Length; } catch (Exception) { }
                }
            }
            catch (Exception) { }
            return total;
        }
    }
}

using Cleaner.Core.Abstractions;
using Cleaner.Core.Models;
using Cleaner.Core.Services;

namespace Cleaner.Modules.DiskUsage;

/// <summary>
/// Levytilan analyysi: rakentaa kansiopuun kokoineen ja raportoi suurimmat kansiot ja tiedostot.
/// Ylätason kansiot käsitellään rinnakkain, niiden alipuut peräkkäin. Reparse pointteja
/// (junction, symlink) ei seurata, jotta sama data ei tule laskettua kahdesti eikä puu kierrä.
///
/// Moduuli on lähtökohtaisesti lukeva. CleanAsync poistaa vain tiedostokohteita SafeFileDeleterin
/// kautta; kansiokohteet ohitetaan aina, koska suurimman kansion rekursiivinen poisto on juuri
/// se vahinko, jota tämä ohjelma ei saa tehdä. Kaikki kohteet ovat Risk = High, joten
/// käyttöliittymä ei koskaan valitse niitä oletuksena.
/// </summary>
public sealed class DiskUsageModule : ICleanerModule
{
    public string Id => "diskusage";
    public string DisplayName => "Levytilan analyysi";
    public string Description => "Etsii suurimmat kansiot ja tiedostot. Ei poista mitään automaattisesti.";
    public RiskLevel Risk => RiskLevel.High;
    public bool RequiresAdmin => false;

    /// <summary>Analyysi on hidas eikä siivoa mitään; ajetaan vain pyydettäessä.</summary>
    public bool SelectedByDefault => false;

    public const string FolderCategory = "Suurimmat kansiot";
    public const string FileCategory = "Suurimmat tiedostot";
    private const string DirPrefix = "diskusage:dir:";
    private const string FilePrefix = "diskusage:file:";

    private static readonly EnumerationOptions EnumOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint, // piilotetut ja järjestelmätiedostot lasketaan mukaan
        ReturnSpecialDirectories = false
    };

    private readonly DiskUsageOptions _options;

    /// <summary>Viimeisimmän skannauksen puut juurta kohden. Tyhjä ennen ensimmäistä skannausta.</summary>
    public IReadOnlyList<DirectoryNode> LastTrees { get; private set; } = Array.Empty<DirectoryNode>();

    public DiskUsageModule() : this(new DiskUsageOptions()) { }

    public DiskUsageModule(DiskUsageOptions options)
    {
        _options = options;
    }

    private sealed record FileEntry(string Path, long Bytes, DateTime LastWriteUtc);

    // ---------- Skannaus ----------

    public Task<ScanResult> ScanAsync(CleanerContext context, CancellationToken ct = default)
        => Task.Run(() => Scan(context, ct), ct);

    private ScanResult Scan(CleanerContext context, CancellationToken ct)
    {
        var result = new ScanResult { ModuleId = Id };
        var trees = new List<DirectoryNode>();
        var fileSinks = new List<List<FileEntry>>();
        var sinkLock = new object();

        var roots = _options.Roots.Where(Directory.Exists).ToList();
        foreach (var missing in _options.Roots.Except(roots, StringComparer.OrdinalIgnoreCase))
        {
            var msg = $"Kansiota ei ole: {missing}";
            context.Logger.Warning(msg);
            result.Errors.Add(msg);
        }

        // Ylätason kansiot kaikista juurista yhteen listaan, jotta edistyminen on yksi prosentti.
        var work = new List<(DirectoryNode Root, string Dir)>();
        foreach (var rootPath in roots)
        {
            var rootNode = new DirectoryNode { Path = Path.GetFullPath(rootPath) };
            trees.Add(rootNode);

            var rootFiles = new List<FileEntry>();
            foreach (var entry in SafeEnumerate(rootPath, rootNode))
            {
                ct.ThrowIfCancellationRequested();
                if (entry is DirectoryInfo di) work.Add((rootNode, di.FullName));
                else if (entry is FileInfo fi) CountFile(rootNode, fi, rootFiles);
            }
            fileSinks.Add(rootFiles);
        }

        int done = 0;
        var parallel = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Math.Max(1, _options.MaxDegreeOfParallelism)
        };

        try
        {
            Parallel.ForEach(work, parallel, item =>
            {
                var sink = new List<FileEntry>();
                var node = Walk(item.Dir, sink, ct);

                lock (sinkLock)
                {
                    item.Root.Children.Add(node);
                    fileSinks.Add(sink);
                    done++;
                }
                context.Report((int)(100.0 * done / Math.Max(1, work.Count)), node.Path);
            });
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
        {
            throw new OperationCanceledException(ct);
        }

        foreach (var root in trees)
        {
            root.TotalBytes = root.OwnBytes + root.Children.Sum(c => c.TotalBytes);
            root.FileCount += root.Children.Sum(c => c.FileCount);
            root.HadErrors |= root.Children.Any(c => c.HadErrors);
        }
        LastTrees = trees;

        // Suurimmat kansiot: kaikki solmut paitsi juuret, koko alipuun mukaan.
        var folders = trees
            .SelectMany(root => root.Descendants().Skip(1).Select(n => (Root: root, Node: n)))
            .Where(x => x.Node.TotalBytes > 0)
            .OrderByDescending(x => x.Node.TotalBytes)
            .Take(_options.TopFolders);

        foreach (var (root, node) in folders)
        {
            var share = root.TotalBytes > 0 ? (double)node.TotalBytes / root.TotalBytes : 0;
            var desc = $"{node.FileCount} tiedostoa, {share:P0} juuresta {root.Path}" + (node.HadErrors ? ", osa alikansioista ei luettavissa" : "");
            result.Items.Add(new CleanItem(DirPrefix + node.Path, Id, FolderCategory, node.Path, node.TotalBytes, desc, RiskLevel.High));
        }

        var files = fileSinks.SelectMany(s => s)
            .OrderByDescending(f => f.Bytes)
            .Take(_options.TopFiles);

        foreach (var file in files)
            result.Items.Add(new CleanItem(FilePrefix + file.Path, Id, FileCategory, file.Path, file.Bytes,
                $"Muokattu {file.LastWriteUtc.ToLocalTime():yyyy-MM-dd}", RiskLevel.High));

        context.Report(100, "Valmis");
        var totalBytes = trees.Sum(t => t.TotalBytes);
        var totalFiles = trees.Sum(t => t.FileCount);
        context.Logger.Info($"{DisplayName}: {roots.Count} juurta, {totalFiles} tiedostoa, {ByteFormatter.Format(totalBytes)}");
        return result;
    }

    private DirectoryNode Walk(string path, List<FileEntry> files, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var node = new DirectoryNode { Path = path };

        foreach (var entry in SafeEnumerate(path, node))
        {
            ct.ThrowIfCancellationRequested();
            if (entry is DirectoryInfo di)
            {
                var child = Walk(di.FullName, files, ct);
                node.Children.Add(child);
                node.TotalBytes += child.TotalBytes;
                node.FileCount += child.FileCount;
                node.HadErrors |= child.HadErrors;
            }
            else if (entry is FileInfo fi)
            {
                CountFile(node, fi, files);
            }
        }

        node.TotalBytes += node.OwnBytes;
        return node;
    }

    private void CountFile(DirectoryNode node, FileInfo fi, List<FileEntry> files)
    {
        long length;
        try { length = fi.Length; }
        catch (Exception) { node.HadErrors = true; return; }

        node.OwnBytes += length;
        node.FileCount++;

        // Pidetään vain top-N ehdokkaat, jotta muisti ei kasva tiedostomäärän mukana.
        files.Add(new FileEntry(fi.FullName, length, fi.LastWriteTimeUtc));
        if (files.Count > _options.TopFiles * 4)
        {
            files.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
            files.RemoveRange(_options.TopFiles, files.Count - _options.TopFiles);
        }
    }

    /// <summary>Enumeroi kansion sisällön niin, että yksittäinen virhe merkitään solmuun eikä kaada skannausta.</summary>
    private static IEnumerable<FileSystemInfo> SafeEnumerate(string path, DirectoryNode node)
    {
        IEnumerator<FileSystemInfo> enumerator;
        try
        {
            enumerator = new DirectoryInfo(path).EnumerateFileSystemInfos("*", EnumOptions).GetEnumerator();
        }
        catch (Exception)
        {
            node.HadErrors = true;
            yield break;
        }

        using (enumerator)
        {
            while (true)
            {
                bool more;
                try { more = enumerator.MoveNext(); }
                catch (Exception) { node.HadErrors = true; yield break; }
                if (!more) yield break;
                yield return enumerator.Current;
            }
        }
    }

    // ---------- Puhdistus ----------

    public Task<CleanResult> CleanAsync(IReadOnlyList<CleanItem> items, CleanerContext context, CancellationToken ct = default)
        => Task.Run(() => Clean(items, context, ct), ct);

    private CleanResult Clean(IReadOnlyList<CleanItem> items, CleanerContext context, CancellationToken ct)
    {
        var result = new CleanResult { ModuleId = Id, WasDryRun = context.DryRun };
        var deleter = new SafeFileDeleter(context.Logger, context.DryRun);

        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = items[i];
            context.Report((int)(100.0 * i / items.Count), item.Path);

            if (!string.Equals(item.ModuleId, Id, StringComparison.OrdinalIgnoreCase))
            {
                result.Skipped.Add((item, "Kohde ei kuulu tälle moduulille"));
                continue;
            }

            if (item.Id.StartsWith(DirPrefix, StringComparison.Ordinal))
            {
                result.Skipped.Add((item, "Kansioita ei poisteta tästä moduulista. Poista sisältö itse Resurssienhallinnassa."));
                continue;
            }

            if (!item.Id.StartsWith(FilePrefix, StringComparison.Ordinal))
            {
                result.Skipped.Add((item, "Virheellinen tunniste"));
                continue;
            }

            if (deleter.TryDeleteFile(item.Path, out var reason))
                result.Removed.Add(item);
            else
                result.Skipped.Add((item, reason));
        }

        context.Report(100, "Valmis");
        context.Logger.Info($"{DisplayName}: {(context.DryRun ? "poistettaisiin" : "poistettiin")} {result.Removed.Count} tiedostoa ({ByteFormatter.Format(result.FreedBytes)}), ohitettu {result.Skipped.Count}");
        return result;
    }
}

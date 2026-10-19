namespace Cleaner.Modules.DiskUsage;

/// <summary>
/// Yksi kansio levytilan puussa. Puu säilytetään skannauksen jälkeen
/// (<see cref="DiskUsageModule.LastTrees"/>), jotta käyttöliittymän treemap voi piirtää sen
/// ilman uutta skannausta.
/// </summary>
public sealed class DirectoryNode
{
    public required string Path { get; init; }

    /// <summary>Suoraan tässä kansiossa olevien tiedostojen koko.</summary>
    public long OwnBytes { get; internal set; }

    /// <summary>Koko alipuun koko (OwnBytes + lasten TotalBytes).</summary>
    public long TotalBytes { get; internal set; }

    /// <summary>Tiedostojen määrä koko alipuussa.</summary>
    public int FileCount { get; internal set; }

    /// <summary>Jokin alikansio jäi lukematta (oikeudet, lukittu).</summary>
    public bool HadErrors { get; internal set; }

    public List<DirectoryNode> Children { get; } = new();

    /// <summary>Kaikki solmut syvyysjärjestyksessä, tämä mukaan lukien.</summary>
    public IEnumerable<DirectoryNode> Descendants()
    {
        var stack = new Stack<DirectoryNode>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            foreach (var child in node.Children) stack.Push(child);
        }
    }
}

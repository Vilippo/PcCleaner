using System.Text;
using Cleaner.Core.Models;

namespace Cleaner.Ai;

/// <summary>Digestin rajat: täysi lista, otos ja koko tekstin listattujen kohteiden enimmäismäärä.</summary>
public sealed class DigestOptions
{
    /// <summary>Kategoria, jossa on enintään näin monta kohdetta, listataan kokonaan.</summary>
    public int FullListThreshold { get; init; } = 40;

    /// <summary>Suuremmasta kategoriasta listataan näin monta suurinta.</summary>
    public int SampleSize { get; init; } = 20;

    /// <summary>Listattujen kohderivien enimmäismäärä koko tekstissä.</summary>
    public int MaxListedItems { get; init; } = 400;
}

/// <summary>
/// Yhden moduulin skannaustulos mallille lähetettävänä tekstinä. Kategoriat ensiesiintymisjärjestyksessä,
/// kohteet koon mukaan suurin ensin. Jokainen listattu kohde saa avaimen <c>i1</c>, <c>i2</c>, ..., jolla malli
/// viittaa siihen. Teksti on englanniksi eikä riipu <c>Loc.Current</c>-kielestä; se ei näy käyttäjälle.
/// </summary>
public sealed class ScanDigest
{
    private const int MaxDescriptionLength = 120;
    private const string NotSummed = "size not summed (nested items)";

    public string ModuleName { get; }
    public string Text { get; }

    /// <summary>Avain ("i1") → kohde. Sisältää täsmälleen listatut rivit.</summary>
    public IReadOnlyDictionary<string, CleanItem> KeyToItem { get; }

    /// <summary>Kategorioiden nimet digestin järjestyksessä.</summary>
    public IReadOnlyList<string> Categories { get; }

    private ScanDigest(string moduleName, string text, IReadOnlyDictionary<string, CleanItem> keyToItem, IReadOnlyList<string> categories)
    {
        ModuleName = moduleName;
        Text = text;
        KeyToItem = keyToItem;
        Categories = categories;
    }

    /// <param name="sumSizes">False opt-in-moduuleille, joiden kohteet voivat olla sisäkkäisiä kansioita.</param>
    public static ScanDigest Build(string moduleName, IEnumerable<(CleanItem Item, bool Selected)> items, bool sumSizes,
        DigestOptions options, PathSanitizer sanitizer)
    {
        // Ryhmittely ensiesiintymisjärjestyksessä.
        var order = new List<string>();
        var groups = new Dictionary<string, List<(CleanItem Item, bool Selected)>>(StringComparer.Ordinal);
        foreach (var entry in items)
        {
            if (!groups.TryGetValue(entry.Item.Category, out var list))
            {
                list = new List<(CleanItem, bool)>();
                groups[entry.Item.Category] = list;
                order.Add(entry.Item.Category);
            }
            list.Add(entry);
        }

        var sb = new StringBuilder();
        sb.Append("# Module: ").Append(moduleName).Append('\n');
        sb.Append("Legend: [x] = currently selected for cleanup, [ ] = not selected. Keys i<n> identify listed items.\n");

        var keyToItem = new Dictionary<string, CleanItem>(StringComparer.Ordinal);
        int quota = Math.Max(0, options.MaxListedItems);
        int key = 0;

        foreach (var category in order)
        {
            var list = groups[category]
                .OrderByDescending(e => e.Item.SizeBytes)
                .ThenBy(e => e.Item.Path, StringComparer.Ordinal)
                .ToList();
            int n = list.Count;
            var risk = list.Max(e => e.Item.Risk);
            int selected = list.Count(e => e.Selected);
            var size = sumSizes ? AiFormat.Bytes(list.Sum(e => Math.Max(0, e.Item.SizeBytes))) : NotSummed;

            sb.Append('\n');
            sb.Append("## ").Append(category)
              .Append(" (risk ").Append(risk.ToString())
              .Append(", ").Append(n).Append(" items, ").Append(size)
              .Append(", ").Append(selected).Append(" selected)\n");

            if (quota == 0)
            {
                sb.Append("(items not listed)\n");
                continue;
            }

            int listed = Math.Min(n, Math.Min(n <= options.FullListThreshold ? n : options.SampleSize, quota));
            for (int i = 0; i < listed; i++)
            {
                var (item, isSelected) = list[i];
                var k = "i" + (++key);
                keyToItem[k] = item;
                sb.Append("- ").Append(k)
                  .Append(isSelected ? " [x] " : " [ ] ")
                  .Append(AiFormat.Bytes(item.SizeBytes)).Append(' ')
                  .Append(OneLine(sanitizer.Sanitize(item.Path)));
                var description = Truncate(OneLine(sanitizer.Sanitize(item.Description ?? "")));
                if (description.Length > 0) sb.Append(" — ").Append(description);
                sb.Append('\n');
            }
            quota -= listed;
            if (listed < n) sb.Append("(+").Append(n - listed).Append(" more, not listed)\n");
        }

        return new ScanDigest(moduleName, sb.ToString(), keyToItem, order);
    }

    private static string OneLine(string text) =>
        text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();

    private static string Truncate(string text) =>
        text.Length <= MaxDescriptionLength ? text : text[..MaxDescriptionLength] + "…";
}

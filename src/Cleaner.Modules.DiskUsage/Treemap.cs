namespace Cleaner.Modules.DiskUsage;

public readonly record struct TreemapRect(double X, double Y, double Width, double Height);

/// <summary>
/// Squarified treemap (Bruls, Huizing, van Wijk): jakaa suorakulmion osiin painojen suhteessa niin,
/// että osat ovat mahdollisimman neliömäisiä. Puhdas laskenta ilman UI-riippuvuuksia, jotta se on testattavissa.
/// </summary>
public static class Treemap
{
    /// <summary>
    /// Palauttaa yhden suorakulmion per paino samassa järjestyksessä. Painot annetaan laskevassa järjestyksessä,
    /// jotta tulos on neliömäisin. Nollapainot saavat nollakokoisen suorakulmion.
    /// </summary>
    public static List<TreemapRect> Squarify(IReadOnlyList<double> weights, double x, double y, double width, double height)
    {
        var result = new List<TreemapRect>(weights.Count);
        var total = weights.Sum();
        if (total <= 0 || width <= 0 || height <= 0) return result;

        var scale = width * height / total;
        var areas = weights.Select(w => Math.Max(0, w) * scale).ToList();

        double cx = x, cy = y, cw = width, ch = height;
        int start = 0;

        while (start < areas.Count)
        {
            // Rivi ladotaan lyhyemmän sivun suuntaan.
            bool verticalStrip = cw >= ch;
            double side = verticalStrip ? ch : cw;
            if (side <= 0) break;

            int end = start;
            double rowArea = 0;
            double bestWorst = double.MaxValue;
            while (end < areas.Count)
            {
                var candidate = rowArea + areas[end];
                var worst = WorstAspect(areas, start, end, candidate, side);
                if (worst > bestWorst && end > start) break;
                bestWorst = worst;
                rowArea = candidate;
                end++;
            }

            var thickness = rowArea / side;
            double offset = 0;
            for (int i = start; i < end; i++)
            {
                var length = thickness > 0 ? areas[i] / thickness : 0;
                result.Add(verticalStrip
                    ? new TreemapRect(cx, cy + offset, thickness, length)
                    : new TreemapRect(cx + offset, cy, length, thickness));
                offset += length;
            }

            if (verticalStrip) { cx += thickness; cw -= thickness; }
            else { cy += thickness; ch -= thickness; }
            start = end;
        }

        return result;
    }

    private static double WorstAspect(List<double> areas, int start, int endInclusive, double rowArea, double side)
    {
        double max = 0, min = double.MaxValue;
        for (int i = start; i <= endInclusive; i++)
        {
            max = Math.Max(max, areas[i]);
            min = Math.Min(min, areas[i]);
        }
        if (rowArea <= 0 || min <= 0) return double.MaxValue;
        var s2 = rowArea * rowArea;
        var w2 = side * side;
        return Math.Max(w2 * max / s2, s2 / (w2 * min));
    }
}

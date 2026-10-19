using Cleaner.Modules.DiskUsage;
using Xunit;

namespace Cleaner.Tests;

public sealed class TreemapTests
{
    [Fact]
    public void Areas_are_proportional_and_fill_the_bounds()
    {
        var weights = new double[] { 6, 6, 4, 3, 2, 2, 1 };
        var rects = Treemap.Squarify(weights, 10, 20, 600, 400);

        Assert.Equal(weights.Length, rects.Count);
        var total = weights.Sum();
        for (int i = 0; i < weights.Length; i++)
        {
            var expected = 600.0 * 400.0 * weights[i] / total;
            Assert.InRange(rects[i].Width * rects[i].Height, expected - 0.01, expected + 0.01);
            Assert.InRange(rects[i].X, 10 - 1e-6, 610 + 1e-6);
            Assert.InRange(rects[i].Y, 20 - 1e-6, 420 + 1e-6);
            Assert.True(rects[i].X + rects[i].Width <= 610 + 1e-6);
            Assert.True(rects[i].Y + rects[i].Height <= 420 + 1e-6);
        }
        Assert.InRange(rects.Sum(r => r.Width * r.Height), 240000 - 0.01, 240000 + 0.01);
    }

    [Fact]
    public void Rectangles_do_not_overlap()
    {
        var rects = Treemap.Squarify(new double[] { 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, 0, 0, 300, 200);

        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
            {
                var a = rects[i]; var b = rects[j];
                var overlapW = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X);
                var overlapH = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y);
                Assert.True(overlapW <= 1e-6 || overlapH <= 1e-6, $"Lohkot {i} ja {j} menevät päällekkäin.");
            }
    }

    [Fact]
    public void Squarified_is_more_square_than_a_single_strip()
    {
        var weights = Enumerable.Range(1, 8).Select(i => (double)(9 - i)).ToList();
        var rects = Treemap.Squarify(weights, 0, 0, 400, 300);

        // Yhden rivin latomuksessa pienin lohko olisi 400*300*(1/36) jaettuna 300 korkeudelle: leveys 11, korkeus 300 → suhde 27.
        var worst = rects.Max(r => Math.Max(r.Width / r.Height, r.Height / r.Width));
        Assert.True(worst < 4, $"Huonoin kuvasuhde {worst:F1} on liian suuri.");
    }

    [Fact]
    public void Degenerate_inputs_return_empty()
    {
        Assert.Empty(Treemap.Squarify(Array.Empty<double>(), 0, 0, 100, 100));
        Assert.Empty(Treemap.Squarify(new double[] { 0, 0 }, 0, 0, 100, 100));
        Assert.Empty(Treemap.Squarify(new double[] { 1 }, 0, 0, 0, 100));
    }

    [Fact]
    public void Combine_merges_roots_into_one_node()
    {
        var a = new DirectoryNode { Path = @"C:\" };
        var b = new DirectoryNode { Path = @"D:\" };
        var single = DirectoryNode.Combine(new[] { a }, "Kaikki");
        Assert.Same(a, single);

        var both = DirectoryNode.Combine(new[] { a, b }, "Kaikki");
        Assert.Equal("Kaikki", both.Path);
        Assert.Equal(2, both.Children.Count);
    }
}

using Cleaner.Ai;
using Cleaner.Core.Localization;
using Cleaner.Core.Models;
using Xunit;

namespace Cleaner.Tests;

/// <summary>
/// Testaa skannaustuloksen tiivistämistä mallille lähetettäväksi tekstiksi ja kielestä riippumatonta tavumuotoilua.
/// Kuuluu kielikokoelmaan, koska yksi testi vaihtaa <see cref="Loc.Current"/>-arvoa.
/// </summary>
[Collection(TestLanguage.Collection)]
public sealed class ScanDigestTests
{
    private static readonly PathSanitizer Sanitizer = new(@"C:\Users\ville", "ville");

    private static CleanItem Item(string category, string path, long size, RiskLevel risk = RiskLevel.Low, string description = "") =>
        new(path, "files", category, path, size, description, risk);

    private static ScanDigest Build(IEnumerable<(CleanItem, bool)> items, bool sumSizes = true, DigestOptions? options = null) =>
        ScanDigest.Build("File cleanup", items, sumSizes, options ?? new DigestOptions(), Sanitizer);

    private static IEnumerable<(CleanItem, bool)> Many(string category, int count, long baseSize = 1000) =>
        Enumerable.Range(1, count).Select(i => (Item(category, $@"C:\Temp\{category}\f{i:D3}.tmp", baseSize + i), true));

    private static string[] Lines(ScanDigest d) => d.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Text_starts_with_module_and_legend()
    {
        var d = Build(new[] { (Item("Temp", @"C:\Temp\a", 10), true) });
        var lines = Lines(d);
        Assert.Equal("# Module: File cleanup", lines[0]);
        Assert.Contains("[x] = currently selected for cleanup", lines[1]);
        Assert.Equal("File cleanup", d.ModuleName);
    }

    [Fact]
    public void Category_header_has_risk_count_size_and_selected()
    {
        var d = Build(new[]
        {
            (Item("Temp", @"C:\Temp\a", 1024), true),
            (Item("Temp", @"C:\Temp\b", 512, RiskLevel.Medium), false),
        });
        Assert.Contains("## Temp (risk Medium, 2 items, 1.5 KB, 1 selected)", d.Text);
    }

    [Fact]
    public void Unsummed_sizes_replace_the_total()
    {
        var d = Build(new[] { (Item("Folders", @"D:\Big", 5_000_000), false) }, sumSizes: false);
        Assert.Contains("## Folders (risk Low, 1 items, size not summed (nested items), 0 selected)", d.Text);
    }

    [Fact]
    public void Items_are_sorted_by_size_then_path_and_keys_run_across_categories()
    {
        var d = Build(new[]
        {
            (Item("A", @"C:\a\small", 10), true),
            (Item("B", @"C:\b\one", 5), false),
            (Item("A", @"C:\a\big", 1000), true),
            (Item("A", @"C:\a\big2", 1000), true),
        });
        var lines = Lines(d).Where(l => l.StartsWith("- ")).ToArray();
        Assert.Equal(@"- i1 [x] 1000 B C:\a\big", lines[0]);
        Assert.Equal(@"- i2 [x] 1000 B C:\a\big2", lines[1]);
        Assert.Equal(@"- i3 [x] 10 B C:\a\small", lines[2]);
        Assert.Equal(@"- i4 [ ] 5 B C:\b\one", lines[3]);
        Assert.Equal(new[] { "A", "B" }, d.Categories);
        Assert.Equal(4, d.KeyToItem.Count);
        Assert.Equal(@"C:\b\one", d.KeyToItem["i4"].Id);
    }

    [Fact]
    public void Category_with_threshold_items_is_listed_in_full()
    {
        var d = Build(Many("Temp", 40));
        Assert.Equal(40, d.KeyToItem.Count);
        Assert.DoesNotContain("more, not listed", d.Text);
    }

    [Fact]
    public void Larger_category_is_sampled()
    {
        var d = Build(Many("Temp", 41));
        Assert.Equal(20, d.KeyToItem.Count);
        Assert.Contains("(+21 more, not listed)", d.Text);
        Assert.Contains("## Temp (risk Low, 41 items,", d.Text);
        // Otos on suurimmat: f041 on suurin.
        Assert.EndsWith(@"f041.tmp", d.KeyToItem["i1"].Path);
    }

    [Fact]
    public void Max_listed_items_cuts_a_category_and_skips_the_rest()
    {
        var options = new DigestOptions { MaxListedItems = 50 };
        var d = Build(Many("A", 30).Concat(Many("B", 30)).Concat(Many("C", 5)), options: options);

        Assert.Equal(50, d.KeyToItem.Count);
        Assert.Contains("(+10 more, not listed)", d.Text);   // B: 20 listattu, 10 jäi pois
        var cIndex = d.Text.IndexOf("## C ", StringComparison.Ordinal);
        Assert.True(cIndex > 0);
        Assert.Contains("(items not listed)", d.Text[cIndex..]);
        Assert.Equal(new[] { "A", "B", "C" }, d.Categories);
        Assert.Equal(Enumerable.Range(1, 50).Select(i => "i" + i).ToHashSet(), d.KeyToItem.Keys.ToHashSet());
    }

    [Fact]
    public void Description_is_sanitized_and_truncated()
    {
        var longText = @"Left over by C:\Users\ville\AppData\Local\Foo " + new string('x', 200);
        var d = Build(new[] { (Item("Temp", @"C:\Users\ville\AppData\Local\Temp\x.tmp", 1, description: longText), true) });
        var line = Lines(d).Single(l => l.StartsWith("- i1"));

        Assert.StartsWith(@"- i1 [x] 1 B %USERPROFILE%\AppData\Local\Temp\x.tmp — Left over by %USERPROFILE%\AppData", line);
        Assert.EndsWith("…", line);
        var description = line[(line.IndexOf(" — ", StringComparison.Ordinal) + 3)..];
        Assert.Equal(121, description.Length);
        Assert.DoesNotContain("ville", d.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Empty_description_is_left_out()
    {
        var d = Build(new[] { (Item("Temp", @"C:\Temp\a", 1), true) });
        Assert.Contains("- i1 [x] 1 B C:\\Temp\\a\n", d.Text);
    }

    [Fact]
    public void Category_without_selected_items_is_included()
    {
        var d = Build(new[] { (Item("Temp", @"C:\Temp\a", 1), false) });
        Assert.Contains("## Temp (risk Low, 1 items, 1 B, 0 selected)", d.Text);
        Assert.Contains("- i1 [ ]", d.Text);
    }

    [Fact]
    public void Same_input_gives_same_text_in_both_languages()
    {
        var items = Many("Temp", 45, baseSize: 1_500_000).Append((Item("Big", @"C:\x", 3_000_000_000), false)).ToList();
        var previous = Loc.Current;
        try
        {
            Loc.Current = Language.Fi;
            var fi = Build(items).Text;
            Loc.Current = Language.En;
            var en = Build(items).Text;
            Assert.Equal(en, fi);
            Assert.Contains("2.8 GB", en);
            Assert.DoesNotContain("Mt", en);
        }
        finally
        {
            Loc.Current = previous;
        }
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(-5, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(64_906_445, "61.9 MB")]
    [InlineData(1_099_511_627_776, "1.0 TB")]
    public void AiFormat_bytes_is_invariant(long bytes, string expected)
    {
        var previous = Loc.Current;
        try
        {
            Loc.Current = Language.Fi;
            Assert.Equal(expected, AiFormat.Bytes(bytes));
        }
        finally
        {
            Loc.Current = previous;
        }
    }
}

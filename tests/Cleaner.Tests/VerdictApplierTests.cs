using Cleaner.Ai;
using Cleaner.Core.Models;
using Xunit;

namespace Cleaner.Tests;

/// <summary>Testaa arvioiden periytymistä ja Auto-tilan valintasääntöä.</summary>
public sealed class VerdictApplierTests
{
    private static readonly CleanItem A = new("id-a", "files", "Temp", @"C:\Temp\a", 1, "", RiskLevel.Low);
    private static readonly CleanItem B = new("id-b", "files", "Temp", @"C:\Temp\b", 1, "", RiskLevel.Low);
    private static readonly CleanItem C = new("id-c", "files", "Logs", @"C:\Logs\c", 1, "", RiskLevel.Low);

    private static readonly AiReview Review = new("s",
        new[] { new AiCategoryVerdict("Temp", AiVerdict.Safe, "r") },
        new[] { new AiItemVerdict("id-a", AiVerdict.Keep, "r") },
        new AiUsage(0, 0));

    [Fact]
    public void Item_verdict_wins_over_category()
    {
        Assert.Equal(AiVerdict.Keep, VerdictApplier.Resolve(Review, A));
    }

    [Fact]
    public void Category_verdict_applies_without_item_verdict()
    {
        Assert.Equal(AiVerdict.Safe, VerdictApplier.Resolve(Review, B));
    }

    [Fact]
    public void No_verdict_gives_null()
    {
        Assert.Null(VerdictApplier.Resolve(Review, C));
    }

    [Theory]
    [InlineData(AiVerdict.Safe, RiskLevel.Low, false, true)]
    [InlineData(AiVerdict.Safe, RiskLevel.Medium, false, true)]
    [InlineData(AiVerdict.Caution, RiskLevel.Low, true, false)]
    [InlineData(AiVerdict.Keep, RiskLevel.Low, true, false)]
    [InlineData(AiVerdict.Safe, RiskLevel.High, true, false)]   // High ei koskaan valittuna, vaikka arvio on Safe
    [InlineData(AiVerdict.Keep, RiskLevel.High, true, false)]
    [InlineData(null, RiskLevel.High, true, false)]
    [InlineData(null, RiskLevel.Low, true, true)]
    [InlineData(null, RiskLevel.Medium, false, false)]
    public void AutoSelection_covers_every_branch(AiVerdict? verdict, RiskLevel risk, bool current, bool expected)
    {
        Assert.Equal(expected, VerdictApplier.AutoSelection(verdict, risk, current));
    }
}

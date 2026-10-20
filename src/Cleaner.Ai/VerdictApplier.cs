using Cleaner.Core.Models;

namespace Cleaner.Ai;

/// <summary>Arvioiden soveltamissäännöt: periytyminen kategoriasta ja Auto-tilan valinta.</summary>
public static class VerdictApplier
{
    /// <summary>Kohteen oma arvio voittaa, sitten kategorian arvio, muuten null.</summary>
    public static AiVerdict? Resolve(AiReview review, CleanItem item)
    {
        foreach (var v in review.Items)
            if (string.Equals(v.ItemId, item.Id, StringComparison.Ordinal)) return v.Verdict;
        foreach (var c in review.Categories)
            if (string.Equals(c.Category, item.Category, StringComparison.Ordinal)) return c.Verdict;
        return null;
    }

    /// <summary>
    /// Auto-tilan valinta: High-riski → false aina; Caution tai Keep → false; Safe → true; ei arviota → <paramref name="current"/>.
    /// </summary>
    public static bool? AutoSelection(AiVerdict? verdict, RiskLevel risk, bool current)
    {
        if (risk == RiskLevel.High) return false;
        return verdict switch
        {
            AiVerdict.Caution or AiVerdict.Keep => false,
            AiVerdict.Safe => true,
            _ => current
        };
    }
}

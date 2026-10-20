using Cleaner.Ai;
using Cleaner.Core.Localization;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cleaner.UI.ViewModels;

/// <summary>Yksi löydös valintaruudulla.</summary>
public sealed partial class ItemViewModel : ObservableObject
{
    public CleanItem Item { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public ItemViewModel(CleanItem item, bool selected)
    {
        Item = item;
        IsSelected = selected;
    }

    public string Path => Item.Path;
    public long SizeBytes => Item.SizeBytes;
    public string SizeText => Item.SizeBytes > 0 ? ByteFormatter.Format(Item.SizeBytes) : "";
    public string Description => Item.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Item.Description);
    public RiskLevel Risk => Item.Risk;

    // ---------- Avustajan merkki (REQ-17) ----------

    /// <summary>Avustajan oma arvio tästä kohteesta (ei kategoriasta periytyvä), muuten null.</summary>
    public AiVerdict? Verdict { get; private set; }

    /// <summary>Avustajan perustelu; näytetään merkin ToolTipissä.</summary>
    public string VerdictReason { get; private set; } = "";

    /// <summary>Merkin teksti nykyisellä kielellä.</summary>
    public string VerdictText => VerdictTextFor(Verdict);

    public bool HasVerdict => Verdict is not null;
    public bool IsSafeVerdict => Verdict == AiVerdict.Safe;
    public bool IsCautionVerdict => Verdict == AiVerdict.Caution;
    public bool IsKeepVerdict => Verdict == AiVerdict.Keep;

    public void SetVerdict(AiVerdict? verdict, string reason)
    {
        Verdict = verdict;
        VerdictReason = verdict is null ? "" : reason;
        OnPropertyChanged(nameof(Verdict));
        OnPropertyChanged(nameof(VerdictReason));
        OnPropertyChanged(nameof(VerdictText));
        OnPropertyChanged(nameof(HasVerdict));
        OnPropertyChanged(nameof(IsSafeVerdict));
        OnPropertyChanged(nameof(IsCautionVerdict));
        OnPropertyChanged(nameof(IsKeepVerdict));
    }

    /// <summary>Kielen vaihto: merkin teksti luetaan uudelleen. Perustelu jää syntykieleensä.</summary>
    public void RefreshLanguage() => OnPropertyChanged(nameof(VerdictText));

    /// <summary>Arvion nimi nykyisellä kielellä, null → "". Yhteinen kohteille, kategorioille ja paneelin viesteille.</summary>
    public static string VerdictTextFor(AiVerdict? verdict) => verdict switch
    {
        AiVerdict.Safe => Loc.T("safe", "turvallinen"),
        AiVerdict.Caution => Loc.T("caution", "varo"),
        AiVerdict.Keep => Loc.T("keep", "säilytä"),
        _ => ""
    };
}

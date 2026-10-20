namespace Cleaner.Ai;

/// <summary>Avustajan arvio kohteesta tai kategoriasta.</summary>
public enum AiVerdict
{
    /// <summary>Voi poistaa ilman tietojen menetystä.</summary>
    Safe,

    /// <summary>Luultavasti turvallinen, mutta käyttäjän kannattaa tarkistaa.</summary>
    Caution,

    /// <summary>Ei saa poistaa.</summary>
    Keep
}

/// <summary>Keskusteluviestin lähettäjä.</summary>
public enum AiRole { User, Assistant }

/// <summary>Mallin ajattelun määrä (API:n <c>effort</c>).</summary>
public enum AiEffort { Low, Medium, High }

/// <summary>Virheen tyyppi; käyttöliittymä lokalisoi tekstin tämän mukaan.</summary>
public enum AiErrorKind { MissingKey, Unauthorized, RateLimited, Network, Server, BadRequest, Refused, InvalidResponse, Other }

/// <summary>Kutsun token-määrät.</summary>
public sealed record AiUsage(long InputTokens, long OutputTokens);

/// <summary>Yksi viesti keskusteluhistoriassa.</summary>
public sealed record AiChatMessage(AiRole Role, string Text);

/// <summary>Kategorian arvio. <paramref name="Category"/> on <see cref="Cleaner.Core.Models.CleanItem.Category"/>.</summary>
public sealed record AiCategoryVerdict(string Category, AiVerdict Verdict, string Reason);

/// <summary>Kohteen arvio. <paramref name="ItemId"/> on <see cref="Cleaner.Core.Models.CleanItem.Id"/>.</summary>
public sealed record AiItemVerdict(string ItemId, AiVerdict Verdict, string Reason);

/// <summary>Jäsennetty arvio yhden moduulin skannaustuloksista.</summary>
public sealed record AiReview(string Summary, IReadOnlyList<AiCategoryVerdict> Categories, IReadOnlyList<AiItemVerdict> Items, AiUsage Usage);

/// <summary>Taustapalvelun vastaus. <paramref name="StopReason"/> ilman lainausmerkkejä, esim. "end_turn".</summary>
public sealed record AiTextResult(string Text, string StopReason, AiUsage Usage, bool Truncated);

/// <summary>Yhden kutsun asetukset.</summary>
public sealed record AiRequestOptions(string Model, AiEffort Effort, int MaxTokens);

/// <summary>
/// Moduulin skannaustuloksen yhteenveto koneen kunnon arviota varten. <paramref name="SizesSummable"/> on false
/// opt-in-moduuleilla, joiden kohteet voivat olla sisäkkäisiä kansioita (summa olisi harhaanjohtava).
/// </summary>
public sealed record ModuleScanSummary(string ModuleId, string DisplayName, bool Scanned, int Count, long TotalBytes,
    bool SizesSummable, IReadOnlyList<(string Name, int Count, long Bytes)> Categories);

/// <summary>Kiinteä asema.</summary>
public sealed record DriveFacts(string Name, string? Label, long TotalBytes, long FreeBytes);

/// <summary>Koneen tiedot koneen kunnon arviota varten. Puhdasta dataa; <see cref="SystemFactsCollector"/> lukee ne.</summary>
public sealed record SystemFacts(string OsVersion, int ProcessorCount, long? TotalMemoryBytes, long? AvailableMemoryBytes,
    TimeSpan Uptime, IReadOnlyList<DriveFacts> Drives, bool IsElevated);

/// <summary><see cref="AiAssistant"/>-olion asetukset.</summary>
public sealed class AiAssistantOptions
{
    /// <summary>Mallin tunnus sellaisenaan, ilman päivämääräliitettä.</summary>
    public string Model { get; init; } = "claude-opus-5-5";

    /// <summary>Historian viestien enimmäismäärä.</summary>
    public int MaxHistory { get; init; } = 20;

    /// <summary>Historian tekstien yhteispituuden enimmäismäärä merkkeinä.</summary>
    public int MaxHistoryChars { get; init; } = 120_000;
}

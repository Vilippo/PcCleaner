using System.Text.Json;

namespace Cleaner.Ai;

/// <summary>
/// Kielimallin taustapalvelu. Toteutus heittää vain <see cref="AiException"/>- tai
/// <see cref="OperationCanceledException"/>-poikkeuksia.
/// </summary>
public interface IAiBackend
{
    /// <summary>JSON-skeeman mukainen vastaus. Text on skeeman mukainen JSON-teksti. Heittää AiException-poikkeuksen.</summary>
    Task<AiTextResult> CompleteJsonAsync(string system, IReadOnlyList<AiChatMessage> messages, JsonElement schema,
        AiRequestOptions options, CancellationToken ct);

    /// <summary>
    /// Suoratoistettu tekstivastaus: onDelta saa jokaisen tekstipalan (voi tulla taustasäikeestä).
    /// Palauttaa kokonaisen tekstin.
    /// </summary>
    Task<AiTextResult> StreamTextAsync(string system, IReadOnlyList<AiChatMessage> messages, Action<string> onDelta,
        AiRequestOptions options, CancellationToken ct);
}

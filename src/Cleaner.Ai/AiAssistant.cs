using Cleaner.Core.Localization;

namespace Cleaner.Ai;

/// <summary>
/// Keskustelu avustajan kanssa: arvio, koneen kunto ja vapaat kysymykset samassa historiassa. Historia pysyy
/// käyttäjä–avustaja-pareina: epäonnistunut tai peruttu kutsu poistaa oman käyttäjäviestinsä. Koko rajataan
/// kahdella säännöllä: vain uusimman arvion digest on kokonaisena, ja vanhimmat parit poistetaan, kun
/// <see cref="AiAssistantOptions.MaxHistory"/> tai <see cref="AiAssistantOptions.MaxHistoryChars"/> ylittyy.
/// Kutsuja ei saa ajaa kahta yhtä aikaa. Awaitit jatkavat kutsujan kontekstissa (ei ConfigureAwait(false)),
/// joten historiaa muutetaan UI-säikeessä, kun kutsu tehdään sieltä.
/// </summary>
public sealed class AiAssistant
{
    private const int ReviewMaxTokens = 16000;
    private const int TextMaxTokens = 8000;

    private readonly IAiBackend _backend;
    private readonly AiAssistantOptions _options;
    private readonly List<Entry> _entries = new();

    public AiAssistant(IAiBackend backend, AiAssistantOptions options)
    {
        _backend = backend;
        _options = options;
    }

    public IReadOnlyList<AiChatMessage> History => _entries.Select(e => e.Message).ToList();

    /// <summary>Pyytää arvion digestistä JSON-skeeman mukaan ja jäsentää sen.</summary>
    public async Task<AiReview> ReviewAsync(ScanDigest digest, Language language, CancellationToken ct)
    {
        var instruction = AiPrompts.ReviewInstruction(language);
        var user = new Entry(new AiChatMessage(AiRole.User, instruction + "\n\n" + digest.Text))
        {
            ReviewModule = digest.ModuleName,
            ReviewInstruction = instruction
        };
        _entries.Add(user);

        AiReview review;
        AiTextResult result;
        try
        {
            result = await _backend.CompleteJsonAsync(AiPrompts.System(language), Snapshot(), AiReviewSchema.Json,
                new AiRequestOptions(_options.Model, AiEffort.High, ReviewMaxTokens), ct);
            // Jäsennys ennen avustajan viestin lisäystä: kelvoton vastaus (myös katkennut JSON) ei jää historiaan.
            review = AiReviewParser.Parse(result.Text, result.Usage, digest);
        }
        catch
        {
            _entries.Remove(user);
            throw;
        }

        AddAnswer(user, result.Text);
        return review;
    }

    /// <summary>Koneen kunnon arvio suoratoistettuna.</summary>
    public Task<AiTextResult> HealthCheckAsync(string healthText, Language language, Action<string> onDelta, CancellationToken ct) =>
        StreamAsync(AiPrompts.HealthInstruction(language) + "\n\n" + healthText, language, onDelta, ct);

    /// <summary>Vapaa kysymys suoratoistettuna.</summary>
    public Task<AiTextResult> AskAsync(string question, Language language, Action<string> onDelta, CancellationToken ct) =>
        StreamAsync(question, language, onDelta, ct);

    /// <summary>Tyhjentää historian.</summary>
    public void Reset() => _entries.Clear();

    private async Task<AiTextResult> StreamAsync(string userText, Language language, Action<string> onDelta, CancellationToken ct)
    {
        var user = new Entry(new AiChatMessage(AiRole.User, userText));
        _entries.Add(user);

        AiTextResult result;
        try
        {
            result = await _backend.StreamTextAsync(AiPrompts.System(language), Snapshot(), onDelta,
                new AiRequestOptions(_options.Model, AiEffort.Medium, TextMaxTokens), ct);
        }
        catch
        {
            _entries.Remove(user);
            throw;
        }

        AddAnswer(user, result.Text);
        return result;
    }

    /// <summary>
    /// Lisää vastauksen kysymyksen pariksi. Jos <see cref="Reset"/> tyhjensi historian kesken kutsun, kysymystä ei
    /// enää ole, eikä vastausta lisätä: yksinäinen avustajan viesti historian alussa rikkoisi seuraavan pyynnön.
    /// </summary>
    private void AddAnswer(Entry user, string text)
    {
        if (!_entries.Contains(user)) return;
        _entries.Add(new Entry(new AiChatMessage(AiRole.Assistant, text)));
        if (user.ReviewModule is not null) OmitEarlierDigests(user);
        Trim();
    }

    private List<AiChatMessage> Snapshot() => _entries.Select(e => e.Message).ToList();

    /// <summary>Korvaa aiempien arvioiden digestit lyhyellä merkinnällä; vain <paramref name="newest"/> jää kokonaiseksi.</summary>
    private void OmitEarlierDigests(Entry newest)
    {
        foreach (var e in _entries)
        {
            if (ReferenceEquals(e, newest) || e.ReviewModule is null || e.DigestOmitted) continue;
            e.Message = new AiChatMessage(AiRole.User,
                e.ReviewInstruction + "\n\n(earlier review of " + e.ReviewModule + "; item list omitted)");
            e.DigestOmitted = true;
        }
    }

    /// <summary>Poistaa vanhimpia pareja, kunnes molemmat rajat täyttyvät tai jäljellä on vain uusin pari.</summary>
    private void Trim()
    {
        while (_entries.Count > 2 &&
               (_entries.Count > _options.MaxHistory || _entries.Sum(e => e.Message.Text.Length) > _options.MaxHistoryChars))
        {
            _entries.RemoveRange(0, 2);
        }
    }

    private sealed class Entry(AiChatMessage message)
    {
        public AiChatMessage Message { get; set; } = message;

        /// <summary>Arvion käyttäjäviestissä moduulin nimi, muuten null.</summary>
        public string? ReviewModule { get; init; }

        public string ReviewInstruction { get; init; } = "";

        public bool DigestOmitted { get; set; }
    }
}

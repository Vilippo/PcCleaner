using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;

namespace Cleaner.Ai;

/// <summary>
/// <see cref="IAiBackend"/> Anthropicin Messages-rajapinnan kautta. API-avain luetaan vain ympäristömuuttujasta
/// <c>ANTHROPIC_API_KEY</c> (SDK:n oletus); sitä ei lueta muualta eikä kirjata minnekään. Kaikki SDK:n poikkeukset
/// kääritään <see cref="AiException"/>-olioiksi; <see cref="OperationCanceledException"/> kulkee läpi sellaisenaan.
/// Viestit ovat englanniksi, käyttöliittymä lokalisoi tyypin mukaan.
/// </summary>
public sealed class AnthropicBackend : IAiBackend
{
    private const string KeyVariable = "ANTHROPIC_API_KEY";

    private readonly AnthropicClient _client;

    /// <summary>Tosi täsmälleen silloin, kun ympäristömuuttuja ANTHROPIC_API_KEY ei ole tyhjä. Ei tee API-kutsua.</summary>
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(KeyVariable));

    /// <exception cref="AiException"><see cref="AiErrorKind.MissingKey"/>, jos avainta ei ole.</exception>
    public AnthropicBackend()
    {
        if (!IsConfigured)
            throw new AiException(AiErrorKind.MissingKey, "The environment variable ANTHROPIC_API_KEY is not set.");
        try
        {
            _client = new AnthropicClient();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Map(ex);
        }
    }

    public async Task<AiTextResult> CompleteJsonAsync(string system, IReadOnlyList<AiChatMessage> messages, JsonElement schema,
        AiRequestOptions options, CancellationToken ct)
    {
        try
        {
            var parameters = Parameters(system, messages, options, new JsonOutputFormat { Schema = ToDictionary(schema) });
            var response = await _client.Messages.Create(parameters, ct);

            var stopReason = Raw(response.StopReason);
            if (stopReason == "refusal")
                throw new AiException(AiErrorKind.Refused, "The model declined to answer.",
                    category: NullIfEmpty(Raw(response.StopDetails?.Category)));

            var text = "";
            foreach (var block in response.Content)
            {
                if (block.TryPickText(out var t))
                {
                    text = t.Text;
                    break;
                }
            }

            return new AiTextResult(text, stopReason,
                new AiUsage(response.Usage.InputTokens, response.Usage.OutputTokens), stopReason == "max_tokens");
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw TimedOut(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not AiException)
        {
            throw Map(ex);
        }
    }

    public async Task<AiTextResult> StreamTextAsync(string system, IReadOnlyList<AiChatMessage> messages, Action<string> onDelta,
        AiRequestOptions options, CancellationToken ct)
    {
        var text = new StringBuilder();
        long inputTokens = 0, outputTokens = 0;
        string stopReason = "";
        string? refusalCategory = null;

        try
        {
            var parameters = Parameters(system, messages, options, format: null);
            await foreach (RawMessageStreamEvent ev in _client.Messages.CreateStreaming(parameters, ct).WithCancellation(ct))
            {
                if (ev.TryPickStart(out var start))
                {
                    inputTokens = start.Message.Usage.InputTokens;
                }
                else if (ev.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var t))
                {
                    text.Append(t.Text);
                    onDelta(t.Text);
                }
                else if (ev.TryPickDelta(out var messageDelta))
                {
                    outputTokens = messageDelta.Usage.OutputTokens;
                    var reason = Raw(messageDelta.Delta.StopReason);
                    if (reason.Length > 0) stopReason = reason;
                    var category = NullIfEmpty(Raw(messageDelta.Delta.StopDetails?.Category));
                    if (category is not null) refusalCategory = category;
                }
            }
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw TimedOut(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not AiException)
        {
            throw Map(ex);
        }

        if (stopReason == "refusal")
            throw new AiException(AiErrorKind.Refused, "The model declined to answer.", category: refusalCategory);

        return new AiTextResult(text.ToString(), stopReason, new AiUsage(inputTokens, outputTokens), stopReason == "max_tokens");
    }

    /// <summary>Thinking-parametria ei aseteta (adaptiivinen oletus).</summary>
    private static MessageCreateParams Parameters(string system, IReadOnlyList<AiChatMessage> messages, AiRequestOptions options,
        JsonOutputFormat? format) => new()
    {
        Model = options.Model,
        MaxTokens = options.MaxTokens,
        System = new List<TextBlockParam> { new() { Text = system } },
        OutputConfig = format is null
            ? new OutputConfig { Effort = Effort(options.Effort) }
            : new OutputConfig { Effort = Effort(options.Effort), Format = format },
        Messages = messages
            .Select(m => new MessageParam { Role = m.Role == AiRole.Assistant ? Role.Assistant : Role.User, Content = m.Text })
            .ToList()
    };

    private static Effort Effort(AiEffort effort) => effort switch
    {
        AiEffort.Low => Anthropic.Models.Messages.Effort.Low,
        AiEffort.High => Anthropic.Models.Messages.Effort.High,
        _ => Anthropic.Models.Messages.Effort.Medium
    };

    private static Dictionary<string, JsonElement> ToDictionary(JsonElement schema)
    {
        var dict = new Dictionary<string, JsonElement>();
        foreach (var prop in schema.EnumerateObject()) dict[prop.Name] = prop.Value.Clone();
        return dict;
    }

    /// <summary>SDK:n enum-arvon ToString() palauttaa lainausmerkit mukaan lukien ("end_turn"); ne poistetaan.</summary>
    private static string Raw(object? value) => value?.ToString()?.Trim('"') ?? "";

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    /// <summary>
    /// SDK:n aikakatkaisu (ClientOptions.Timeout) tulee TaskCanceledException-poikkeuksena, vaikka kutsuja ei perunut.
    /// Se on verkkovirhe, ei käyttäjän peruutus.
    /// </summary>
    private static AiException TimedOut(OperationCanceledException ex) =>
        new(AiErrorKind.Network, "The request timed out.", ex);

    /// <summary>SDK:n poikkeus →<see cref="AiException"/>, tarkimmasta yleisimpään. Alkuperäinen on InnerException.</summary>
    private static AiException Map(Exception ex) => ex switch
    {
        AnthropicUnauthorizedException => new(AiErrorKind.Unauthorized, ex.Message, ex),
        AnthropicRateLimitException => new(AiErrorKind.RateLimited, ex.Message, ex),
        Anthropic4xxException => new(AiErrorKind.BadRequest, ex.Message, ex),
        Anthropic5xxException => new(AiErrorKind.Server, ex.Message, ex),
        AnthropicSseException => new(AiErrorKind.Server, ex.Message, ex),
        AnthropicIOException => new(AiErrorKind.Network, ex.Message, ex),
        AnthropicInvalidDataException => new(AiErrorKind.InvalidResponse, ex.Message, ex),
        AnthropicException => new(AiErrorKind.Other, ex.Message, ex),
        _ => new(AiErrorKind.Other, ex.Message, ex)
    };
}

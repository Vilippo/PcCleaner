using System.Text.Json;

namespace Cleaner.Ai;

/// <summary>
/// Jäsentää <see cref="AiReviewSchema"/>-muotoisen vastauksen. Tuntematon avain, tuntematon kategoria ja tuntematon
/// verdict pudotetaan hiljaa; rikkinäinen JSON tai puuttuva pakollinen kenttä heittää
/// <see cref="AiException"/>-poikkeuksen tyypillä <see cref="AiErrorKind.InvalidResponse"/>.
/// </summary>
public static class AiReviewParser
{
    public static AiReview Parse(string json, AiUsage usage, ScanDigest digest)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Invalid("the answer is not a JSON object");

            var summary = RequiredString(root, "summary");
            var categoriesElement = Required(root, "categories", JsonValueKind.Array);
            var itemsElement = Required(root, "items", JsonValueKind.Array);

            var known = new HashSet<string>(digest.Categories, StringComparer.Ordinal);
            var categories = new List<AiCategoryVerdict>();
            var seenCategories = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in categoriesElement.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Object) throw Invalid("a category entry is not an object");
                var name = RequiredString(c, "category");
                var verdictText = RequiredString(c, "verdict");
                var reason = RequiredString(c, "reason");
                if (!known.Contains(name) || !TryVerdict(verdictText, out var verdict)) continue;
                if (!seenCategories.Add(name)) continue;   // sama kategoria kahdesti: ensimmäinen voittaa
                categories.Add(new AiCategoryVerdict(name, verdict, reason));
            }

            var items = new List<AiItemVerdict>();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var i in itemsElement.EnumerateArray())
            {
                if (i.ValueKind != JsonValueKind.Object) throw Invalid("an item entry is not an object");
                var key = RequiredString(i, "key");
                var verdictText = RequiredString(i, "verdict");
                var reason = RequiredString(i, "reason");
                if (!digest.KeyToItem.TryGetValue(key, out var item) || !TryVerdict(verdictText, out var verdict)) continue;
                if (!seenKeys.Add(key)) continue;   // sama avain kahdesti: ensimmäinen voittaa
                items.Add(new AiItemVerdict(item.Id, verdict, reason));
            }

            return new AiReview(summary, categories, items, usage);
        }
        catch (JsonException ex)
        {
            throw new AiException(AiErrorKind.InvalidResponse, "The review answer is not valid JSON: " + ex.Message, ex);
        }
    }

    // Ei Enum.TryParse: se hyväksyisi myös numerot ("1" → Caution).
    private static bool TryVerdict(string text, out AiVerdict verdict)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "safe": verdict = AiVerdict.Safe; return true;
            case "caution": verdict = AiVerdict.Caution; return true;
            case "keep": verdict = AiVerdict.Keep; return true;
            default: verdict = default; return false;
        }
    }

    private static JsonElement Required(JsonElement obj, string name, JsonValueKind kind)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind != kind)
            throw Invalid($"the field '{name}' is missing or has the wrong type");
        return value;
    }

    private static string RequiredString(JsonElement obj, string name) =>
        Required(obj, name, JsonValueKind.String).GetString() ?? "";

    private static AiException Invalid(string reason) =>
        new(AiErrorKind.InvalidResponse, "The review answer could not be interpreted: " + reason + ".");
}

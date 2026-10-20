using System.Text.Json;

namespace Cleaner.Ai;

/// <summary>
/// Arvion JSON-skeema (structured outputs). Kaikki kentät pakollisia, <c>additionalProperties: false</c> joka tasolla,
/// <c>verdict</c> on <c>safe | caution | keep</c>. Muoto on todennettu API:a vasten.
/// </summary>
public static class AiReviewSchema
{
    public static JsonElement Json { get; } = Create();

    private static JsonElement Create()
    {
        var verdict = new { type = "string", @enum = new[] { "safe", "caution", "keep" } };
        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new Dictionary<string, object>
            {
                ["summary"] = new { type = "string" },
                ["categories"] = new
                {
                    type = "array",
                    items = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["category"] = new { type = "string" },
                            ["verdict"] = verdict,
                            ["reason"] = new { type = "string" }
                        },
                        ["required"] = new[] { "category", "verdict", "reason" }
                    }
                },
                ["items"] = new
                {
                    type = "array",
                    items = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["key"] = new { type = "string" },
                            ["verdict"] = verdict,
                            ["reason"] = new { type = "string" }
                        },
                        ["required"] = new[] { "key", "verdict", "reason" }
                    }
                }
            },
            ["required"] = new[] { "summary", "categories", "items" }
        };
        return JsonSerializer.SerializeToElement(schema);
    }
}

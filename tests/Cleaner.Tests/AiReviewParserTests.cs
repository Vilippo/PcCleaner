using System.Text.Json;
using Cleaner.Ai;
using Cleaner.Core.Models;
using Xunit;

namespace Cleaner.Tests;

/// <summary>Testaa arvion JSON-skeemaa ja vastauksen jäsennystä.</summary>
public sealed class AiReviewParserTests
{
    private static readonly AiUsage Usage = new(100, 20);

    private static ScanDigest Digest() => ScanDigest.Build("File cleanup", new[]
    {
        (new CleanItem("id-a", "files", "Temp", @"C:\Temp\a", 200, "", RiskLevel.Low), true),
        (new CleanItem("id-b", "files", "Logs", @"C:\Logs\b", 100, "", RiskLevel.Medium), false),
    }, true, new DigestOptions(), new PathSanitizer(@"C:\Users\x", "x"));

    [Fact]
    public void Valid_answer_is_parsed()
    {
        const string json = """
            { "summary": "Mostly safe.",
              "categories": [ { "category": "Temp", "verdict": "safe", "reason": "Temporary files." },
                              { "category": "Logs", "verdict": "caution", "reason": "May be needed." } ],
              "items": [ { "key": "i2", "verdict": "keep", "reason": "Recent log." } ] }
            """;
        var review = AiReviewParser.Parse(json, Usage, Digest());

        Assert.Equal("Mostly safe.", review.Summary);
        Assert.Equal(new AiCategoryVerdict("Temp", AiVerdict.Safe, "Temporary files."), review.Categories[0]);
        Assert.Equal(new AiCategoryVerdict("Logs", AiVerdict.Caution, "May be needed."), review.Categories[1]);
        Assert.Equal(new AiItemVerdict("id-b", AiVerdict.Keep, "Recent log."), Assert.Single(review.Items));
        Assert.Same(Usage, review.Usage);
    }

    [Fact]
    public void Unknown_key_and_unknown_category_are_dropped()
    {
        const string json = """
            { "summary": "s",
              "categories": [ { "category": "Nope", "verdict": "safe", "reason": "r" },
                              { "category": "temp", "verdict": "safe", "reason": "case differs" },
                              { "category": "Temp", "verdict": "keep", "reason": "r" } ],
              "items": [ { "key": "i9", "verdict": "safe", "reason": "r" },
                         { "key": "i1", "verdict": "caution", "reason": "r" } ] }
            """;
        var review = AiReviewParser.Parse(json, Usage, Digest());

        Assert.Equal("Temp", Assert.Single(review.Categories).Category);
        Assert.Equal("id-a", Assert.Single(review.Items).ItemId);
    }

    [Fact]
    public void Verdict_ignores_case_and_unknown_verdict_is_dropped()
    {
        const string json = """
            { "summary": "s",
              "categories": [ { "category": "Temp", "verdict": "SAFE", "reason": "r" },
                              { "category": "Logs", "verdict": "maybe", "reason": "r" } ],
              "items": [ { "key": "i1", "verdict": "Caution", "reason": "r" },
                         { "key": "i2", "verdict": "1", "reason": "numbers are not verdicts" } ] }
            """;
        var review = AiReviewParser.Parse(json, Usage, Digest());

        Assert.Equal(AiVerdict.Safe, Assert.Single(review.Categories).Verdict);
        Assert.Equal(AiVerdict.Caution, Assert.Single(review.Items).Verdict);
    }

    [Fact]
    public void Duplicate_key_keeps_the_first()
    {
        const string json = """
            { "summary": "s", "categories": [],
              "items": [ { "key": "i1", "verdict": "keep", "reason": "first" },
                         { "key": "i1", "verdict": "safe", "reason": "second" } ] }
            """;
        Assert.Equal("first", Assert.Single(AiReviewParser.Parse(json, Usage, Digest()).Items).Reason);
    }

    [Theory]
    [InlineData("{ \"summary\": \"s\", \"categories\": [")]   // katkennut (max_tokens)
    [InlineData("not json")]
    [InlineData("[]")]
    public void Broken_json_throws_invalid_response(string json)
    {
        var ex = Assert.Throws<AiException>(() => AiReviewParser.Parse(json, Usage, Digest()));
        Assert.Equal(AiErrorKind.InvalidResponse, ex.Kind);
    }

    [Theory]
    [InlineData("{ \"categories\": [], \"items\": [] }")]
    [InlineData("{ \"summary\": \"s\", \"items\": [] }")]
    [InlineData("{ \"summary\": \"s\", \"categories\": [] }")]
    [InlineData("{ \"summary\": 5, \"categories\": [], \"items\": [] }")]
    [InlineData("{ \"summary\": \"s\", \"categories\": [ { \"category\": \"Temp\", \"verdict\": \"safe\" } ], \"items\": [] }")]
    public void Missing_or_wrong_field_throws_invalid_response(string json)
    {
        var ex = Assert.Throws<AiException>(() => AiReviewParser.Parse(json, Usage, Digest()));
        Assert.Equal(AiErrorKind.InvalidResponse, ex.Kind);
    }

    [Fact]
    public void Schema_is_strict_at_every_level()
    {
        var schema = AiReviewSchema.Json;
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(new[] { "summary", "categories", "items" },
            schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray());

        foreach (var (array, keyName) in new[] { ("categories", "category"), ("items", "key") })
        {
            var item = schema.GetProperty("properties").GetProperty(array).GetProperty("items");
            Assert.Equal("object", item.GetProperty("type").GetString());
            Assert.False(item.GetProperty("additionalProperties").GetBoolean());
            Assert.Equal(new[] { keyName, "verdict", "reason" },
                item.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray());
            var verdicts = item.GetProperty("properties").GetProperty("verdict").GetProperty("enum");
            Assert.Equal(new[] { "safe", "caution", "keep" }, verdicts.EnumerateArray().Select(e => e.GetString()).ToArray());
        }
        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
    }
}

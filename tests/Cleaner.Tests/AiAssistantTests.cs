using System.Text.Json;
using Cleaner.Ai;
using Cleaner.Core.Localization;
using Cleaner.Core.Models;
using Xunit;

namespace Cleaner.Tests;

/// <summary>Testaa keskusteluhistorian hallintaa väärennetyllä taustapalvelulla sekä kehotteiden kieltä.</summary>
public sealed class AiAssistantTests
{
    private const string ReviewJson =
        """{ "summary": "ok", "categories": [ { "category": "Temp", "verdict": "safe", "reason": "r" } ], "items": [] }""";

    private sealed class FakeBackend : IAiBackend
    {
        public Func<string>? JsonAnswer { get; set; } = () => ReviewJson;
        public Func<string, string> TextAnswer { get; set; } = q => "answer to " + q.Length;
        public Exception? Throw { get; set; }
        public List<IReadOnlyList<AiChatMessage>> Calls { get; } = new();
        public List<AiRequestOptions> Options { get; } = new();

        public Task<AiTextResult> CompleteJsonAsync(string system, IReadOnlyList<AiChatMessage> messages, JsonElement schema,
            AiRequestOptions options, CancellationToken ct)
        {
            Calls.Add(messages);
            Options.Add(options);
            if (Throw is not null) throw Throw;
            return Task.FromResult(new AiTextResult(JsonAnswer!(), "end_turn", new AiUsage(10, 5), false));
        }

        public Task<AiTextResult> StreamTextAsync(string system, IReadOnlyList<AiChatMessage> messages, Action<string> onDelta,
            AiRequestOptions options, CancellationToken ct)
        {
            Calls.Add(messages);
            Options.Add(options);
            if (Throw is not null) throw Throw;
            var text = TextAnswer(messages[^1].Text);
            foreach (var part in text.Chunk(3)) onDelta(new string(part));
            return Task.FromResult(new AiTextResult(text, "end_turn", new AiUsage(10, 5), false));
        }
    }

    private static ScanDigest Digest(string module = "File cleanup", int items = 3) => ScanDigest.Build(module,
        Enumerable.Range(1, items).Select(i =>
            (new CleanItem($"id{i}", "files", "Temp", $@"C:\Temp\file{i}.tmp", i * 100, "", RiskLevel.Low), true)),
        true, new DigestOptions(), new PathSanitizer(@"C:\Users\x", "x"));

    [Fact]
    public async Task ReviewAsync_adds_two_messages_and_returns_review()
    {
        var backend = new FakeBackend();
        var assistant = new AiAssistant(backend, new AiAssistantOptions { Model = "m" });

        var review = await assistant.ReviewAsync(Digest(), Language.En, CancellationToken.None);

        Assert.Equal("ok", review.Summary);
        Assert.Equal(2, assistant.History.Count);
        Assert.Equal(AiRole.User, assistant.History[0].Role);
        Assert.StartsWith(AiPrompts.ReviewInstruction(Language.En) + "\n\n# Module: File cleanup", assistant.History[0].Text);
        Assert.Equal(new AiChatMessage(AiRole.Assistant, ReviewJson), assistant.History[1]);
        Assert.Equal(new AiRequestOptions("m", AiEffort.High, 16000), backend.Options[0]);
    }

    [Fact]
    public async Task AskAsync_forwards_deltas_and_adds_two_messages()
    {
        var backend = new FakeBackend { TextAnswer = _ => "hello world" };
        var assistant = new AiAssistant(backend, new AiAssistantOptions());
        var deltas = new List<string>();

        var result = await assistant.AskAsync("why?", Language.Fi, deltas.Add, CancellationToken.None);

        Assert.Equal("hello world", result.Text);
        Assert.Equal("hello world", string.Concat(deltas));
        Assert.True(deltas.Count > 1);
        Assert.Equal(new[] { new AiChatMessage(AiRole.User, "why?"), new AiChatMessage(AiRole.Assistant, "hello world") },
            assistant.History);
        Assert.Equal(AiEffort.Medium, backend.Options[0].Effort);
        Assert.Equal(8000, backend.Options[0].MaxTokens);
        Assert.Single(backend.Calls[0]);   // taustapalvelu saa historian, jossa on uusi kysymys
    }

    [Fact]
    public async Task HealthCheckAsync_prefixes_the_instruction()
    {
        var assistant = new AiAssistant(new FakeBackend(), new AiAssistantOptions());

        await assistant.HealthCheckAsync("# System", Language.En, _ => { }, CancellationToken.None);

        Assert.Equal(AiPrompts.HealthInstruction(Language.En) + "\n\n# System", assistant.History[0].Text);
        Assert.Equal(2, assistant.History.Count);
    }

    [Fact]
    public async Task Backend_exception_removes_the_user_message()
    {
        var backend = new FakeBackend();
        var assistant = new AiAssistant(backend, new AiAssistantOptions());
        await assistant.AskAsync("first", Language.En, _ => { }, CancellationToken.None);

        backend.Throw = new AiException(AiErrorKind.Refused, "no", category: "cyber");
        await Assert.ThrowsAsync<AiException>(() => assistant.AskAsync("second", Language.En, _ => { }, CancellationToken.None));
        await Assert.ThrowsAsync<AiException>(() => assistant.ReviewAsync(Digest(), Language.En, CancellationToken.None));
        backend.Throw = new OperationCanceledException();
        await Assert.ThrowsAsync<OperationCanceledException>(() => assistant.AskAsync("third", Language.En, _ => { }, CancellationToken.None));

        Assert.Equal(2, assistant.History.Count);
        Assert.Equal("first", assistant.History[0].Text);
    }

    [Fact]
    public async Task Invalid_review_json_removes_the_user_message()
    {
        var backend = new FakeBackend { JsonAnswer = () => "{ \"summary\": \"cut" };
        var assistant = new AiAssistant(backend, new AiAssistantOptions());

        var ex = await Assert.ThrowsAsync<AiException>(() => assistant.ReviewAsync(Digest(), Language.En, CancellationToken.None));

        Assert.Equal(AiErrorKind.InvalidResponse, ex.Kind);
        Assert.Empty(assistant.History);
    }

    [Fact]
    public async Task History_is_trimmed_to_max_history_in_pairs()
    {
        var assistant = new AiAssistant(new FakeBackend(), new AiAssistantOptions());

        for (int i = 1; i <= 15; i++)
            await assistant.AskAsync("q" + i, Language.En, _ => { }, CancellationToken.None);

        Assert.Equal(20, assistant.History.Count);
        Assert.Equal("q6", assistant.History[0].Text);
        Assert.Equal(AiRole.User, assistant.History[0].Role);
        Assert.Equal("q15", assistant.History[18].Text);
    }

    [Fact]
    public async Task History_is_trimmed_by_characters_but_newest_pair_stays()
    {
        var backend = new FakeBackend { TextAnswer = q => new string('a', 50) };
        var assistant = new AiAssistant(backend, new AiAssistantOptions { MaxHistoryChars = 200 });

        await assistant.AskAsync(new string('1', 40), Language.En, _ => { }, CancellationToken.None);   // 90
        await assistant.AskAsync(new string('2', 40), Language.En, _ => { }, CancellationToken.None);   // 180
        await assistant.AskAsync(new string('3', 40), Language.En, _ => { }, CancellationToken.None);   // 270 → 180
        Assert.Equal(4, assistant.History.Count);
        Assert.StartsWith("2", assistant.History[0].Text);

        await assistant.AskAsync(new string('4', 500), Language.En, _ => { }, CancellationToken.None);   // yksin yli rajan
        Assert.Equal(2, assistant.History.Count);
        Assert.StartsWith("4", assistant.History[0].Text);
        Assert.Equal(AiRole.User, assistant.History[0].Role);
    }

    [Fact]
    public async Task Earlier_review_digest_is_omitted_after_a_new_review()
    {
        var assistant = new AiAssistant(new FakeBackend(), new AiAssistantOptions());

        await assistant.ReviewAsync(Digest("File cleanup"), Language.En, CancellationToken.None);
        await assistant.AskAsync("why?", Language.En, _ => { }, CancellationToken.None);
        Assert.Contains("file1.tmp", assistant.History[0].Text);   // vain yksi arvio: digest on kokonaisena

        await assistant.ReviewAsync(Digest("Browser caches"), Language.En, CancellationToken.None);

        var first = assistant.History[0].Text;
        Assert.Equal(AiPrompts.ReviewInstruction(Language.En) + "\n\n(earlier review of File cleanup; item list omitted)", first);
        Assert.DoesNotContain("file1.tmp", first);
        Assert.Contains("file1.tmp", assistant.History[4].Text);
        Assert.Contains("# Module: Browser caches", assistant.History[4].Text);
        Assert.Equal(6, assistant.History.Count);
    }

    [Fact]
    public async Task Reset_clears_history()
    {
        var assistant = new AiAssistant(new FakeBackend(), new AiAssistantOptions());
        await assistant.AskAsync("q", Language.En, _ => { }, CancellationToken.None);

        assistant.Reset();

        Assert.Empty(assistant.History);
    }

    [Fact]
    public async Task Reset_during_a_call_does_not_leave_a_lone_answer()
    {
        var backend = new FakeBackend();
        var assistant = new AiAssistant(backend, new AiAssistantOptions());
        backend.TextAnswer = q => { assistant.Reset(); return "late"; };
        backend.JsonAnswer = () => { assistant.Reset(); return ReviewJson; };

        var text = await assistant.AskAsync("q", Language.En, _ => { }, CancellationToken.None);
        var review = await assistant.ReviewAsync(Digest(), Language.En, CancellationToken.None);

        Assert.Equal("late", text.Text);
        Assert.Equal("ok", review.Summary);
        Assert.Empty(assistant.History);
    }

    [Fact]
    public void System_prompt_names_the_answer_language()
    {
        Assert.Contains("suomeksi", AiPrompts.System(Language.Fi));
        Assert.Contains("English", AiPrompts.System(Language.En));
        Assert.Contains("%USERPROFILE%", AiPrompts.System(Language.Fi));
        Assert.Contains("%USERNAME%", AiPrompts.System(Language.En));
        Assert.NotEqual(AiPrompts.ReviewInstruction(Language.En), AiPrompts.ReviewInstruction(Language.Fi));
        Assert.NotEqual(AiPrompts.HealthInstruction(Language.En), AiPrompts.HealthInstruction(Language.Fi));
    }
}

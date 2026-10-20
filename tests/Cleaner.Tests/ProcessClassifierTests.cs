using Cleaner.Core.Models;
using Cleaner.Modules.Processes;
using Xunit;

namespace Cleaner.Tests;

/// <summary>Puhtaan logiikan testit käsin rakennetuilla näytteillä. Ei oikeita prosesseja.</summary>
public sealed class ProcessClassifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private const int OwnPid = 999;
    private const int OwnParentPid = 998;
    private const string ExplorerExe = @"C:\Windows\explorer.exe";
    private const string AppExe = @"C:\Program Files\App\app.exe";
    private const string HelperExe = @"C:\Program Files\App\helper.exe";

    public ProcessClassifierTests()
    {
        ProcessGuard.WindowsDirectory = @"C:\Windows";
    }

    /// <summary>FILETIME minuutteja sitten: suurempi luku tarkoittaa vanhempaa prosessia.</summary>
    private static long T(int minutesAgo) => Now.UtcDateTime.AddMinutes(-minutesAgo).ToFileTimeUtc();

    private static ProcessInfo Info(int pid, int ppid, string name, string? exe, long start,
        int session = 1, long bytes = 0, bool window = false, bool critical = false, bool canTerminate = true,
        bool exeMissing = false, bool idle = false)
        => new(pid, ppid, name, exe, start, session, bytes, TimeSpan.Zero, window, critical, canTerminate, exeMissing, idle);

    private static ProcessInfo Explorer() => Info(50, 40, "explorer", ExplorerExe, T(600));

    private static ProcessSnapshot Snap(params ProcessInfo[] processes)
        => new(processes, CurrentSessionId: 1, OwnPid: OwnPid, OwnParentPid: OwnParentPid, TakenAt: Now, SampleInterval: TimeSpan.FromSeconds(3));

    private static ProcessRule Rule(string pattern, string name = "Testisääntö", string description = "", RiskLevel risk = RiskLevel.Medium)
        => new() { Id = "r-" + pattern, Name = name, Match = new List<string> { pattern }, Description = description, Risk = risk };

    private static IReadOnlyList<ProcessRule> NoRules => Array.Empty<ProcessRule>();

    // ---------- Suojaus ----------

    [Fact]
    public void Guard_returns_reason_for_each_protected_case()
    {
        var snap = Snap();

        Assert.Equal("Oma prosessi", ProcessGuard.Protects(Info(OwnPid, 1, "me", AppExe, T(1)), snap));
        Assert.Equal("Oma prosessi", ProcessGuard.Protects(Info(OwnParentPid, 1, "parent", AppExe, T(1)), snap));
        Assert.Equal("Järjestelmäprosessi", ProcessGuard.Protects(Info(4, 0, "System", AppExe, T(1)), snap));
        Assert.Equal("Järjestelmäprosessi", ProcessGuard.Protects(Info(0, 0, "[System Process]", AppExe, T(1)), snap));
        Assert.Equal("Toinen istunto tai palvelu", ProcessGuard.Protects(Info(10, 1, "svc", AppExe, T(1), session: 0), snap));
        Assert.Equal("Tietoja ei voitu lukea", ProcessGuard.Protects(Info(10, 1, "x", null, T(1)), snap));
        Assert.Equal("Tietoja ei voitu lukea", ProcessGuard.Protects(Info(10, 1, "x", AppExe, 0), snap));
        Assert.Equal("Windows-kansion prosessi", ProcessGuard.Protects(Info(10, 1, "explorer", @"c:\WINDOWS\System32\svchost.exe", T(1)), snap));
        Assert.Equal("Kriittinen prosessi", ProcessGuard.Protects(Info(10, 1, "x", AppExe, T(1), critical: true), snap));
        Assert.Equal("Ei oikeuksia lopettaa", ProcessGuard.Protects(Info(10, 1, "x", AppExe, T(1), canTerminate: false), snap));

        Assert.Null(ProcessGuard.Protects(Info(10, 1, "x", AppExe, T(1)), snap));
        // "C:\Windows2\..." ei ole Windows-kansion alla.
        Assert.Null(ProcessGuard.Protects(Info(10, 1, "x", @"C:\Windows2\app.exe", T(1)), snap));
    }

    [Fact]
    public void Protected_processes_are_not_listed()
    {
        var snap = Snap(
            Explorer(),
            Info(10, 50, "svc", AppExe, T(5), session: 0),
            Info(11, 50, "noexe", null, T(5)),
            Info(12, 50, "locked", AppExe, T(5), canTerminate: false),
            Info(13, 50, "ok", AppExe, T(5)));

        var items = ProcessClassifier.Classify(snap, NoRules);

        var only = Assert.Single(items);
        Assert.Equal(13, only.Root.Pid);
    }

    // ---------- Sulautus ----------

    [Fact]
    public void Same_exe_chain_merges_into_one_root()
    {
        var snap = Snap(
            Explorer(),
            Info(100, 50, "app", AppExe, T(30), bytes: 100),
            Info(101, 100, "app", AppExe, T(20), bytes: 10),
            Info(102, 101, "app", AppExe.ToUpperInvariant(), T(10), bytes: 1));

        var items = ProcessClassifier.Classify(snap, NoRules);

        var group = Assert.Single(items);
        Assert.Equal(100, group.Root.Pid);
        Assert.Equal(new[] { 101, 102 }, group.Children.Select(c => c.Pid));
        Assert.Equal(111, group.TotalPrivateBytes);
        Assert.Contains("2 aliprosessia", ProcessDescription.Build(group, snap));
        Assert.Contains("käynnistäjä explorer (PID 50)", ProcessDescription.Build(group, snap));
    }

    [Fact]
    public void Same_exe_pid_cycle_keeps_one_root()
    {
        // PID-kierrätys voi teoriassa tuottaa syklin, jossa molemmat ovat toistensa vanhempia samalla käynnistysajalla.
        var start = T(30);
        var snap = Snap(
            Info(100, 101, "app", AppExe, start, bytes: 1),
            Info(101, 100, "app", AppExe, start, bytes: 2));

        var items = ProcessClassifier.Classify(snap, NoRules);

        var group = Assert.Single(items);
        Assert.Single(group.Children);
        Assert.Equal(3, group.TotalPrivateBytes);
    }

    [Fact]
    public void Protected_same_exe_intermediate_stops_merge_chain()
    {
        var snap = Snap(
            Explorer(),
            Info(100, 50, "app", AppExe, T(30)),
            Info(101, 100, "app", AppExe, T(20), canTerminate: false),
            Info(102, 101, "app", AppExe, T(10)));

        var items = ProcessClassifier.Classify(snap, NoRules);

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Empty(i.Children));
        var grandchild = Assert.Single(items, i => i.Root.Pid == 102);
        Assert.Equal(101, grandchild.Parent?.Pid);
    }

    [Fact]
    public void CollectSameExeDescendants_returns_children_before_grandchildren()
    {
        var snap = Snap(
            Explorer(),
            Info(100, 50, "app", AppExe, T(30)),
            Info(103, 100, "helper", HelperExe, T(25)),
            Info(102, 101, "app", AppExe, T(10)),
            Info(101, 100, "app", AppExe, T(20)),
            Info(104, 100, "app", AppExe, T(15), canTerminate: false));

        var descendants = ProcessClassifier.CollectSameExeDescendants(snap, 100);

        // Lapset PID-järjestyksessä (104 on suojattu mutta mukana), sitten lapsenlapsi.
        Assert.Equal(new[] { 101, 104, 102 }, descendants.Select(d => d.Pid));
        Assert.Empty(ProcessClassifier.CollectSameExeDescendants(snap, 12345));
    }

    [Fact]
    public void Different_exe_child_is_its_own_root_with_parent_in_description()
    {
        var snap = Snap(
            Explorer(),
            Info(200, 50, "steam", @"C:\Program Files\Steam\steam.exe", T(30)),
            Info(201, 200, "steamwebhelper", @"C:\Program Files\Steam\bin\steamwebhelper.exe", T(20)));

        var items = ProcessClassifier.Classify(snap, NoRules);

        Assert.Equal(2, items.Count);
        var helper = Assert.Single(items, i => i.Root.Pid == 201);
        Assert.Empty(helper.Children);
        Assert.Equal(ProcessCategory.ActiveBackground, helper.Category);
        Assert.Contains("käynnistäjä steam (PID 200)", ProcessDescription.Build(helper, snap));
        var steam = Assert.Single(items, i => i.Root.Pid == 200);
        Assert.Contains("käynnistäjä explorer (PID 50)", ProcessDescription.Build(steam, snap));
    }

    [Fact]
    public void Same_exe_without_parent_relation_stays_two_roots()
    {
        var snap = Snap(
            Explorer(),
            Info(300, 50, "app", AppExe, T(30)),
            Info(301, 50, "app", AppExe, T(20)));

        var items = ProcessClassifier.Classify(snap, NoRules);

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Empty(i.Children));
    }

    [Fact]
    public void Same_exe_parent_started_later_does_not_merge_and_child_is_orphan()
    {
        var snap = Snap(
            Explorer(),
            Info(500, 50, "app", AppExe, T(5)),
            Info(501, 500, "app", AppExe, T(30)));

        var items = ProcessClassifier.Classify(snap, NoRules);

        Assert.Equal(2, items.Count);
        var child = Assert.Single(items, i => i.Root.Pid == 501);
        Assert.Equal(ProcessCategory.Orphan, child.Category);
        Assert.Null(child.Parent);
        var parent = Assert.Single(items, i => i.Root.Pid == 500);
        Assert.Equal(ProcessCategory.ActiveBackground, parent.Category);
    }

    // ---------- Orpo ----------

    [Fact]
    public void Dead_parent_makes_orphan()
    {
        var snap = Snap(Explorer(), Info(400, 777, "app", AppExe, T(30)));

        var item = Assert.Single(ProcessClassifier.Classify(snap, NoRules));

        Assert.Equal(ProcessCategory.Orphan, item.Category);
        Assert.Equal(RiskLevel.Medium, item.Risk);
        Assert.Null(item.Parent);
        Assert.StartsWith("Käynnistäjä (PID 777) on päättynyt", ProcessDescription.Build(item, snap));
    }

    [Fact]
    public void Orphan_is_preselected_only_when_idle()
    {
        var idle = Assert.Single(ProcessClassifier.Classify(Snap(Info(400, 777, "app", AppExe, T(30), idle: true)), NoRules));
        Assert.True(idle.Preselected);

        var active = Assert.Single(ProcessClassifier.Classify(Snap(Info(400, 777, "app", AppExe, T(30), idle: false)), NoRules));
        Assert.False(active.Preselected);

        // Yksi aktiivinen lapsi tekee koko ryhmästä aktiivisen.
        var mixed = Assert.Single(ProcessClassifier.Classify(Snap(
            Info(400, 777, "app", AppExe, T(30), idle: true),
            Info(401, 400, "app", AppExe, T(20), idle: false)), NoRules));
        Assert.Equal(ProcessCategory.Orphan, mixed.Category);
        Assert.False(mixed.Preselected);
    }

    // ---------- Kategorioiden etusija ----------

    [Fact]
    public void Window_wins_over_missing_exe_orphan_and_rule()
    {
        var rules = new[] { Rule("app", name: "Sovellus") };
        var snap = Snap(Info(600, 777, "app", AppExe, T(30), window: true, exeMissing: true, idle: true));

        var item = Assert.Single(ProcessClassifier.Classify(snap, rules));

        Assert.Equal(ProcessCategory.Windowed, item.Category);
        Assert.Equal(RiskLevel.High, item.Risk);
        Assert.False(item.Preselected);
        Assert.NotNull(item.Rule);
        var description = ProcessDescription.Build(item, snap);
        Assert.StartsWith("Näkyvä ikkuna · ohjelmatiedostoa ei löydy · Sovellus", description);
    }

    [Fact]
    public void Window_in_child_makes_whole_group_windowed()
    {
        var snap = Snap(
            Explorer(),
            Info(600, 50, "app", AppExe, T(30)),
            Info(601, 600, "app", AppExe, T(20), window: true));

        var item = Assert.Single(ProcessClassifier.Classify(snap, NoRules));

        Assert.Equal(ProcessCategory.Windowed, item.Category);
        Assert.True(item.HasVisibleWindow);
    }

    [Fact]
    public void Missing_exe_wins_over_rule_and_is_preselected()
    {
        var snap = Snap(Explorer(), Info(600, 50, "app", AppExe, T(30), exeMissing: true));

        var item = Assert.Single(ProcessClassifier.Classify(snap, new[] { Rule("app") }));

        Assert.Equal(ProcessCategory.MissingExe, item.Category);
        Assert.Equal(RiskLevel.Medium, item.Risk);
        Assert.True(item.Preselected);
        Assert.StartsWith("Ohjelmatiedostoa ei löydy", ProcessDescription.Build(item, snap));
    }

    [Fact]
    public void Orphan_wins_over_rule()
    {
        var snap = Snap(Info(600, 777, "app", AppExe, T(30)));

        var item = Assert.Single(ProcessClassifier.Classify(snap, new[] { Rule("app") }));

        Assert.Equal(ProcessCategory.Orphan, item.Category);
        Assert.NotNull(item.Rule);
    }

    [Fact]
    public void Rule_wins_over_idle_and_carries_its_risk()
    {
        var snap = Snap(Explorer(), Info(600, 50, "app", AppExe, T(30), idle: true));

        var item = Assert.Single(ProcessClassifier.Classify(snap, new[] { Rule("APP.EXE", name: "Sovellus", risk: RiskLevel.Low) }));

        Assert.Equal(ProcessCategory.Known, item.Category);
        Assert.Equal(RiskLevel.Low, item.Risk);
        Assert.False(item.Preselected);
        Assert.StartsWith("Sovellus · ei suorittimen käyttöä 3 s aikana", ProcessDescription.Build(item, snap));
    }

    [Fact]
    public void Idle_and_active_background_are_never_preselected()
    {
        var snap = Snap(
            Explorer(),
            Info(600, 50, "idle", AppExe, T(30), idle: true),
            Info(601, 50, "busy", HelperExe, T(30), idle: false));

        var items = ProcessClassifier.Classify(snap, NoRules);

        var idle = Assert.Single(items, i => i.Root.Pid == 600);
        Assert.Equal(ProcessCategory.IdleBackground, idle.Category);
        Assert.False(idle.Preselected);
        Assert.Contains("ei suorittimen käyttöä 3 s aikana", ProcessDescription.Build(idle, snap));

        var busy = Assert.Single(items, i => i.Root.Pid == 601);
        Assert.Equal(ProcessCategory.ActiveBackground, busy.Category);
        Assert.False(busy.Preselected);
        Assert.Contains("käyttää suoritinta", ProcessDescription.Build(busy, snap));
    }

    [Fact]
    public void Result_is_ordered_by_category_then_memory()
    {
        var snap = Snap(
            Explorer(),
            Info(600, 50, "small", AppExe, T(30), bytes: 1),
            Info(601, 50, "big", HelperExe, T(30), bytes: 100),
            Info(602, 777, "orphan", @"C:\x\o.exe", T(30)),
            Info(603, 50, "gone", @"C:\x\g.exe", T(30), exeMissing: true));

        var items = ProcessClassifier.Classify(snap, NoRules);

        Assert.Equal(new[] { 603, 602, 601, 600 }, items.Select(i => i.Root.Pid));
    }

    // ---------- Tunniste ja kuvaus ----------

    [Fact]
    public void Item_id_round_trips_and_rejects_bad_input()
    {
        var id = ProcessItemId.Make(1234, 133_000_000_000_000_000L);
        Assert.Equal("processes:1234:133000000000000000", id);
        Assert.True(ProcessItemId.TryParse(id, out var pid, out var start));
        Assert.Equal(1234, pid);
        Assert.Equal(133_000_000_000_000_000L, start);

        Assert.False(ProcessItemId.TryParse("processes:", out _, out _));
        Assert.False(ProcessItemId.TryParse("processes:1", out _, out _));
        Assert.False(ProcessItemId.TryParse("processes:1:2:3", out _, out _));
        Assert.False(ProcessItemId.TryParse("startup:1:2", out _, out _));
        Assert.False(ProcessItemId.TryParse("processes:a:2", out _, out _));
        Assert.False(ProcessItemId.TryParse("processes:-1:2", out _, out _));
        Assert.False(ProcessItemId.TryParse("processes:1:x", out _, out _));
        Assert.False(ProcessItemId.TryParse("", out _, out _));
    }

    [Fact]
    public void Description_parts_follow_fixed_order()
    {
        var rules = new[] { Rule("GoogleCrashHandler.exe", name: "Google Crash Handler", description: "kuvaus") };
        var snap = Snap(
            Explorer(),
            Info(700, 50, "GoogleCrashHandler", @"C:\Program Files\Google\GoogleCrashHandler.exe", T(5), idle: true),
            Info(701, 700, "GoogleCrashHandler", @"C:\Program Files\Google\GoogleCrashHandler.exe", T(4), idle: true));

        var item = Assert.Single(ProcessClassifier.Classify(snap, rules));

        Assert.Equal(
            "Google Crash Handler: kuvaus · ei suorittimen käyttöä 3 s aikana · käynnistetty 5 min · 1 aliprosessia · käynnistäjä explorer (PID 50)",
            ProcessDescription.Build(item, snap));
    }

    [Fact]
    public void Description_age_formats_and_omits_cpu_when_not_sampled()
    {
        var noSampling = Snap(Explorer(), Info(700, 50, "app", AppExe, T(90))) with { SampleInterval = TimeSpan.Zero };
        var item = Assert.Single(ProcessClassifier.Classify(noSampling, NoRules));
        Assert.Equal("käynnistetty 1 h 30 min · käynnistäjä explorer (PID 50)", ProcessDescription.Build(item, noSampling));

        // Prosessi on vanhempi kuin explorer (T(600)), joten se on orpo; ikä näkyy syyn jälkeen.
        var days = Snap(Explorer(), Info(700, 50, "app", AppExe, T(50 * 60 + 7))) with { SampleInterval = TimeSpan.Zero };
        item = Assert.Single(ProcessClassifier.Classify(days, NoRules));
        Assert.Equal(ProcessCategory.Orphan, item.Category);
        Assert.Equal("Käynnistäjä (PID 50) on päättynyt · käynnistetty 2 d 2 h", ProcessDescription.Build(item, days));
    }

    [Fact]
    public void Category_names_are_finnish()
    {
        Assert.Equal("Ohjelmatiedosto puuttuu levyltä", ProcessDescription.CategoryName(ProcessCategory.MissingExe));
        Assert.Equal("Orvot apuprosessit", ProcessDescription.CategoryName(ProcessCategory.Orphan));
        Assert.Equal("Tunnetut taustaprosessit", ProcessDescription.CategoryName(ProcessCategory.Known));
        Assert.Equal("Avoimet ohjelmat", ProcessDescription.CategoryName(ProcessCategory.Windowed));
        Assert.Equal("Taustaprosessit, ei käyttöä", ProcessDescription.CategoryName(ProcessCategory.IdleBackground));
        Assert.Equal("Taustaprosessit, aktiivisia", ProcessDescription.CategoryName(ProcessCategory.ActiveBackground));
    }
}

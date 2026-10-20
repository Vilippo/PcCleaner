using System.Diagnostics;
using System.Runtime.Versioning;
using Cleaner.Core.Logging;
using Cleaner.Core.Models;
using Cleaner.Modules.Processes;
using Xunit;

namespace Cleaner.Tests;

/// <summary>
/// Integraatiotestit oikealla prosessilla: cmd.exe kopioidaan tilapäiskansioon, koska Windows-kansion prosessit
/// ovat suojattuja eikä niitä listata. Jokainen testi saa oman kopion ja prosessin. Ajetaan vain Windowsissa.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection(ProcessClassifierTests.Collection)]   // ProcessGuard.WindowsDirectory on staattinen
public sealed class ProcessesModuleTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "pccleaner-tests", Guid.NewGuid().ToString("N"));
    private readonly string _exePath;
    private readonly Process? _process;
    private readonly TestLogger _logger = new();

    public ProcessesModuleTests()
    {
        _exePath = Path.Combine(_tempDir, "pcc-test-cmd.exe");
        if (!OperatingSystem.IsWindows()) return;

        Directory.CreateDirectory(_tempDir);
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), _exePath);
        _process = Process.Start(new ProcessStartInfo(_exePath, "/c ping -n 60 127.0.0.1 >nul")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        })!;
    }

    public void Dispose()
    {
        if (_process is not null)
        {
            // Koko puu: cmd:n lapsena pyörivä ping.exe ei saa jäädä orvoksi, vaikka moduuli olisi jo lopettanut cmd:n.
            try { _process.Kill(entireProcessTree: true); } catch { }
            try { _process.WaitForExit(2000); } catch { }
            _process.Dispose();
        }

        // Exe voi olla hetken lukittu lopetuksen jälkeen.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
                break;
            }
            catch (IOException) { Thread.Sleep(200); }
            catch (UnauthorizedAccessException) { Thread.Sleep(200); }
        }
    }

    private Process TestProcess => _process ?? throw new InvalidOperationException("Testiprosessi ei käynnistynyt.");

    private long StartFileTime => TestProcess.StartTime.ToFileTime();

    private string TestItemId => ProcessItemId.Make(TestProcess.Id, StartFileTime);

    private ProcessesModule CreateModule()
        => new(rulesFile: Path.Combine(_tempDir, "missing.json"), sampleInterval: TimeSpan.FromMilliseconds(200));

    private CleanerContext Context(bool dryRun) => new() { Logger = _logger, DryRun = dryRun };

    private CleanItem TestItem(string? id = null) => new(
        Id: id ?? TestItemId,
        ModuleId: "processes",
        Category: "Taustaprosessit, ei käyttöä",
        Path: _exePath,
        SizeBytes: 0,
        Description: "",
        Risk: RiskLevel.Medium,
        Preselected: false);

    private async Task<CleanItem> ScanTestProcessAsync(ProcessesModule module)
    {
        var scan = await module.ScanAsync(Context(dryRun: true));
        Assert.Empty(scan.Errors);
        return Assert.Single(scan.Items, i => i.Id == TestItemId);
    }

    // ---------- Skannaus ----------

    [Fact]
    public async Task Scan_lists_test_process_as_background()
    {
        if (!OperatingSystem.IsWindows()) return;
        Thread.Sleep(300);

        var item = await ScanTestProcessAsync(CreateModule());

        Assert.Equal(_exePath, item.Path, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("processes", item.ModuleId);
        Assert.StartsWith("Taustaprosessit, ", item.Category);
        Assert.Equal(RiskLevel.Medium, item.Risk);
        Assert.False(item.Preselected);
        Assert.Contains("käynnistäjä", item.Description);
        Assert.Contains("käynnistetty", item.Description);
    }

    [Fact]
    public async Task Scan_is_idempotent_for_test_process()
    {
        if (!OperatingSystem.IsWindows()) return;
        Thread.Sleep(300);
        var module = CreateModule();

        var first = await ScanTestProcessAsync(module);
        Assert.False(TestProcess.HasExited);
        var second = await ScanTestProcessAsync(module);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Path, second.Path);
        Assert.False(TestProcess.HasExited);
    }

    [Fact]
    public async Task Scan_does_not_list_windows_directory_processes()
    {
        if (!OperatingSystem.IsWindows()) return;

        var scan = await CreateModule().ScanAsync(Context(dryRun: true));

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
        Assert.All(scan.Items, i => Assert.False(i.Path.StartsWith(windows, StringComparison.OrdinalIgnoreCase), i.Path));
        Assert.All(scan.Items, i => Assert.StartsWith("processes:", i.Id));
        Assert.DoesNotContain(scan.Items, i => i.Id.StartsWith($"processes:{Environment.ProcessId}:"));
    }

    [Fact]
    public async Task Scan_does_not_list_any_ancestor_of_own_process()
    {
        if (!OperatingSystem.IsWindows()) return;

        // Ketju lasketaan tässä itsenäisesti näytteen ParentPid-tiedoista: testhost -> dotnet (vstest) -> dotnet (test) -> pääte.
        var snapshot = new ProcessInventory().Capture(TimeSpan.Zero, default);
        var byPid = snapshot.Processes.ToDictionary(p => p.Pid);
        var ancestors = new List<int>();
        var current = byPid[Environment.ProcessId];
        while (byPid.TryGetValue(current.ParentPid, out var parent) && parent.StartFileTime <= current.StartFileTime && !ancestors.Contains(parent.Pid))
        {
            ancestors.Add(parent.Pid);
            current = parent;
        }
        Assert.NotEmpty(ancestors);

        var scan = await CreateModule().ScanAsync(Context(dryRun: true));

        foreach (var pid in ancestors)
            Assert.DoesNotContain(scan.Items, i => i.Id.StartsWith($"processes:{pid}:", StringComparison.Ordinal));
        Assert.DoesNotContain(scan.Items, i => i.Id.StartsWith($"processes:{snapshot.OwnParentPid}:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Scan_warns_when_rules_file_missing()
    {
        if (!OperatingSystem.IsWindows()) return;

        var scan = await CreateModule().ScanAsync(Context(dryRun: true));

        Assert.Empty(scan.Errors);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.StartsWith("Sääntötiedostoa ei löydy: "));
    }

    [Fact]
    public async Task Scan_reports_invalid_rules_file_in_errors()
    {
        if (!OperatingSystem.IsWindows()) return;
        var rulesFile = Path.Combine(_tempDir, "broken.json");
        File.WriteAllText(rulesFile, """{ "rules": [ { "id": "x", "name": "X", "match": [] } ] }""");
        var module = new ProcessesModule(rulesFile: rulesFile, sampleInterval: TimeSpan.FromMilliseconds(200));

        var scan = await module.ScanAsync(Context(dryRun: true));

        var error = Assert.Single(scan.Errors);
        Assert.StartsWith("Sääntötiedosto on virheellinen: ", error);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message == error);
        Assert.Contains(scan.Items, i => i.Id == TestItemId);
    }

    // ---------- Lopetus ----------

    [Fact]
    public async Task DryRun_keeps_process_alive_but_reports_removed()
    {
        if (!OperatingSystem.IsWindows()) return;

        var result = await CreateModule().CleanAsync(new[] { TestItem() }, Context(dryRun: true));

        Assert.True(result.WasDryRun);
        Assert.Single(result.Removed);
        Assert.Empty(result.Skipped);
        Assert.False(TestProcess.HasExited);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Debug && e.Message.StartsWith("[KUIVA] Lopetettaisiin "));
        Assert.DoesNotContain(_logger.Entries, e => e.Message.StartsWith("Lopetettiin "));
    }

    [Fact]
    public async Task Apply_terminates_process()
    {
        if (!OperatingSystem.IsWindows()) return;
        var pid = TestProcess.Id;

        var result = await CreateModule().CleanAsync(new[] { TestItem() }, Context(dryRun: false));

        Assert.False(result.WasDryRun);
        Assert.Single(result.Removed);
        Assert.Empty(result.Skipped);
        Assert.True(TestProcess.WaitForExit(5000));
        Assert.True(TestProcess.HasExited);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Info
            && e.Message.StartsWith("Lopetettiin pcc-test-cmd (PID ")
            && e.Message.Contains($"(PID {pid}): ")
            && e.Message.EndsWith(_exePath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Apply_skips_reused_pid()
    {
        if (!OperatingSystem.IsWindows()) return;
        var wrongId = ProcessItemId.Make(TestProcess.Id, StartFileTime + 1);

        var result = await CreateModule().CleanAsync(new[] { TestItem(wrongId) }, Context(dryRun: false));

        Assert.Empty(result.Removed);
        var (_, reason) = Assert.Single(result.Skipped);
        Assert.Equal("PID on käytetty uudelleen", reason);
        Assert.False(TestProcess.HasExited);
    }

    [Fact]
    public async Task Clean_skips_foreign_module_and_bad_id()
    {
        if (!OperatingSystem.IsWindows()) return;
        var foreign = TestItem() with { ModuleId = "startup" };
        var badId = TestItem("processes:abc");

        var result = await CreateModule().CleanAsync(new[] { foreign, badId }, Context(dryRun: false));

        Assert.Empty(result.Removed);
        Assert.Equal(2, result.Skipped.Count);
        Assert.Equal("Kohde ei kuulu tälle moduulille", result.Skipped[0].Reason);
        Assert.Equal("Virheellinen tunniste", result.Skipped[1].Reason);
        Assert.False(TestProcess.HasExited);
    }

    [Fact]
    public async Task Clean_skips_protected_process()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var self = Process.GetCurrentProcess();
        var ownItem = TestItem(ProcessItemId.Make(Environment.ProcessId, self.StartTime.ToFileTime()))
            with { Path = Environment.ProcessPath ?? "" };

        var result = await CreateModule().CleanAsync(new[] { ownItem }, Context(dryRun: false));

        Assert.Empty(result.Removed);
        var (_, reason) = Assert.Single(result.Skipped);
        Assert.Equal("Oma prosessi", reason);
    }

    // ---------- Jälkeläiset ja kuiva ajo väärennetyillä rajapinnoilla ----------

    private const string FakeExe = @"C:\Program Files\Fake\fake.exe";

    private static ProcessInfo FakeInfo(int pid, int ppid, long start, string exe = FakeExe, bool canTerminate = true)
        => new(pid, ppid, "fake", exe, start, SessionId: 1, PrivateBytes: 0, CpuTime: TimeSpan.Zero,
            HasVisibleWindow: false, IsCritical: false, CanTerminate: canTerminate, ExeMissing: false, IsIdle: false);

    /// <summary>Juuri 100, saman exen lapsi 101 ja lapsenlapsi 102, eri exen lapsi 103, suojattu saman exen lapsi 104.</summary>
    private static ProcessSnapshot FakeTree() => new(
        new[]
        {
            FakeInfo(100, 50, 10),
            FakeInfo(101, 100, 20),
            FakeInfo(102, 101, 30),
            FakeInfo(103, 100, 25, exe: @"C:\Program Files\Fake\other.exe"),
            FakeInfo(104, 100, 26, canTerminate: false),
        },
        CurrentSessionId: 1, OwnPid: 999, OwnParentPid: 998, TakenAt: DateTimeOffset.Now, SampleInterval: TimeSpan.Zero);

    private sealed class FakeInventory(ProcessSnapshot snapshot) : IProcessInventory
    {
        public ProcessSnapshot Capture(TimeSpan sampleInterval, CancellationToken ct) => snapshot with { SampleInterval = sampleInterval };
    }

    private sealed class FakeTerminator : IProcessTerminator
    {
        public List<int> Calls { get; } = new();
        public HashSet<int> Fail { get; } = new();

        public bool TryTerminate(int pid, long startFileTime, out string reason)
        {
            Calls.Add(pid);
            if (Fail.Contains(pid))
            {
                reason = "Ei oikeuksia";
                return false;
            }
            reason = "";
            return true;
        }
    }

    private static ProcessesModule FakeModule(FakeTerminator terminator)
        => new(rulesFile: Path.Combine(Path.GetTempPath(), "pccleaner-tests", "missing.json"),
            inventory: new FakeInventory(FakeTree()), terminator: terminator, sampleInterval: TimeSpan.Zero);

    private static CleanItem FakeItem(int pid, long start) => new(
        ProcessItemId.Make(pid, start), "processes", "Taustaprosessit, aktiivisia", FakeExe, 0, "", RiskLevel.Medium, false);

    [Fact]
    public async Task Apply_terminates_same_exe_descendants_after_root_and_skips_protected()
    {
        if (!OperatingSystem.IsWindows()) return;
        var terminator = new FakeTerminator();
        terminator.Fail.Add(101);

        var result = await FakeModule(terminator).CleanAsync(new[] { FakeItem(100, 10) }, Context(dryRun: false));

        // Juuri ensin, sitten lapset ennen lapsenlapsia; 103 on eri exe, 104 on suojattu (ei lopetusoikeutta).
        Assert.Equal(new[] { 100, 101, 102 }, terminator.Calls);
        Assert.Single(result.Removed);
        Assert.Empty(result.Skipped);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Info && e.Message.StartsWith("Lopetettiin fake (PID 100): "));
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.StartsWith("Aliprosessia fake (PID 101) ei lopetettu: Ei oikeuksia"));
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Info && e.Message.StartsWith("Lopetettiin aliprosessi fake (PID 102): "));
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.StartsWith("Aliprosessia fake (PID 104) ei lopetettu: Ei oikeuksia lopettaa"));
    }

    [Fact]
    public async Task DryRun_never_calls_terminator()
    {
        if (!OperatingSystem.IsWindows()) return;
        var terminator = new FakeTerminator();

        var result = await FakeModule(terminator).CleanAsync(new[] { FakeItem(100, 10), FakeItem(100, 11) }, Context(dryRun: true));

        Assert.Empty(terminator.Calls);
        Assert.Single(result.Removed);
        var (_, reason) = Assert.Single(result.Skipped);
        Assert.Equal("PID on käytetty uudelleen", reason);
    }

    [Fact]
    public async Task Clean_skips_exited_process()
    {
        if (!OperatingSystem.IsWindows()) return;
        var item = TestItem();
        TestProcess.Kill();
        TestProcess.WaitForExit(5000);

        var result = await CreateModule().CleanAsync(new[] { item }, Context(dryRun: false));

        Assert.Empty(result.Removed);
        var (_, reason) = Assert.Single(result.Skipped);
        Assert.True(reason is "Prosessi on jo päättynyt" or "PID on käytetty uudelleen", reason);
    }
}

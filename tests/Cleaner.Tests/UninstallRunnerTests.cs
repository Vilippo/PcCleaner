using System.Runtime.Versioning;
using Cleaner.Modules.Uninstall;
using Microsoft.Win32;
using Xunit;

namespace Cleaner.Tests;

/// <summary>UninstallRunner ja RunningProcessCheck. Rekisteri vain HKCU\Software\PcCleanerTests\&lt;guid&gt; (CON-8).</summary>
[SupportedOSPlatform("windows")]
public sealed class UninstallRunnerTests : IDisposable
{
    private const string Guid1 = "{11111111-2222-3333-4444-555555555555}";
    private readonly string _sub = $@"Software\PcCleanerTests\{Guid.NewGuid():N}";
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PcCleanerTests", Guid.NewGuid().ToString("N"));

    private string FooKey => @"HKCU\" + _sub + @"\Uninstall\Foo";

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        if (OperatingSystem.IsWindows())
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(_sub, throwOnMissingSubKey: false); } catch { }
            try { Registry.CurrentUser.DeleteSubKey(@"Software\PcCleanerTests", throwOnMissingSubKey: false); } catch { }
        }
    }

    private static InstalledProgram Program(string keyName, string? uninstall, string? quiet = null, bool isMsi = false,
        string keyPath = @"HKCU\Software\PcCleanerTests\none") =>
        new(keyPath, keyName, "Foo", null, null, null, uninstall, quiet, null, 0, isMsi);

    public static TheoryData<InstalledProgram, bool, string?, string?> Commands => new()
    {
        { Program(Guid1, "MsiExec.exe /X" + Guid1, isMsi: true), false, "msiexec.exe", "/x " + Guid1 },
        { Program(Guid1, "MsiExec.exe /X" + Guid1, isMsi: true), true, "msiexec.exe", "/x " + Guid1 + " /qb-" },
        { Program("FooMsi", "MsiExec.exe /I" + Guid1, isMsi: true), true, "msiexec.exe", "/x " + Guid1 + " /qb-" },
        { Program("Foo", @"""C:\Foo\unins000.exe"" /S", @"""C:\Foo\unins000.exe"" /VERYSILENT"), true, @"C:\Foo\unins000.exe", "/VERYSILENT" },
        { Program("Foo", @"""C:\Foo\unins000.exe"" /S", @"""C:\Foo\unins000.exe"" /VERYSILENT"), false, @"C:\Foo\unins000.exe", "/S" },
        { Program("Foo", @"""C:\Program Files\Foo\uninst.exe"" /log=""C:\x y.txt"""), true, @"C:\Program Files\Foo\uninst.exe", @"/log=""C:\x y.txt""" },
        { Program("Foo", null), false, null, null },
        { Program("Foo", "   "), true, null, null },
    };

    [Theory]
    [MemberData(nameof(Commands))]
    public void BuildCommand_picks_command(InstalledProgram program, bool quiet, string? fileName, string? arguments)
    {
        if (!OperatingSystem.IsWindows()) return;

        var cmd = UninstallRunner.BuildCommand(program, quiet);

        if (fileName is null)
        {
            Assert.Null(cmd);
            return;
        }
        Assert.NotNull(cmd);
        Assert.Equal(fileName, cmd.Value.FileName);
        Assert.Equal(arguments, cmd.Value.Arguments);
    }

    [Fact]
    public async Task RunAsync_reports_key_removed_when_uninstaller_deletes_key()
    {
        if (!OperatingSystem.IsWindows()) return;
        Registry.CurrentUser.CreateSubKey(_sub + @"\Uninstall\Foo").Dispose();
        var program = Program("Foo", $@"cmd.exe /c reg delete ""{FooKey}"" /f", keyPath: FooKey);
        var log = new TestLogger();

        var result = await new UninstallRunner(log, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200))
            .RunAsync(program, quiet: false, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.KeyRemoved);
        Assert.True(result.Elapsed > TimeSpan.Zero);
        Assert.Contains(log.Entries, e => e.Message.StartsWith("Käynnistetään poisto-ohjelma: cmd.exe"));
    }

    [Fact]
    public async Task RunAsync_reports_exit_code_and_key_kept_after_timeout()
    {
        if (!OperatingSystem.IsWindows()) return;
        Registry.CurrentUser.CreateSubKey(_sub + @"\Uninstall\Foo").Dispose();
        var program = Program("Foo", "cmd.exe /c exit 3", keyPath: FooKey);

        var result = await new UninstallRunner(new TestLogger(), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(200))
            .RunAsync(program, quiet: false, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(3, result.ExitCode);
        Assert.False(result.KeyRemoved);
        Assert.True(result.Elapsed >= TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RunAsync_returns_error_for_missing_executable()
    {
        if (!OperatingSystem.IsWindows()) return;
        var missing = Path.Combine(_tempDir, "missing_uninstaller.exe");
        var program = Program("Foo", $@"""{missing}"" /S");

        var result = await new UninstallRunner(new TestLogger(), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(200))
            .RunAsync(program, quiet: false, CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_without_command_returns_finnish_error()
    {
        if (!OperatingSystem.IsWindows()) return;
        var program = Program("Foo", null);

        var result = await new UninstallRunner(new TestLogger()).RunAsync(program, quiet: true, CancellationToken.None);

        Assert.Equal("Ohjelmalla ei ole poisto-ohjelmaa.", result.Error);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public void FindRunningUnder_system_directory_does_not_throw()
    {
        if (!OperatingSystem.IsWindows()) return;

        var ex = Record.Exception(() => RunningProcessCheck.FindRunningUnder(Environment.SystemDirectory));

        Assert.Null(ex);
    }

    [Fact]
    public void FindRunningUnder_empty_temp_directory_returns_null()
    {
        if (!OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(_tempDir);

        Assert.Null(RunningProcessCheck.FindRunningUnder(_tempDir));
    }
}

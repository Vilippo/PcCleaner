using System.Diagnostics;
using System.Runtime.Versioning;
using Cleaner.Modules.Processes;
using Xunit;

namespace Cleaner.Tests;

/// <summary>Oikeiden prosessien testit: oma prosessi, System (PID 4) ja lyhytikäinen cmd-apuprosessi. Ajetaan vain Windowsissa.</summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessInventoryTests
{
    private static Process StartPing(int seconds = 30)
        => Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n {seconds} 127.0.0.1 >nul")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        })!;

    private static void KillQuietly(Process process)
    {
        // Koko puu: cmd:n lapsena pyörivä ping.exe ei saa jäädä orvoksi, vaikka cmd olisi jo lopetettu.
        try { process.Kill(entireProcessTree: true); } catch { }
        process.Dispose();
    }

    [Fact]
    public void Capture_includes_current_process_with_exe_path_and_session()
    {
        if (!OperatingSystem.IsWindows()) return;

        var snapshot = new ProcessInventory().Capture(TimeSpan.Zero, default);

        Assert.Equal(Environment.ProcessId, snapshot.OwnPid);
        Assert.Equal(TimeSpan.Zero, snapshot.SampleInterval);

        var self = Assert.Single(snapshot.Processes, p => p.Pid == Environment.ProcessId);
        Assert.Equal(Environment.ProcessPath, self.ExePath, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(snapshot.CurrentSessionId, self.SessionId);
        Assert.True(self.StartFileTime > 0);
        Assert.True(self.CanTerminate);
        Assert.False(self.IsIdle);
        Assert.False(self.ExeMissing);
        Assert.NotNull(self.CpuTime);
        Assert.Equal(snapshot.OwnParentPid, self.ParentPid);

        var system = Assert.Single(snapshot.Processes, p => p.Pid == 4);
        Assert.False(system.CanTerminate);
        Assert.Equal("System", system.Name);
    }

    [Fact]
    public void Capture_with_interval_lists_child_process_with_cpu_time()
    {
        if (!OperatingSystem.IsWindows()) return;

        var child = StartPing();
        try
        {
            Thread.Sleep(500);

            var snapshot = new ProcessInventory().Capture(TimeSpan.FromMilliseconds(300), default);

            Assert.Equal(TimeSpan.FromMilliseconds(300), snapshot.SampleInterval);
            var row = Assert.Single(snapshot.Processes, p => p.Pid == child.Id);
            Assert.Equal(Environment.ProcessId, row.ParentPid);
            Assert.Equal("cmd", row.Name);
            Assert.NotNull(row.CpuTime);
            Assert.NotNull(row.ExePath);
            Assert.Equal(child.StartTime.ToFileTime(), row.StartFileTime);
            // Joutilaisuutta ei väitetä varmaksi: ping voi ajoittaa suoritinta mittausjakson aikana.
        }
        finally
        {
            KillQuietly(child);
        }
    }

    [Fact]
    public void TryTerminate_rejects_wrong_start_time()
    {
        if (!OperatingSystem.IsWindows()) return;

        var child = StartPing();
        try
        {
            var start = child.StartTime.ToFileTime();
            var terminator = new ProcessTerminator();

            Assert.False(terminator.TryTerminate(child.Id, start + 1, out var reason));
            Assert.Equal("PID on käytetty uudelleen", reason);
            Assert.False(child.HasExited);

            Assert.True(terminator.TryTerminate(child.Id, start, out reason));
            Assert.Equal("", reason);
            Assert.True(child.WaitForExit(5000));
            Assert.True(child.HasExited);
        }
        finally
        {
            KillQuietly(child);
        }
    }

    [Fact]
    public void TryTerminate_reports_already_exited()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        })!;
        child.WaitForExit();

        Assert.False(new ProcessTerminator().TryTerminate(child.Id, 1, out var reason));
        Assert.Equal("Prosessi on jo päättynyt", reason);
    }
}

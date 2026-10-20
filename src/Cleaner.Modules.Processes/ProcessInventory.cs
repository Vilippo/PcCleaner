using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Cleaner.Modules.Processes;

/// <summary>
/// Lukee prosessit Toolhelp32-snapshotista (PID, PPID, nimi) ja täydentää tiedot System.Diagnostics.Process-luokalla
/// sekä kernel32-kutsuilla (exe-polku, kriittisyys, lopetusoikeus). Ei muuta mitään. Prosessikohtaiset
/// lukuvirheet antavat puuttuvan arvon, jolloin ProcessGuard pitää prosessin suojattuna.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessInventory : IProcessInventory
{
    private readonly record struct ToolhelpEntry(int Pid, int ParentPid, string Name);

    public ProcessSnapshot Capture(TimeSpan sampleInterval, CancellationToken ct)
    {
        var entries = ReadToolhelp();
        var ownPid = Environment.ProcessId;
        var ownParentPid = 0;
        foreach (var e in entries)
        {
            if (e.Pid == ownPid) { ownParentPid = e.ParentPid; break; }
        }

        var first = new List<ProcessInfo>(entries.Count);
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var info = ReadProcess(entry);
            if (info is not null) first.Add(info);
        }

        List<ProcessInfo> processes;
        if (sampleInterval > TimeSpan.Zero)
        {
            // Odotus keskeytyy peruutukseen.
            if (ct.WaitHandle.WaitOne(sampleInterval))
                ct.ThrowIfCancellationRequested();

            processes = new List<ProcessInfo>(first.Count);
            foreach (var info in first)
            {
                ct.ThrowIfCancellationRequested();
                if (info.CpuTime is null)
                {
                    processes.Add(info); // ensimmäinen näyte epäonnistui: IsIdle jää false
                    continue;
                }

                var (alive, second) = ReadCpuTime(info.Pid);
                if (!alive) continue; // kadonnut näytteiden välissä: pois
                processes.Add(info with { IsIdle = second is not null && second.Value == info.CpuTime.Value });
            }
        }
        else
        {
            processes = first;
        }

        int sessionId;
        using (var self = Process.GetCurrentProcess())
            sessionId = self.SessionId;

        return new ProcessSnapshot(processes, sessionId, ownPid, ownParentPid, DateTimeOffset.Now, sampleInterval);
    }

    private static ProcessInfo? ReadProcess(ToolhelpEntry entry)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(entry.Pid);
        }
        catch (ArgumentException) { return null; }          // kadonnut
        catch (InvalidOperationException) { return null; }

        using (process)
        {
            var sessionId = -1;
            try { sessionId = process.SessionId; }
            catch (Win32Exception) { }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }

            long privateBytes = 0;
            try { privateBytes = process.PrivateMemorySize64; }
            catch (Win32Exception) { }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }

            long startFileTime = 0;
            try { startFileTime = process.StartTime.ToFileTime(); }
            catch (Win32Exception) { }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }

            TimeSpan? cpuTime = null;
            try { cpuTime = process.TotalProcessorTime; }
            catch (Win32Exception) { }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }

            var hasVisibleWindow = false;
            try { hasVisibleWindow = process.MainWindowHandle != IntPtr.Zero; }
            catch (Win32Exception) { }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }

            var (exePath, isCritical) = QueryImage(entry.Pid);
            var exeMissing = exePath is not null && !File.Exists(exePath);
            var canTerminate = CanOpenForTerminate(entry.Pid);

            return new ProcessInfo(
                Pid: entry.Pid,
                ParentPid: entry.ParentPid,
                Name: entry.Name,
                ExePath: exePath,
                StartFileTime: startFileTime,
                SessionId: sessionId,
                PrivateBytes: privateBytes,
                CpuTime: cpuTime,
                HasVisibleWindow: hasVisibleWindow,
                IsCritical: isCritical,
                CanTerminate: canTerminate,
                ExeMissing: exeMissing,
                IsIdle: false);
        }
    }

    /// <summary>Toinen suoritinaikanäyte. alive = false, kun prosessi on kadonnut; second = null, kun lukeminen epäonnistui.</summary>
    private static (bool Alive, TimeSpan? Second) ReadCpuTime(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return (true, process.TotalProcessorTime);
        }
        catch (ArgumentException) { return (false, null); }
        catch (InvalidOperationException) { return (false, null); }
        catch (Win32Exception) { return (true, null); }
    }

    private static List<ToolhelpEntry> ReadToolhelp()
    {
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == INVALID_HANDLE_VALUE)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Prosessilistan lukeminen epäonnistui (CreateToolhelp32Snapshot).");

        var entries = new List<ToolhelpEntry>();
        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (!Process32FirstW(snapshot, ref entry))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Prosessilistan lukeminen epäonnistui (Process32First).");

            do
            {
                entries.Add(new ToolhelpEntry((int)entry.th32ProcessID, (int)entry.th32ParentProcessID, StripExe(entry.szExeFile)));
            }
            while (Process32NextW(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return entries;
    }

    private static string StripExe(string name)
        => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    /// <summary>Exe-polku ja kriittisyys yhdellä PROCESS_QUERY_LIMITED_INFORMATION-kahvalla. Epäonnistuminen antaa (null, false).</summary>
    private static (string? ExePath, bool IsCritical) QueryImage(int pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return (null, false);

        try
        {
            string? path = null;
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            if (QueryFullProcessImageNameW(handle, 0, buffer, ref size) && size > 0)
                path = buffer.ToString(0, size);

            var critical = IsProcessCritical(handle, out var isCritical) && isCritical;
            return (path, critical);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static bool CanOpenForTerminate(int pid)
    {
        var handle = OpenProcess(PROCESS_TERMINATE, false, pid);
        if (handle == IntPtr.Zero) return false;
        CloseHandle(handle);
        return true;
    }

    #region P/Invoke

    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const uint PROCESS_TERMINATE = 0x0001;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool IsProcessCritical(IntPtr hProcess, out bool critical);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CloseHandle(IntPtr hObject);

    #endregion
}

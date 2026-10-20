using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Cleaner.Core.Services;

namespace Cleaner.Ai;

/// <summary>
/// Lukee koneen tiedot koneen kunnon arviota varten. Ei heitä koskaan: jokainen lähde luetaan omassa
/// try-lohkossaan, ja epäonnistunut kenttä jää null-arvoksi, tyhjäksi tai oletukseksi.
/// </summary>
public static class SystemFactsCollector
{
    public static SystemFacts Collect()
    {
        string os = "unknown";
        try
        {
            os = Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? " (64-bit)" : " (32-bit)");
        }
        catch { }

        int cores = 0;
        try { cores = Environment.ProcessorCount; } catch { }

        long? total = null;
        try
        {
            var value = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (value > 0) total = value;
        }
        catch { }

        long? available = null;
        try
        {
            if (OperatingSystem.IsWindows()) available = WindowsMemory.AvailablePhysicalBytes();
        }
        catch { }

        var uptime = TimeSpan.Zero;
        try { uptime = TimeSpan.FromMilliseconds(Environment.TickCount64); } catch { }

        var drives = new List<DriveFacts>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                    string? label = null;
                    try { label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? null : drive.VolumeLabel; } catch { }
                    drives.Add(new DriveFacts(drive.Name, label, drive.TotalSize, drive.AvailableFreeSpace));
                }
                catch { }
            }
        }
        catch { }

        bool elevated = false;
        try { elevated = ElevationHelper.IsElevated(); } catch { }

        return new SystemFacts(os, cores, total, available, uptime, drives, elevated);
    }
}

[SupportedOSPlatform("windows")]
internal static class WindowsMemory
{
    /// <summary>Vapaa fyysinen muisti tavuina tai null, jos kutsu epäonnistuu.</summary>
    public static long? AvailablePhysicalBytes()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref status) ? (long)status.ullAvailPhys : null;
    }

    #region P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    #endregion
}

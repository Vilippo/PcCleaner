using System.Runtime.InteropServices;

namespace Cleaner.UI.Services;

/// <summary>
/// Lukee ikkunan palautussijainnin Windowsilta (<c>GetWindowPlacement</c>). Windows pitää <c>rcNormalPosition</c>-
/// kentässä koon, johon ikkuna palautuu: maksimoidulla, pienennetyllä ja Aero Snapilla napsautetulla ikkunalla se
/// on tilaa edeltävä koko, ei näytöllä näkyvä. AppWindow ei kerro tätä: napsautettu ikkuna on sille tavallinen
/// palautettu ikkuna.
/// </summary>
internal static class NativeWindowPlacement
{
    /// <summary>
    /// Palautussijainti näytön koordinaateissa (fyysiset pikselit) ja tieto, palautuuko ikkuna maksimoituna.
    /// Null, jos Windows-kutsu epäonnistuu.
    /// </summary>
    public static (int X, int Y, int Width, int Height, bool Maximized)? Read(IntPtr hwnd)
    {
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hwnd, ref placement)) return null;

        // rcNormalPosition on työtilakoordinaateissa (origo näytön työalueen vasemmassa yläkulmassa). Ero näytön
        // koordinaatteihin on nollasta poikkeava, kun tehtäväpalkki on näytön vasemmassa tai yläreunassa.
        var r = placement.rcNormalPosition;
        int dx = 0, dy = 0;
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
        {
            dx = info.rcWork.Left - info.rcMonitor.Left;
            dy = info.rcWork.Top - info.rcMonitor.Top;
        }

        // Pienennetty ikkuna palautuu maksimoituna, jos se pienennettiin maksimoidusta tilasta.
        var maximized = placement.showCmd == SW_SHOWMAXIMIZED ||
                        (placement.showCmd == SW_SHOWMINIMIZED && (placement.flags & WPF_RESTORETOMAXIMIZED) != 0);
        return (r.Left + dx, r.Top + dy, r.Right - r.Left, r.Bottom - r.Top, maximized);
    }

    #region P/Invoke

    private const int SW_SHOWMINIMIZED = 2;
    private const int SW_SHOWMAXIMIZED = 3;
    private const int WPF_RESTORETOMAXIMIZED = 0x0002;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    #endregion
}

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Cleaner.Core.Localization;

namespace Cleaner.Modules.Processes;

/// <summary>
/// Lopettaa prosessin kahdessa vaiheessa: ensin pääikkunan sulkupyyntö (enintään 5 s), sitten Kill (enintään 5 s).
/// Prosessia, jonka käynnistysaika ei vastaa annettua tunnistetta, ei koskaan lopeteta (PID-kierrätys).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessTerminator : IProcessTerminator
{
    private const int CloseWaitMs = 5000;
    private const int KillWaitMs = 5000;

    public bool TryTerminate(int pid, long startFileTime, out string reason)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            reason = Loc.T("Process has already exited", "Prosessi on jo päättynyt");
            return false;
        }
        catch (InvalidOperationException)
        {
            reason = Loc.T("Process has already exited", "Prosessi on jo päättynyt");
            return false;
        }

        using (process)
        {
            long actualStart;
            try
            {
                actualStart = process.StartTime.ToFileTime();
            }
            catch (Win32Exception)
            {
                reason = Loc.T("Access denied", "Ei oikeuksia");
                return false;
            }
            catch (InvalidOperationException)
            {
                // Päättyi ennen tarkistusta: mitään ei lopetettu.
                reason = Loc.T("Process has already exited", "Prosessi on jo päättynyt");
                return false;
            }

            if (actualStart != startFileTime)
            {
                reason = Loc.T("PID has been reused", "PID on käytetty uudelleen");
                return false;
            }

            try
            {
                if (process.MainWindowHandle != IntPtr.Zero && process.CloseMainWindow())
                    process.WaitForExit(CloseWaitMs);

                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(KillWaitMs);
                }

                if (process.HasExited)
                {
                    reason = "";
                    return true;
                }

                reason = Loc.T("Process did not exit within 10 s", "Prosessi ei päättynyt 10 s kuluessa");
                return false;
            }
            catch (Win32Exception)
            {
                reason = Loc.T("Access denied", "Ei oikeuksia");
                return false;
            }
            catch (InvalidOperationException)
            {
                // Prosessi päättyi kesken toimenpiteen: se on poissa, eli tavoite täyttyi.
                reason = "";
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }
    }
}

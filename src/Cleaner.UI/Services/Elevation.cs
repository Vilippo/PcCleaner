using System.ComponentModel;
using System.Diagnostics;

namespace Cleaner.UI.Services;

public static class Elevation
{
    /// <summary>
    /// Käynnistää saman ohjelman uudelleen järjestelmänvalvojana (ShellExecute "runas").
    /// Palauttaa true, jos uusi prosessi käynnistyi; false jos käyttäjä perui UAC-kehotteen.
    /// </summary>
    public static bool TryRestartElevated()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            });
            return true;
        }
        catch (Win32Exception)
        {
            // Käyttäjä perui UAC-kehotteen (ERROR_CANCELLED).
            return false;
        }
    }
}

using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Cleaner.Core.Services;

public static class ElevationHelper
{
    /// <summary>Onko nykyinen prosessi käynnissä järjestelmänvalvojana.</summary>
    public static bool IsElevated()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}

namespace Cleaner.Modules.Processes;

/// <summary>Lopettaa yksittäisen prosessin.</summary>
public interface IProcessTerminator
{
    /// <summary>
    /// Lopettaa prosessin vain, jos sen StartTime.ToFileTime() == startFileTime. Palauttaa true, kun prosessi on päättynyt;
    /// muuten false ja syy suomeksi.
    /// </summary>
    bool TryTerminate(int pid, long startFileTime, out string reason);
}

namespace Cleaner.Modules.Processes;

/// <summary>
/// Yhden prosessin tiedot näytteenottohetkellä. Puuttuva arvo (ExePath null, StartFileTime 0, CpuTime null)
/// tarkoittaa, ettei tietoa voitu lukea; ProcessGuard pitää sellaisen prosessin suojattuna.
/// </summary>
/// <param name="Pid">Prosessin tunniste.</param>
/// <param name="ParentPid">Käynnistäjän tunniste Toolhelp-snapshotista. Voi osoittaa jo päättyneeseen prosessiin.</param>
/// <param name="Name">Prosessin nimi ilman .exe-päätettä (kuten Process.ProcessName).</param>
/// <param name="ExePath">Ohjelmatiedoston täysi polku tai null, jos sitä ei voitu lukea.</param>
/// <param name="StartFileTime">Kernelin luontiaika FILETIME-muodossa; 0 jos ei luettavissa.</param>
/// <param name="SessionId">Istunnon numero.</param>
/// <param name="PrivateBytes">Yksityinen muisti tavuina (0 jos ei luettavissa).</param>
/// <param name="CpuTime">Suoritinaika ensimmäisellä näytteellä tai null, jos lukeminen epäonnistui.</param>
/// <param name="HasVisibleWindow">Onko prosessilla näkyvä pääikkuna.</param>
/// <param name="IsCritical">Onko prosessi merkitty kriittiseksi (lopetus kaataa järjestelmän).</param>
/// <param name="CanTerminate">Onnistuiko prosessin avaaminen PROCESS_TERMINATE-oikeudella.</param>
/// <param name="ExeMissing">ExePath tunnetaan, mutta tiedostoa ei ole levyllä.</param>
/// <param name="IsIdle">Suoritinaika ei muuttunut mittausjakson aikana (molemmat näytteet onnistuivat).</param>
public sealed record ProcessInfo(
    int Pid,
    int ParentPid,
    string Name,
    string? ExePath,
    long StartFileTime,
    int SessionId,
    long PrivateBytes,
    TimeSpan? CpuTime,
    bool HasVisibleWindow,
    bool IsCritical,
    bool CanTerminate,
    bool ExeMissing,
    bool IsIdle);

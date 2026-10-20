namespace Cleaner.Modules.Processes;

/// <summary>Kaikkien prosessien näyte yhdellä hetkellä, myös suojatut.</summary>
/// <param name="Processes">Kaikki näytteen prosessit.</param>
/// <param name="CurrentSessionId">Oma istunto; muiden istuntojen prosessit ovat suojattuja.</param>
/// <param name="OwnPid">Oma prosessi.</param>
/// <param name="OwnParentPid">Oman prosessin käynnistäjä (0 jos ei tiedossa).</param>
/// <param name="TakenAt">Näytteenottohetki (iän laskentaan).</param>
/// <param name="SampleInterval">Suoritinmittauksen väli; 0 tarkoittaa, ettei joutilaisuutta mitattu.</param>
public sealed record ProcessSnapshot(
    IReadOnlyList<ProcessInfo> Processes,
    int CurrentSessionId,
    int OwnPid,
    int OwnParentPid,
    DateTimeOffset TakenAt,
    TimeSpan SampleInterval);

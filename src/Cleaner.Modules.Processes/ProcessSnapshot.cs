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
    TimeSpan SampleInterval)
{
    private AncestorCache? _ancestors;

    /// <summary>
    /// Oman prosessin esivanhemmat näytteessä (vanhempi, isovanhempi jne.), ei omaa PID:tä. Ketju seurataan
    /// <see cref="ProcessInfo.ParentPid"/>-kenttää pitkin vain, kun esivanhempi käynnistyi aikaisintaan lapsensa
    /// kanssa (kierrätetty PID katkaisee ketjun), ja sykli pysäyttää seurannan. Lasketaan kerran per näyte.
    /// Jo päättynyt vanhempi ei ole mukana; sen hoitaa <see cref="OwnParentPid"/>.
    /// </summary>
    public IReadOnlySet<int> OwnAncestorPids
    {
        get
        {
            var cache = _ancestors;
            if (cache is null || !ReferenceEquals(cache.Source, Processes))
                _ancestors = cache = new AncestorCache(Processes, ComputeOwnAncestors());
            return cache.Pids;
        }
    }

    private HashSet<int> ComputeOwnAncestors()
    {
        var byPid = new Dictionary<int, ProcessInfo>();
        foreach (var p in Processes)
            byPid.TryAdd(p.Pid, p);

        var result = new HashSet<int>();
        if (!byPid.TryGetValue(OwnPid, out var current))
            return result;

        var visited = new HashSet<int> { OwnPid };
        while (byPid.TryGetValue(current.ParentPid, out var parent)
               && parent.StartFileTime <= current.StartFileTime
               && visited.Add(parent.Pid))
        {
            result.Add(parent.Pid);
            current = parent;
        }
        return result;
    }

    /// <summary>Laskettu joukko ja lähde, josta se laskettiin: with-kopio uudella Processes-listalla laskee uudelleen.</summary>
    private sealed record AncestorCache(IReadOnlyList<ProcessInfo> Source, HashSet<int> Pids);
}

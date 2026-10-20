namespace Cleaner.Modules.Processes;

/// <summary>Lukee käynnissä olevat prosessit. Toteutus ei muuta mitään.</summary>
public interface IProcessInventory
{
    /// <summary>
    /// Ottaa näytteen kaikista prosesseista. sampleInterval &gt; 0: kaksi suoritinaikanäytettä, IsIdle täytetään.
    /// sampleInterval == 0: yksi näyte, IsIdle = false kaikilla. Ei heitä prosessikohtaisia virheitä.
    /// </summary>
    ProcessSnapshot Capture(TimeSpan sampleInterval, CancellationToken ct);
}

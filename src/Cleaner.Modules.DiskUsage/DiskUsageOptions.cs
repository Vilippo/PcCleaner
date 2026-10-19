namespace Cleaner.Modules.DiskUsage;

public sealed record DiskUsageOptions
{
    /// <summary>Skannattavat juuret. Oletus: kaikki kiinteät, valmiit asemat.</summary>
    public IReadOnlyList<string> Roots { get; init; } = DefaultRoots();

    /// <summary>Montako suurinta kansiota raportoidaan (juuria ei lasketa).</summary>
    public int TopFolders { get; init; } = 20;

    /// <summary>Montako suurinta tiedostoa raportoidaan.</summary>
    public int TopFiles { get; init; } = 20;

    /// <summary>Rinnakkaisten ylätason kansioiden määrä. Oletus: prosessoriytimet.</summary>
    public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;

    public static IReadOnlyList<string> DefaultRoots()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => d.RootDirectory.FullName)
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }
}

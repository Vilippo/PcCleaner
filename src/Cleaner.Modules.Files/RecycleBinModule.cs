using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Cleaner.Core.Abstractions;
using Cleaner.Core.Models;
using Cleaner.Core.Services;

namespace Cleaner.Modules.Files;

/// <summary>
/// Roskakorin tyhjennys Windowsin omalla rajapinnalla (shell32).
/// Ei kosketa $Recycle.Bin-kansioon suoraan – se on Windowsin sisäinen rakenne.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RecycleBinModule : ICleanerModule
{
    public string Id => "recyclebin";
    public string DisplayName => "Roskakori";
    public string Description => "Tyhjentää roskakorin kaikilta asemilta.";
    public RiskLevel Risk => RiskLevel.Low;
    public bool RequiresAdmin => false;

    private const string ItemId = "recyclebin:all";

    public Task<ScanResult> ScanAsync(CleanerContext context, CancellationToken ct = default)
    {
        var result = new ScanResult { ModuleId = Id };

        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        var hr = SHQueryRecycleBin(null, ref info);

        if (hr != 0)
        {
            result.Errors.Add($"Roskakorin kysely epäonnistui (HRESULT 0x{hr:X8})");
            return Task.FromResult(result);
        }

        if (info.i64NumItems > 0)
        {
            result.Items.Add(new CleanItem(
                Id: ItemId,
                ModuleId: Id,
                Category: "Roskakori",
                Path: "Roskakori (kaikki asemat)",
                SizeBytes: info.i64Size,
                Description: $"{info.i64NumItems} kohdetta roskakorissa",
                Risk: RiskLevel.Low));
        }

        context.Logger.Info($"Roskakori: {info.i64NumItems} kohdetta, {ByteFormatter.Format(info.i64Size)}");
        return Task.FromResult(result);
    }

    public Task<CleanResult> CleanAsync(IReadOnlyList<CleanItem> items, CleanerContext context, CancellationToken ct = default)
    {
        var result = new CleanResult { ModuleId = Id, WasDryRun = context.DryRun };
        var item = items.FirstOrDefault(i => i.Id == ItemId);
        if (item is null) return Task.FromResult(result);

        if (context.DryRun)
        {
            context.Logger.Info("[KUIVA] Roskakori tyhjennettäisiin.");
            result.Removed.Add(item);
            return Task.FromResult(result);
        }

        // Ei vahvistusikkunaa, ei edistymispalkkia, ei ääntä – oma UI hoitaa nämä.
        const uint flags = SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND;
        var hr = SHEmptyRecycleBin(IntPtr.Zero, null, flags);

        if (hr == 0)
        {
            result.Removed.Add(item);
            context.Logger.Info("Roskakori tyhjennetty.");
        }
        else
        {
            result.Skipped.Add((item, $"HRESULT 0x{hr:X8}"));
            context.Logger.Warning($"Roskakorin tyhjennys epäonnistui: 0x{hr:X8}");
        }

        return Task.FromResult(result);
    }

    #region P/Invoke

    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    // shellapi.h pakkaa tämän rakenteen 1 tavuun vain 32-bittisenä (#if !defined(_WIN64)).
    // 64-bittisenä käytetään luonnollista tasausta: 4 + 4 täytettä + 8 + 8 = 24 tavua.
    // Pack = 4 antoi 20 tavua, ja shell32 vastasi E_INVALIDARG (0x80070057).
    // Ohjelma on vain 64-bittinen; 32-bittinen käännös vaatisi Pack = 1.
    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    #endregion
}

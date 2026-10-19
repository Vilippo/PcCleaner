using System.Collections.ObjectModel;
using Cleaner.Core.Engine;
using Cleaner.Core.Logging;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using Cleaner.Modules.DiskUsage;
using Cleaner.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace Cleaner.UI.ViewModels;

/// <summary>
/// Pääikkunan tila: moduulilista, valittu moduuli, skannaus ja puhdistus edistymisineen.
/// Puhdistus on kolmivaiheinen: kuiva ajo → yhteenveto → toinen vahvistus → oikea ajo.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IDialogService _dialogs;
    private readonly CleanerEngine _engine;
    private readonly FileLogger _fileLog;
    private readonly UiLogger _uiLog;
    private readonly ICleanerLogger _log;
    private CancellationTokenSource? _cts;

    public ObservableCollection<ModuleViewModel> Modules { get; } = new();
    public ObservableCollection<string> LogLines => _uiLog.Lines;

    public bool IsElevated { get; }
    public bool ShowElevationHint => !IsElevated;
    public string TitleText => IsElevated ? "PC Cleaner (järjestelmänvalvoja)" : "PC Cleaner";
    public string LogPath => _fileLog.FilePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanScanAll))]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(CleanCommand), nameof(ScanAllCommand))]
    public partial ModuleViewModel? SelectedModule { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(CanScanAll))]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(CleanCommand), nameof(ScanAllCommand), nameof(CancelCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial int ProgressPercent { get; set; }

    [ObservableProperty]
    public partial string ProgressMessage { get; set; }

    [ObservableProperty]
    public partial string BusyTitle { get; set; }

    /// <summary>Rekisterimoduulin sivulla näytetään palautusnappi.</summary>
    [ObservableProperty]
    public partial bool IsRegistrySelected { get; set; }

    /// <summary>Levytilan analyysin sivulla näytetään polku- ja top-N-valinta sekä treemap.</summary>
    [ObservableProperty]
    public partial bool IsDiskUsageSelected { get; set; }

    /// <summary>Skannattava kansio. Tyhjä = kaikki kiinteät asemat.</summary>
    [ObservableProperty]
    public partial string DiskUsagePath { get; set; }

    /// <summary>Montako suurinta kansiota ja tiedostoa listataan (NumberBox sitoo doublen).</summary>
    [ObservableProperty]
    public partial double DiskUsageTop { get; set; }

    /// <summary>Viimeisimmän levytila-analyysin puu treemapia varten (useampi asema yhdistetään).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTreemap))]
    public partial DirectoryNode? TreemapRoot { get; set; }

    public bool HasTreemap => TreemapRoot is not null;

    public bool IsIdle => !IsBusy;
    public bool CanScanAll => !IsBusy;

    public MainViewModel(IDialogService dialogs, DispatcherQueue dispatcher)
    {
        _dialogs = dialogs;
        ProgressMessage = "";
        BusyTitle = "";
        DiskUsagePath = "";
        DiskUsageTop = 20;
        IsElevated = ElevationHelper.IsElevated();

        _fileLog = new FileLogger(minLevel: LogLevel.Debug);
        _uiLog = new UiLogger(dispatcher, LogLevel.Info);
        _log = new CompositeLogger(_uiLog, _fileLog);

        var rulesDir = Path.Combine(AppContext.BaseDirectory, "rules");
        _engine = new CleanerEngine(UiModuleFactory.CreateAll(rulesDir, _log));

        foreach (var module in _engine.Modules)
            Modules.Add(new ModuleViewModel(module));

        _log.Info($"PC Cleaner käynnistyi ({(IsElevated ? "järjestelmänvalvoja" : "tavallinen käyttäjä")}). Loki: {_fileLog.FilePath}");
    }

    // ---------- Konteksti ----------

    private CleanerContext CreateContext(bool dryRun, CancellationTokenSource cts)
    {
        // Progress<T> luodaan UI-säikeessä, joten raportit tulevat takaisin UI-säikeeseen.
        var progress = new Progress<(int Percent, string Message)>(p =>
        {
            ProgressPercent = p.Percent;
            ProgressMessage = p.Message;
        });

        return new CleanerContext
        {
            DryRun = dryRun,
            Logger = _log,
            IsElevated = IsElevated,
            Progress = progress
        };
    }

    private CancellationTokenSource BeginBusy(string title)
    {
        _cts = new CancellationTokenSource();
        IsBusy = true;
        BusyTitle = title;
        ProgressPercent = 0;
        ProgressMessage = "";
        return _cts;
    }

    private void EndBusy()
    {
        IsBusy = false;
        BusyTitle = "";
        ProgressMessage = "";
        _cts?.Dispose();
        _cts = null;
    }

    // ---------- Komennot ----------

    private bool CanScan() => !IsBusy && SelectedModule is not null;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        if (SelectedModule is null) return;
        await ScanModulesAsync(new[] { SelectedModule });
    }

    [RelayCommand(CanExecute = nameof(CanScanAll))]
    private async Task ScanAllAsync()
    {
        var targets = Modules.Where(m => m.SelectedByDefault).ToList();
        await ScanModulesAsync(targets);
    }

    private async Task ScanModulesAsync(IReadOnlyList<ModuleViewModel> targets)
    {
        var cts = BeginBusy("Skannataan");
        try
        {
            foreach (var target in targets)
            {
                cts.Token.ThrowIfCancellationRequested();
                BusyTitle = $"Skannataan: {target.DisplayName}";
                target.ClearScan();

                if (target.Module is DiskUsageModule diskUsage)
                {
                    var top = Math.Clamp((int)DiskUsageTop, 1, 500);
                    var path = DiskUsagePath.Trim().Trim('"');
                    diskUsage.Options = new DiskUsageOptions
                    {
                        Roots = path.Length == 0 ? DiskUsageOptions.DefaultRoots() : new[] { path },
                        TopFolders = top,
                        TopFiles = top
                    };
                    TreemapRoot = null;
                }

                var ctx = CreateContext(dryRun: true, cts);
                var results = await _engine.ScanAsync(ctx, new[] { target.Id }, cts.Token);
                target.LoadScan(results[0]);

                if (target.Module is DiskUsageModule scanned && scanned.LastTrees.Count > 0)
                    TreemapRoot = DirectoryNode.Combine(scanned.LastTrees, "Kaikki asemat");
            }
        }
        catch (OperationCanceledException)
        {
            _log.Warning("Skannaus peruutettu.");
            foreach (var target in targets.Where(t => !t.HasScanned))
                target.StatusText = "Skannaus peruutettu.";
        }
        catch (Exception ex)
        {
            _log.Error($"Skannaus epäonnistui: {ex}");
            await _dialogs.InfoAsync("Skannaus epäonnistui", ex.Message);
        }
        finally
        {
            EndBusy();
        }
    }

    private bool CanClean() => !IsBusy && SelectedModule is { HasScanned: true, SelectedCount: > 0 };

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        var module = SelectedModule;
        if (module is null) return;

        var items = module.SelectedItems();
        if (items.Count == 0)
        {
            await _dialogs.InfoAsync("Ei valittuja kohteita", "Valitse ensin kohteet valintaruuduista.");
            return;
        }

        var isStartup = module.Id == "startup";
        var verb = isStartup ? "poistetaan käytöstä" : "poistetaan";

        // 1) Kuiva ajo
        IReadOnlyList<CleanResult> dry;
        var cts = BeginBusy($"Kuiva ajo: {module.DisplayName}");
        try
        {
            dry = await _engine.CleanAsync(items, CreateContext(dryRun: true, cts), cts.Token);
        }
        catch (OperationCanceledException)
        {
            _log.Warning("Kuiva ajo peruutettu.");
            return;
        }
        finally
        {
            EndBusy();
        }

        var dryRemoved = dry.Sum(r => r.Removed.Count);
        var dryBytes = dry.Sum(r => r.FreedBytes);
        var drySkipped = dry.SelectMany(r => r.Skipped).ToList();
        var dryErrors = dry.SelectMany(r => r.Errors).ToList();

        var summary = $"Kuivassa ajossa {verb} {dryRemoved} kohdetta" +
                      (dryBytes > 0 ? $" ({ByteFormatter.Format(dryBytes)})" : "") + ".";
        if (drySkipped.Count > 0)
        {
            var reasons = drySkipped.GroupBy(s => s.Reason)
                .OrderByDescending(g => g.Count())
                .Take(4)
                .Select(g => $"  • {g.Count()} kpl: {g.Key}");
            summary += $"\n\nOhitettaisiin {drySkipped.Count} kohdetta:\n{string.Join("\n", reasons)}";
        }
        if (dryErrors.Count > 0)
            summary += $"\n\nVirheet:\n{string.Join("\n", dryErrors.Take(4))}";

        if (dryRemoved == 0)
        {
            await _dialogs.InfoAsync("Ei mitään tehtävää", summary);
            return;
        }

        // 2) Yhteenveto kuivasta ajosta
        if (!await _dialogs.ConfirmAsync("Kuivan ajon yhteenveto", summary, "Jatka"))
            return;

        // 3) Toinen vahvistus
        var confirmText = isStartup
            ? $"Poistetaanko {dryRemoved} käynnistyskohdetta käytöstä?\n\nMitään ei poisteta. Palautus onnistuu Tehtävienhallinnan Käynnistys-välilehdeltä."
            : $"Poistetaanko {dryRemoved} kohdetta pysyvästi" + (dryBytes > 0 ? $" ({ByteFormatter.Format(dryBytes)})" : "") +
              "?\n\nTiedostot eivät mene roskakoriin. Kaikki poistot kirjataan lokiin.";

        if (!await _dialogs.ConfirmAsync("Vahvista", confirmText, isStartup ? "Poista käytöstä" : "Poista pysyvästi"))
            return;

        // 4) Oikea ajo
        IReadOnlyList<CleanResult> real;
        cts = BeginBusy($"Puhdistetaan: {module.DisplayName}");
        try
        {
            real = await _engine.CleanAsync(items, CreateContext(dryRun: false, cts), cts.Token);
        }
        catch (OperationCanceledException)
        {
            _log.Warning("Puhdistus peruutettu kesken. Osa kohteista on jo käsitelty, katso loki.");
            await _dialogs.InfoAsync("Peruutettu", "Puhdistus keskeytettiin. Jo poistetut kohteet näkyvät lokissa.");
            await ScanModulesAsync(new[] { module });
            return;
        }
        finally
        {
            EndBusy();
        }

        var removed = real.Sum(r => r.Removed.Count);
        var freed = real.Sum(r => r.FreedBytes);
        var skipped = real.Sum(r => r.Skipped.Count);
        var resultText = $"{char.ToUpper(verb[0])}{verb[1..]} {removed} kohdetta" +
                         (freed > 0 ? $", vapautettu {ByteFormatter.Format(freed)}" : "") +
                         (skipped > 0 ? $". Ohitettu {skipped} (katso loki)." : ".");
        await _dialogs.InfoAsync("Valmis", resultText);

        // Tulos vanheni: skannataan moduuli uudelleen.
        await ScanModulesAsync(new[] { module });
    }

    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
        ProgressMessage = "Peruutetaan...";
    }

    [RelayCommand]
    private async Task RestartElevatedAsync()
    {
        if (Elevation.TryRestartElevated())
        {
            Microsoft.UI.Xaml.Application.Current.Exit();
            return;
        }
        await _dialogs.InfoAsync("Ei käynnistetty", "Järjestelmänvalvojan oikeuksia ei myönnetty. Ohjelma jatkaa tavallisena käyttäjänä.");
    }

    [RelayCommand]
    private void ClearLog() => LogLines.Clear();

    [RelayCommand]
    private async Task RestoreRegistryBackupAsync()
    {
        var backups = RegistryBackup.List();
        if (backups.Count == 0)
        {
            await _dialogs.InfoAsync("Ei varmuuskopioita", $"Rekisterivarmuuskopioita ei ole kansiossa\n{RegistryBackup.DefaultRoot}");
            return;
        }

        var options = backups.Select(b => $"{b.Timestamp}  ({b.FileCount} tiedostoa, {b.Summary})").ToList();
        var chosen = await _dialogs.PickAsync(
            "Palauta rekisterivarmuuskopio",
            "Valitun ajon kaikki .reg-tiedostot tuodaan takaisin rekisteriin (reg import). HKLM-avaimet vaativat järjestelmänvalvojan.",
            options, "Palauta");
        if (chosen is null) return;

        var backup = backups[options.IndexOf(chosen)];
        var cts = BeginBusy($"Palautetaan: {backup.Timestamp}");
        RegistryBackup.RestoreResult result;
        try
        {
            result = await Task.Run(() => RegistryBackup.Restore(backup.Timestamp, _log), cts.Token);
        }
        finally
        {
            EndBusy();
        }

        var text = $"Palautettu {result.Imported} tiedostoa, epäonnistui {result.Failed}.";
        if (result.Messages.Count > 0) text += "\n\n" + string.Join("\n", result.Messages.Take(5));
        await _dialogs.InfoAsync("Palautus valmis", text);

        if (SelectedModule is { Id: "registry" } module)
            await ScanModulesAsync(new[] { module });
    }

    /// <summary>Kutsutaan, kun valitun moduulin valinta muuttuu, jotta Puhdista-napin tila päivittyy.</summary>
    public void NotifySelectionChanged() => CleanCommand.NotifyCanExecuteChanged();

    partial void OnSelectedModuleChanged(ModuleViewModel? oldValue, ModuleViewModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnModulePropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnModulePropertyChanged;
        IsRegistrySelected = newValue?.Id == "registry";
        IsDiskUsageSelected = newValue?.Id == "diskusage";
    }

    private void OnModulePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ModuleViewModel.SelectedCount) or nameof(ModuleViewModel.HasScanned))
            CleanCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _fileLog.Dispose();
    }
}

using System.Collections.ObjectModel;
using Cleaner.Core.Engine;
using Cleaner.Core.Logging;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using Cleaner.Modules.DiskUsage;
using Cleaner.Modules.Uninstall;
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
    [NotifyCanExecuteChangedFor(nameof(RefreshProgramsCommand), nameof(UninstallProgramCommand), nameof(ScanLeftoversCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial int ProgressPercent { get; set; }

    [ObservableProperty]
    public partial string ProgressMessage { get; set; }

    [ObservableProperty]
    public partial string BusyTitle { get; set; }

    /// <summary>Rekisterimoduulin sivulla näytetään palautusnappi.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRestoreButton))]
    public partial bool IsRegistrySelected { get; set; }

    /// <summary>Ohjelman poisto -sivulla näytetään ohjelmalista, haku ja poistonapit (CON-7: moduuli ajetaan vain omalta sivultaan).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRestoreButton))]
    public partial bool IsUninstallSelected { get; set; }

    /// <summary>Palauta varmuuskopio... -nappi: rekisteri ja ohjelman poisto käyttävät samoja varmuuskopiosessioita.</summary>
    public bool ShowRestoreButton => IsRegistrySelected || IsUninstallSelected;

    /// <summary>Ohjelman poisto -sivun lista, suodatettuna ProgramFilter-tekstillä.</summary>
    public ObservableCollection<InstalledProgramViewModel> Programs { get; } = new();

    /// <summary>Kaikki luetut ohjelmat. Programs rakennetaan tästä suodattimen muuttuessa.</summary>
    private List<InstalledProgram> _allPrograms = new();
    private bool _loadingPrograms;

    /// <summary>Hakuteksti: osamerkkijono nimestä tai julkaisijasta.</summary>
    [ObservableProperty]
    public partial string ProgramFilter { get; set; }

    /// <summary>Ohjelmalistan järjestys ComboBoxin indeksinä: 0 = nimi, 1 = asennuspäivä uusin ensin.</summary>
    [ObservableProperty]
    public partial int ProgramSortIndex { get; set; }

    private ProgramSortOrder ProgramSort => ProgramSortIndex == 1 ? ProgramSortOrder.InstallDateDescending : ProgramSortOrder.Name;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UninstallProgramCommand), nameof(ScanLeftoversCommand))]
    public partial InstalledProgramViewModel? SelectedProgram { get; set; }

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
        ProgramFilter = "";
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

                // Ohjelman poisto: kohde asetetaan Etsi jäänteet- tai Poista ohjelma -napilla ennen skannausta.
                if (target.Module is UninstallModule { Target: null })
                {
                    target.StatusText = "Valitse ohjelma listasta ja paina Etsi jäänteet tai Poista ohjelma.";
                    continue;
                }

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
        var isUninstall = module.Id == "uninstall";
        var isProcesses = module.Id == "processes";
        var verb = isUninstall ? "siirretään karanteeniin" : isStartup ? "poistetaan käytöstä" : isProcesses ? "lopetetaan" : "poistetaan";

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
        var confirmText = isUninstall
            ? $"Siirretäänkö {dryRemoved} kohdetta karanteeniin?\n\nKansiot ja tiedostot siirretään kansioon %LOCALAPPDATA%\\PcCleaner\\quarantine, rekisteriavaimet viedään .reg-varmuuskopioon. Palautus: Palauta varmuuskopio... tai pccleaner restore <aikaleima>."
            : isStartup
            ? $"Poistetaanko {dryRemoved} käynnistyskohdetta käytöstä?\n\nMitään ei poisteta. Palautus onnistuu Tehtävienhallinnan Käynnistys-välilehdeltä."
            : isProcesses
            ? $"Lopetetaanko {dryRemoved} prosessia" + (dryBytes > 0 ? $" ({ByteFormatter.Format(dryBytes)})" : "") +
              "?\n\nTallentamattomat tiedot katoavat, jos ohjelma on auki. Mitään ei poisteta levyltä. Ohjelman voi käynnistää uudelleen tavallisesti."
            : $"Poistetaanko {dryRemoved} kohdetta pysyvästi" + (dryBytes > 0 ? $" ({ByteFormatter.Format(dryBytes)})" : "") +
              "?\n\nTiedostot eivät mene roskakoriin. Kaikki poistot kirjataan lokiin.";
        var confirmButton = isUninstall ? "Siirrä karanteeniin" : isStartup ? "Poista käytöstä" : isProcesses ? "Lopeta prosessit" : "Poista pysyvästi";

        if (!await _dialogs.ConfirmAsync("Vahvista", confirmText, confirmButton))
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
                         (freed > 0 ? (isProcesses ? $", vapautettu muistia {ByteFormatter.Format(freed)}" : $", vapautettu {ByteFormatter.Format(freed)}") : "") +
                         (skipped > 0 ? $". Ohitettu {skipped} (katso loki)." : ".");
        if (module.Module is UninstallModule uninstall)
        {
            if (uninstall.LastQuarantineDirectory is not null)
                resultText += $"\n\nKaranteeni: {uninstall.LastQuarantineDirectory}";
            if (uninstall.LastBackupTimestamp is not null)
                resultText += $"\nPalautus: pccleaner restore {uninstall.LastBackupTimestamp}";
        }
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
    private async Task RestoreBackupAsync()
    {
        var sessions = BackupSessions.List();
        if (sessions.Count == 0)
        {
            await _dialogs.InfoAsync("Ei varmuuskopioita",
                $"Varmuuskopioita ei ole kansioissa\n{RegistryBackup.DefaultRoot}\n{Quarantine.DefaultRoot}");
            return;
        }

        var options = sessions.Select(s => $"{s.Timestamp}  ({s.Summary})").ToList();
        var chosen = await _dialogs.PickAsync(
            "Palauta varmuuskopio",
            "Valitun ajon .reg-tiedostot tuodaan takaisin rekisteriin ja karanteenin kansiot siirretään alkuperäisiin paikkoihinsa. HKLM-avaimet ja Program Files vaativat järjestelmänvalvojan.",
            options, "Palauta");
        if (chosen is null) return;

        var session = sessions[options.IndexOf(chosen)];
        var cts = BeginBusy($"Palautetaan: {session.Timestamp}");
        BackupSessions.RestoreResult result;
        try
        {
            result = await Task.Run(() => BackupSessions.Restore(session.Timestamp, _log), cts.Token);
        }
        finally
        {
            EndBusy();
        }

        var text = $"Rekisteri: palautettu {result.RegistryImported}, epäonnistui {result.RegistryFailed}. " +
                   $"Karanteeni: palautettu {result.QuarantineRestored}, ohitettu {result.QuarantineSkipped}.";
        if (result.Messages.Count > 0) text += "\n\n" + string.Join("\n", result.Messages.Take(5));
        await _dialogs.InfoAsync("Palautus valmis", text);

        if (SelectedModule is { Id: "registry" } module)
            await ScanModulesAsync(new[] { module });
        else if (SelectedModule is { Module: UninstallModule { Target: not null } } uninstallVm)
            await ScanModulesAsync(new[] { uninstallVm });
    }

    // ---------- Ohjelman poisto ----------

    private bool CanRefreshPrograms() => !IsBusy;

    /// <summary>Lukee asennetut ohjelmat rekisteristä taustasäikeessä ja säilyttää valinnan, jos ohjelma on yhä listalla.</summary>
    [RelayCommand(CanExecute = nameof(CanRefreshPrograms))]
    private async Task RefreshProgramsAsync()
    {
        if (_loadingPrograms) return;
        if (Modules.FirstOrDefault(m => m.Module is UninstallModule)?.Module is not UninstallModule module) return;

        _loadingPrograms = true;
        try
        {
            var programs = await Task.Run(() => module.ListPrograms());
            _allPrograms = programs.ToList();
            ApplyProgramFilter();
        }
        catch (Exception ex)
        {
            _log.Error($"Ohjelmalistan luku epäonnistui: {ex}");
            await _dialogs.InfoAsync("Ohjelmalistaa ei voitu lukea", ex.Message);
        }
        finally
        {
            _loadingPrograms = false;
        }
    }

    partial void OnProgramFilterChanged(string value) => ApplyProgramFilter();

    partial void OnProgramSortIndexChanged(int value) => ApplyProgramFilter();

    private void ApplyProgramFilter()
    {
        var selectedKey = SelectedProgram?.Program.KeyPath;
        var filter = ProgramFilter.Trim();

        Programs.Clear();
        foreach (var program in InstalledProgramReader.Sort(_allPrograms, ProgramSort))
        {
            if (filter.Length == 0 ||
                program.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (program.Publisher?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
                Programs.Add(new InstalledProgramViewModel(program));
        }

        SelectedProgram = selectedKey is null
            ? null
            : Programs.FirstOrDefault(p => string.Equals(p.Program.KeyPath, selectedKey, StringComparison.OrdinalIgnoreCase));
    }

    private bool CanActOnProgram() => !IsBusy && SelectedProgram is not null;

    [RelayCommand(CanExecute = nameof(CanActOnProgram))]
    private async Task ScanLeftoversAsync()
    {
        if (SelectedProgram is not { } selected) return;
        if (SelectedModule is not { Module: UninstallModule module } vm) return;

        module.Target = selected.Program;
        module.TargetKeyRemoved = !RegistryPaths.KeyExists(selected.Program.KeyPath);
        await ScanModulesAsync(new[] { vm });
    }

    [RelayCommand(CanExecute = nameof(CanActOnProgram))]
    private async Task UninstallProgramAsync()
    {
        if (SelectedProgram is not { } selected) return;
        if (SelectedModule is not { Module: UninstallModule module } vm) return;

        var program = selected.Program;
        var name = program.Name;
        if (!await _dialogs.ConfirmAsync("Poista ohjelma",
                $"Käynnistetäänkö ohjelman '{name}' oma poisto-ohjelma? Sen jälkeen jäänteet etsitään automaattisesti.",
                "Käynnistä"))
            return;

        UninstallRunResult result;
        var cts = BeginBusy($"Odotetaan poisto-ohjelmaa: {name}");
        ProgressMessage = "Poisto-ohjelma on käynnissä. Peruuta lopettaa vain odotuksen.";
        try
        {
            result = await module.RunUninstallerAsync(program, quiet: false, CreateContext(dryRun: true, cts), cts.Token);
        }
        catch (OperationCanceledException)
        {
            EndBusy();
            _log.Warning($"Poisto-ohjelman odotus lopetettu: {name}");
            await _dialogs.InfoAsync("Odotus lopetettu",
                "Poisto-ohjelmaa ei keskeytetty. Voit etsiä jäänteet myöhemmin painikkeella Etsi jäänteet.");
            return;
        }
        catch (Exception ex)
        {
            EndBusy();
            _log.Error($"Poisto-ohjelman ajo epäonnistui: {ex}");
            await _dialogs.InfoAsync("Poisto-ohjelma ei käynnistynyt", ex.Message + "\n\nVoit silti etsiä jäänteet.");
            return;
        }
        finally
        {
            EndBusy();
        }

        if (result.Error is not null)
        {
            await _dialogs.InfoAsync("Poisto-ohjelma ei käynnistynyt", result.Error + "\n\nVoit silti etsiä jäänteet.");
            return;
        }

        module.Target = program;
        module.TargetKeyRemoved = result.KeyRemoved;
        await ScanModulesAsync(new[] { vm });
        await RefreshProgramsAsync();
    }

    /// <summary>Kutsutaan, kun valitun moduulin valinta muuttuu, jotta Puhdista-napin tila päivittyy.</summary>
    public void NotifySelectionChanged() => CleanCommand.NotifyCanExecuteChanged();

    partial void OnSelectedModuleChanged(ModuleViewModel? oldValue, ModuleViewModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnModulePropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnModulePropertyChanged;
        IsRegistrySelected = newValue?.Id == "registry";
        IsDiskUsageSelected = newValue?.Id == "diskusage";
        IsUninstallSelected = newValue?.Id == "uninstall";

        // Ohjelmalista luetaan vasta, kun sivu avataan ensimmäisen kerran.
        if (IsUninstallSelected && _allPrograms.Count == 0)
            _ = RefreshProgramsAsync();
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

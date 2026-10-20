using System.Collections.ObjectModel;
using Cleaner.Ai;
using Cleaner.Core.Engine;
using Cleaner.Core.Localization;
using Cleaner.Core.Logging;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using Cleaner.Modules.DiskUsage;
using Cleaner.Modules.Uninstall;
using Cleaner.UI.Services;
using Cleaner.UI.Theming;
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
    private readonly DispatcherQueue _dispatcher;
    private CancellationTokenSource? _cts;

    public ObservableCollection<ModuleViewModel> Modules { get; } = new();
    public ObservableCollection<string> LogLines => _uiLog.Lines;

    /// <summary>MainWindow.xaml-tiedoston kiinteät tekstit nykyisellä kielellä.</summary>
    public UiText T { get; } = new();

    /// <summary>Laukeaa UI-säikeessä, kun kielestä riippuvat tekstit on päivitetty (ikkunan otsikko, treemap).</summary>
    public event EventHandler? LanguageRefreshed;

    public bool IsElevated { get; }
    public bool ShowElevationHint => !IsElevated;
    public string TitleText => IsElevated ? Loc.T("PC Cleaner (administrator)", "PC Cleaner (järjestelmänvalvoja)") : "PC Cleaner";
    public string LogPath => _fileLog.FilePath;

    /// <summary>
    /// Kielivalitsimen indeksi: 0 = English, 1 = Suomi. Valitsimen alkuvalinta kirjoittaa saman arvon takaisin,
    /// joten tallennus tehdään vain, kun kieli todella vaihtuu (settings.json syntyy vasta käyttäjän vaihdosta).
    /// </summary>
    public int LanguageIndex
    {
        get => Loc.Current == Language.Fi ? 1 : 0;
        set
        {
            if (value is not (0 or 1)) return;
            var language = value == 1 ? Language.Fi : Language.En;
            if (language == Loc.Current) return;

            // Kieli vaihtuu tälle istunnolle, vaikka tallennus epäonnistuisi.
            Loc.Current = language;
            try
            {
                // Update säilyttää tiedoston muut asetukset (paletti, fontti).
                AppSettings.Update(s => s.Language = Loc.ToCode(language));
            }
            catch (Exception ex)
            {
                _log.Warning(Loc.T($"Saving the language setting failed: {ex.Message}", $"Kielivalinnan tallennus epäonnistui: {ex.Message}"));
            }
        }
    }

    // ---------- Ulkoasu ----------

    /// <summary>Asetussivun palettilista <see cref="Appearance.Palettes"/>-järjestyksessä.</summary>
    public ObservableCollection<AppearanceOptionViewModel> PaletteOptions { get; } = new();

    /// <summary>Asetussivun fonttilista <see cref="Appearance.Fonts"/>-järjestyksessä.</summary>
    public ObservableCollection<AppearanceOptionViewModel> FontOptions { get; } = new();

    /// <summary>
    /// Laukeaa UI-säikeessä, kun paletti tai fontti on vaihdettu ja resurssit korvattu.
    /// Näkymä luo itsensä uudelleen, jotta uudet {ThemeResource}-arvot tulevat käyttöön.
    /// </summary>
    public event EventHandler? AppearanceChanged;

    /// <summary>Nykyisen paletin indeksi. Sama tai listan ulkopuolinen arvo ei tee mitään (valitsimen alkuvalinta).</summary>
    public int PaletteIndex
    {
        get => IndexOf(Appearance.Palettes, p => p.Id == Appearance.CurrentPalette.Id);
        set
        {
            if (value < 0 || value >= Appearance.Palettes.Count || value == PaletteIndex) return;
            var palette = Appearance.Palettes[value];
            Appearance.Apply(palette, Appearance.CurrentFont);
            SaveAppearance(s => s.Palette = palette.Id);
            OnPropertyChanged();
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Nykyisen fontin indeksi. Sama tai listan ulkopuolinen arvo ei tee mitään (valitsimen alkuvalinta).</summary>
    public int FontIndex
    {
        get => IndexOf(Appearance.Fonts, f => f.Id == Appearance.CurrentFont.Id);
        set
        {
            if (value < 0 || value >= Appearance.Fonts.Count || value == FontIndex) return;
            var font = Appearance.Fonts[value];
            Appearance.Apply(Appearance.CurrentPalette, font);
            SaveAppearance(s => s.Font = font.Id);
            OnPropertyChanged();
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Ulkoasu vaihtuu tälle istunnolle, vaikka tallennus epäonnistuisi.</summary>
    private void SaveAppearance(Action<AppSettings> change)
    {
        try
        {
            AppSettings.Update(change);
        }
        catch (Exception ex)
        {
            _log.Warning(Loc.T($"Saving the appearance setting failed: {ex.Message}", $"Ulkoasuasetuksen tallennus epäonnistui: {ex.Message}"));
        }
    }

    private static int IndexOf<T>(IReadOnlyList<T> list, Func<T, bool> match)
    {
        for (var i = 0; i < list.Count; i++)
            if (match(list[i])) return i;
        return -1;
    }

    /// <summary>Asetussivu näkyy sisältöalueella. Valittu moduuli säilyy ennallaan.</summary>
    [ObservableProperty]
    public partial bool IsSettingsSelected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanScanAll))]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(CleanCommand), nameof(ScanAllCommand))]
    public partial ModuleViewModel? SelectedModule { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(CanScanAll))]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(CleanCommand), nameof(ScanAllCommand), nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshProgramsCommand), nameof(UninstallProgramCommand), nameof(ScanLeftoversCommand))]
    [NotifyCanExecuteChangedFor(nameof(AutoCleanCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>Avustajan komennot ovat toisessa oliossa, joten NotifyCanExecuteChangedFor ei yllä niihin.</summary>
    partial void OnIsBusyChanged(bool value) => Ai.OnMainBusyChanged();

    /// <summary>Tekoälyavustajan paneeli (REQ-12).</summary>
    public AiAssistantViewModel Ai { get; }

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
        _dispatcher = dispatcher;
        ProgressMessage = "";
        BusyTitle = "";
        DiskUsagePath = "";
        DiskUsageTop = 20;
        ProgramFilter = "";
        IsElevated = ElevationHelper.IsElevated();

        _fileLog = new FileLogger(minLevel: LogLevel.Debug);
        _uiLog = new UiLogger(dispatcher, LogLevel.Info);
        _log = new CompositeLogger(_uiLog, _fileLog);

        // Ei API-kutsua käynnistyksessä: avain tarkistetaan ympäristöstä ja asiakas luodaan ensimmäisellä kutsulla.
        Ai = new AiAssistantViewModel(this, _log, dispatcher, () => new AnthropicBackend(),
            AnthropicBackend.IsConfigured, AppSettings.Load());

        var rulesDir = Path.Combine(AppContext.BaseDirectory, "rules");
        _engine = new CleanerEngine(UiModuleFactory.CreateAll(rulesDir, _log));

        foreach (var module in _engine.Modules)
            Modules.Add(new ModuleViewModel(module));

        foreach (var palette in Appearance.Palettes)
            PaletteOptions.Add(AppearanceOptionViewModel.ForPalette(palette));
        foreach (var font in Appearance.Fonts)
            FontOptions.Add(AppearanceOptionViewModel.ForFont(font));

        _log.Info(Loc.T(
            $"PC Cleaner started ({(IsElevated ? "administrator" : "standard user")}). Log: {_fileLog.FilePath}",
            $"PC Cleaner käynnistyi ({(IsElevated ? "järjestelmänvalvoja" : "tavallinen käyttäjä")}). Loki: {_fileLog.FilePath}"));

        // Staattinen tapahtuma: tilaus perutaan Dispose-kutsussa (ikkunan Closed).
        Loc.Changed += OnLanguageChanged;
    }

    // ---------- Kieli ----------

    /// <summary>Loc.Changed voi tulla mistä säikeestä tahansa, joten päivitys ajetaan UI-säikeessä.</summary>
    private void OnLanguageChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(RefreshLanguage);

    /// <summary>
    /// Päivittää kaikki kielestä riippuvat tekstit ilman uudelleenkäynnistystä. Jo tuotetut moduulitulokset
    /// ja kirjoitetut lokirivit jäävät kielelle, jolla ne syntyivät.
    /// </summary>
    private void RefreshLanguage()
    {
        T.Refresh();
        OnPropertyChanged(nameof(TitleText));
        OnPropertyChanged(nameof(LanguageIndex));
        foreach (var module in Modules)
            module.RefreshLanguage();
        foreach (var program in Programs)
            program.RefreshLanguage();
        foreach (var option in PaletteOptions.Concat(FontOptions))
            option.RefreshLanguage();
        OnPropertyChanged(nameof(PaletteIndex));
        OnPropertyChanged(nameof(FontIndex));
        Ai.RefreshLanguage();
        LanguageRefreshed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Treemapin otsikkorivin polku: useamman aseman yhdistetty juuri nimetään nykyisellä kielellä.</summary>
    public string TreemapNodeLabel(DirectoryNode node) =>
        _treemapCombined && ReferenceEquals(node, TreemapRoot) ? Loc.T("All drives", "Kaikki asemat") : node.Path;

    /// <summary>TreemapRoot on DirectoryNode.Combine-kutsun luoma keinotekoinen solmu (useampi asema).</summary>
    private bool _treemapCombined;

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
        var cts = BeginBusy(Loc.T("Scanning", "Skannataan"));
        try
        {
            await ScanModulesCoreAsync(targets, cts);
        }
        finally
        {
            EndBusy();
        }
    }

    /// <summary>
    /// Skannaa kohteet järjestyksessä ilman BeginBusy/EndBusy-kutsuja (automaattinen siivous ajaa useamman vaiheen
    /// saman kiireisen tilan alla). Palauttaa true vain, kun kaikki kohteet skannattiin loppuun; peruutus ja virhe
    /// palauttavat false. Peruutetun skannauksen jäljelle jääneet moduulit säilyttävät vanhan tuloksen, joten
    /// peruutus todetaan paluuarvosta eikä HasScanned-tilasta.
    /// </summary>
    private async Task<bool> ScanModulesCoreAsync(IReadOnlyList<ModuleViewModel> targets, CancellationTokenSource cts)
    {
        try
        {
            foreach (var target in targets)
            {
                cts.Token.ThrowIfCancellationRequested();
                BusyTitle = Loc.T($"Scanning: {target.DisplayName}", $"Skannataan: {target.DisplayName}");
                target.ClearScan();

                // Ohjelman poisto: kohde asetetaan Etsi jäänteet- tai Poista ohjelma -napilla ennen skannausta.
                if (target.Module is UninstallModule { Target: null })
                {
                    target.Status = ModuleScanStatus.SelectProgram;
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
                {
                    // Yhdistetyn juuren nimi lasketaan piirrettäessä (TreemapNodeLabel), jotta se seuraa kieltä.
                    _treemapCombined = scanned.LastTrees.Count > 1;
                    TreemapRoot = DirectoryNode.Combine(scanned.LastTrees, Loc.T("All drives", "Kaikki asemat"));
                }
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            _log.Warning(Loc.T("Scan canceled.", "Skannaus peruutettu."));
            foreach (var target in targets.Where(t => !t.HasScanned))
                target.Status = ModuleScanStatus.ScanCanceled;
            return false;
        }
        catch (Exception ex)
        {
            _log.Error(Loc.T($"Scan failed: {ex}", $"Skannaus epäonnistui: {ex}"));
            await _dialogs.InfoAsync(Loc.T("Scan failed", "Skannaus epäonnistui"), ex.Message);
            return false;
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
            await _dialogs.InfoAsync(Loc.T("No items selected", "Ei valittuja kohteita"),
                Loc.T("Select items with the checkboxes first.", "Valitse ensin kohteet valintaruuduista."));
            return;
        }

        var isStartup = module.Id == "startup";
        var isUninstall = module.Id == "uninstall";
        var isProcesses = module.Id == "processes";
        // Suomen lauseet rakennetaan verbistä; englanniksi kirjoitetaan kokonaiset lauseet moduulityypin mukaan.
        var verb = isUninstall ? "siirretään karanteeniin" : isStartup ? "poistetaan käytöstä" : isProcesses ? "lopetetaan" : "poistetaan";

        // 1) Kuiva ajo
        IReadOnlyList<CleanResult> dry;
        var cts = BeginBusy(Loc.T($"Dry run: {module.DisplayName}", $"Kuiva ajo: {module.DisplayName}"));
        try
        {
            dry = await _engine.CleanAsync(items, CreateContext(dryRun: true, cts), cts.Token);
        }
        catch (OperationCanceledException)
        {
            _log.Warning(Loc.T("Dry run canceled.", "Kuiva ajo peruutettu."));
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

        var drySize = dryBytes > 0 ? $" ({ByteFormatter.Format(dryBytes)})" : "";
        var dryEn = isUninstall ? $"The dry run would move {dryRemoved} items to quarantine"
            : isStartup ? $"The dry run would disable {dryRemoved} items"
            : isProcesses ? $"The dry run would end {dryRemoved} processes"
            : $"The dry run would delete {dryRemoved} items";
        var summary = Loc.T(dryEn, $"Kuivassa ajossa {verb} {dryRemoved} kohdetta") + drySize + ".";
        summary += SkippedAndErrorsText(drySkipped, dryErrors);

        if (dryRemoved == 0)
        {
            await _dialogs.InfoAsync(Loc.T("Nothing to do", "Ei mitään tehtävää"), summary);
            return;
        }

        // 2) Yhteenveto kuivasta ajosta
        if (!await _dialogs.ConfirmAsync(Loc.T("Dry run summary", "Kuivan ajon yhteenveto"), summary, Loc.T("Continue", "Jatka")))
            return;

        // 3) Toinen vahvistus
        var confirmText = isUninstall
            ? Loc.T(
                $"Move {dryRemoved} items to quarantine?\n\nFolders and files are moved to %LOCALAPPDATA%\\PcCleaner\\quarantine and registry keys are exported to a .reg backup. To restore: Restore backup... or pccleaner restore <timestamp>.",
                $"Siirretäänkö {dryRemoved} kohdetta karanteeniin?\n\nKansiot ja tiedostot siirretään kansioon %LOCALAPPDATA%\\PcCleaner\\quarantine, rekisteriavaimet viedään .reg-varmuuskopioon. Palautus: Palauta varmuuskopio... tai pccleaner restore <aikaleima>.")
            : isStartup
            ? Loc.T(
                $"Disable {dryRemoved} startup items?\n\nNothing is deleted. You can re-enable them on the Startup apps tab of Task Manager.",
                $"Poistetaanko {dryRemoved} käynnistyskohdetta käytöstä?\n\nMitään ei poisteta. Palautus onnistuu Tehtävienhallinnan Käynnistys-välilehdeltä.")
            : isProcesses
            ? Loc.T($"End {dryRemoved} processes", $"Lopetetaanko {dryRemoved} prosessia") + drySize +
              Loc.T(
                  "?\n\nUnsaved data is lost if the program is open. Nothing is deleted from disk. You can start the program again as usual.",
                  "?\n\nTallentamattomat tiedot katoavat, jos ohjelma on auki. Mitään ei poisteta levyltä. Ohjelman voi käynnistää uudelleen tavallisesti.")
            : Loc.T($"Permanently delete {dryRemoved} items", $"Poistetaanko {dryRemoved} kohdetta pysyvästi") + drySize +
              Loc.T(
                  "?\n\nFiles do not go to the Recycle Bin. All deletions are written to the log.",
                  "?\n\nTiedostot eivät mene roskakoriin. Kaikki poistot kirjataan lokiin.");
        var confirmButton = isUninstall ? Loc.T("Move to quarantine", "Siirrä karanteeniin")
            : isStartup ? Loc.T("Disable", "Poista käytöstä")
            : isProcesses ? Loc.T("End processes", "Lopeta prosessit")
            : Loc.T("Delete permanently", "Poista pysyvästi");

        if (!await _dialogs.ConfirmAsync(Loc.T("Confirm", "Vahvista"), confirmText, confirmButton))
            return;

        // 4) Oikea ajo. Avustajan arvio voi valmistua dialogien aikana ja poistaa valintoja (Auto-tila, EDGE-11):
        // ajetaan vain kohteet, jotka ovat yhä valittuina. Suodatus voi vain vähentää vahvistettuja kohteita.
        var stillSelected = module.SelectedItems().ToHashSet();
        items = items.Where(stillSelected.Contains).ToList();
        if (items.Count == 0)
        {
            await _dialogs.InfoAsync(Loc.T("Nothing to do", "Ei mitään tehtävää"),
                Loc.T("No items are selected anymore.", "Yhtään kohdetta ei ole enää valittuna."));
            return;
        }
        IReadOnlyList<CleanResult> real;
        cts = BeginBusy(Loc.T($"Cleaning: {module.DisplayName}", $"Puhdistetaan: {module.DisplayName}"));
        try
        {
            real = await _engine.CleanAsync(items, CreateContext(dryRun: false, cts), cts.Token);
        }
        catch (OperationCanceledException)
        {
            _log.Warning(Loc.T(
                "Cleaning canceled midway. Some items were already processed, see the log.",
                "Puhdistus peruutettu kesken. Osa kohteista on jo käsitelty, katso loki."));
            await _dialogs.InfoAsync(Loc.T("Canceled", "Peruutettu"), Loc.T(
                "Cleaning was interrupted. Items already removed are listed in the log.",
                "Puhdistus keskeytettiin. Jo poistetut kohteet näkyvät lokissa."));
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
        var resultEn = isUninstall ? $"Moved {removed} items to quarantine"
            : isStartup ? $"Disabled {removed} items"
            : isProcesses ? $"Ended {removed} processes"
            : $"Deleted {removed} items";
        var freedText = freed > 0 ? ByteFormatter.Format(freed) : "";
        var resultText = Loc.T(resultEn, $"{char.ToUpper(verb[0])}{verb[1..]} {removed} kohdetta") +
                         (freed > 0
                             ? (isProcesses
                                 ? Loc.T($", freed {freedText} of memory", $", vapautettu muistia {freedText}")
                                 : Loc.T($", freed {freedText}", $", vapautettu {freedText}"))
                             : "") +
                         (skipped > 0 ? Loc.T($". Skipped {skipped} (see the log).", $". Ohitettu {skipped} (katso loki).") : ".");
        if (module.Module is UninstallModule uninstall)
        {
            if (uninstall.LastQuarantineDirectory is not null)
                resultText += Loc.T($"\n\nQuarantine: {uninstall.LastQuarantineDirectory}", $"\n\nKaranteeni: {uninstall.LastQuarantineDirectory}");
            if (uninstall.LastBackupTimestamp is not null)
                resultText += Loc.T($"\nRestore: pccleaner restore {uninstall.LastBackupTimestamp}", $"\nPalautus: pccleaner restore {uninstall.LastBackupTimestamp}");
        }
        await _dialogs.InfoAsync(Loc.T("Done", "Valmis"), resultText);

        // Tulos vanheni: skannataan moduuli uudelleen.
        await ScanModulesAsync(new[] { module });
    }

    /// <summary>Kuivan ajon ohitetut (enintään 4 yleisintä syytä) ja virheet (enintään 4) vahvistusdialogiin.</summary>
    private static string SkippedAndErrorsText(IReadOnlyList<(CleanItem Item, string Reason)> skipped, IReadOnlyList<string> errors)
    {
        var text = "";
        if (skipped.Count > 0)
        {
            var reasons = skipped.GroupBy(s => s.Reason)
                .OrderByDescending(g => g.Count())
                .Take(4)
                .Select(g => Loc.T($"  • {g.Count()} items: {g.Key}", $"  • {g.Count()} kpl: {g.Key}"));
            text += Loc.T($"\n\n{skipped.Count} items would be skipped:\n{string.Join("\n", reasons)}",
                          $"\n\nOhitettaisiin {skipped.Count} kohdetta:\n{string.Join("\n", reasons)}");
        }
        if (errors.Count > 0)
            text += Loc.T($"\n\nErrors:\n{string.Join("\n", errors.Take(4))}", $"\n\nVirheet:\n{string.Join("\n", errors.Take(4))}");
        return text;
    }

    // ---------- Automaattinen siivous (REQ-19) ----------

    private bool CanAutoClean() => !IsBusy && !Ai.IsThinking && Ai.IsAvailable && Ai.AutoMode;

    /// <summary>
    /// Skannaa oletusmoduulit, pyytää arvion jokaisesta moduulista, jolla on kohteita, soveltaa Auto-tilan valinnat,
    /// ajaa kuivan ajon ja näyttää yhden vahvistusdialogin ennen oikeaa ajoa (CON-4). Vaiheet 1–3 ajetaan yhden
    /// kiireisen tilan ja yhden CancellationTokenSourcen alla, joten alapalkin Peruuta keskeyttää minkä tahansa niistä.
    /// Opt-in-moduulit eivät ole mukana, ja High-riskin kohde ei ole koskaan valittuna (VerdictApplier.AutoSelection).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAutoClean))]
    private async Task AutoCleanAsync()
    {
        var targets = Modules.Where(m => m.SelectedByDefault).ToList();
        var reviewed = new List<(ModuleViewModel Module, AiReview Review)>();
        List<CleanItem> items;
        IReadOnlyList<CleanResult>? dry = null;

        var cts = BeginBusy(Loc.T("Automatic cleanup", "Automaattinen siivous"));
        try
        {
            // 1) Skannaus
            if (!await ScanModulesCoreAsync(targets, cts)) return;

            // 2) Arviot; Auto-tila soveltaa valinnat ReviewModuleAsync-kutsussa.
            foreach (var module in targets.Where(m => m.HasResults))
            {
                cts.Token.ThrowIfCancellationRequested();
                BusyTitle = Loc.T($"Review: {module.DisplayName}", $"Arvio: {module.DisplayName}");
                var review = await Ai.ReviewModuleAsync(module, cts.Token);
                if (review is null) return;   // virhe tai peruutus näkyy paneelissa
                if (!Ai.AutoMode)
                {
                    // Auto-tila kytkettiin pois kesken: valintoja ei sovellettu, joten ei jatketa puhdistukseen.
                    _log.Warning(Loc.T("Automatic cleanup stopped: auto mode was turned off.",
                                       "Automaattinen siivous keskeytettiin: Auto-tila kytkettiin pois."));
                    return;
                }
                reviewed.Add((module, review));
            }

            // 3) Kuiva ajo
            // High-riskin kohde ei ole koskaan mukana, vaikka käyttäjä olisi rastinut sen kesken ajon.
            items = targets.SelectMany(m => m.SelectedItems()).Where(i => i.Risk != RiskLevel.High).ToList();
            if (items.Count > 0)
            {
                BusyTitle = Loc.T("Dry run: automatic cleanup", "Kuiva ajo: automaattinen siivous");
                try
                {
                    dry = await _engine.CleanAsync(items, CreateContext(dryRun: true, cts), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    _log.Warning(Loc.T("Dry run canceled.", "Kuiva ajo peruutettu."));
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _log.Warning(Loc.T("Automatic cleanup canceled.", "Automaattinen siivous peruutettu."));
            return;
        }
        finally
        {
            EndBusy();
        }

        if (dry is null)
        {
            await _dialogs.InfoAsync(Loc.T("Nothing to do", "Ei mitään tehtävää"),
                Loc.T("The assistant did not select anything to delete.", "Avustaja ei valinnut mitään poistettavaksi."));
            return;
        }

        var dryRemoved = dry.Sum(r => r.Removed.Count);
        var dryBytes = dry.Sum(r => r.FreedBytes);
        var details = SkippedAndErrorsText(dry.SelectMany(r => r.Skipped).ToList(), dry.SelectMany(r => r.Errors).ToList());
        var dryText = Loc.T($"The dry run would delete {dryRemoved} items ({ByteFormatter.Format(dryBytes)}).",
                            $"Kuivassa ajossa poistetaan {dryRemoved} kohdetta ({ByteFormatter.Format(dryBytes)}).");

        if (dryRemoved == 0)
        {
            await _dialogs.InfoAsync(Loc.T("Nothing to do", "Ei mitään tehtävää"), dryText + details);
            return;
        }

        // 4) Yksi vahvistus: avustajan yhteenvedot ja kuivan ajon määrät.
        var summaries = string.Join("\n\n", reviewed.Select(r => $"{r.Module.DisplayName}: {r.Review.Summary.Trim()}"));
        var confirmText = (summaries.Length > 0 ? summaries + "\n\n" : "") + dryText + details + "\n\n" +
                          Loc.T("Files do not go to the Recycle Bin. All deletions are written to the log.",
                                "Tiedostot eivät mene roskakoriin. Kaikki poistot kirjataan lokiin.");
        if (!await _dialogs.ConfirmAsync(Loc.T("Automatic cleanup", "Automaattinen siivous"), confirmText,
                Loc.T("Delete permanently", "Poista pysyvästi")))
            return;

        // 5) Oikea ajo, tulos ja uusi skannaus
        IReadOnlyList<CleanResult> real;
        cts = BeginBusy(Loc.T("Cleaning: automatic cleanup", "Puhdistetaan: automaattinen siivous"));
        try
        {
            real = await _engine.CleanAsync(items, CreateContext(dryRun: false, cts), cts.Token);
        }
        catch (OperationCanceledException)
        {
            _log.Warning(Loc.T(
                "Cleaning canceled midway. Some items were already processed, see the log.",
                "Puhdistus peruutettu kesken. Osa kohteista on jo käsitelty, katso loki."));
            await _dialogs.InfoAsync(Loc.T("Canceled", "Peruutettu"), Loc.T(
                "Cleaning was interrupted. Items already removed are listed in the log.",
                "Puhdistus keskeytettiin. Jo poistetut kohteet näkyvät lokissa."));
            await ScanModulesAsync(targets);
            return;
        }
        finally
        {
            EndBusy();
        }

        var removed = real.Sum(r => r.Removed.Count);
        var freed = real.Sum(r => r.FreedBytes);
        var skipped = real.Sum(r => r.Skipped.Count);
        var resultText = Loc.T($"Deleted {removed} items", $"Poistettu {removed} kohdetta") +
                         (freed > 0 ? Loc.T($", freed {ByteFormatter.Format(freed)}", $", vapautettu {ByteFormatter.Format(freed)}") : "") +
                         (skipped > 0 ? Loc.T($". Skipped {skipped} (see the log).", $". Ohitettu {skipped} (katso loki).") : ".");
        await _dialogs.InfoAsync(Loc.T("Done", "Valmis"), resultText);

        await ScanModulesAsync(targets);
    }

    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
        ProgressMessage = Loc.T("Canceling...", "Peruutetaan...");
    }

    [RelayCommand]
    private async Task RestartElevatedAsync()
    {
        if (Elevation.TryRestartElevated())
        {
            Microsoft.UI.Xaml.Application.Current.Exit();
            return;
        }
        await _dialogs.InfoAsync(Loc.T("Not restarted", "Ei käynnistetty"), Loc.T(
            "Administrator rights were not granted. The program continues as a standard user.",
            "Järjestelmänvalvojan oikeuksia ei myönnetty. Ohjelma jatkaa tavallisena käyttäjänä."));
    }

    [RelayCommand]
    private void ClearLog() => LogLines.Clear();

    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        var sessions = BackupSessions.List();
        if (sessions.Count == 0)
        {
            await _dialogs.InfoAsync(Loc.T("No backups", "Ei varmuuskopioita"), Loc.T(
                $"No backups found in the folders\n{RegistryBackup.DefaultRoot}\n{Quarantine.DefaultRoot}",
                $"Varmuuskopioita ei ole kansioissa\n{RegistryBackup.DefaultRoot}\n{Quarantine.DefaultRoot}"));
            return;
        }

        var options = sessions.Select(s => $"{s.Timestamp}  ({s.Summary})").ToList();
        var chosen = await _dialogs.PickAsync(
            Loc.T("Restore backup", "Palauta varmuuskopio"),
            Loc.T(
                "The .reg files of the selected run are imported back into the registry and the quarantined folders are moved back to their original locations. HKLM keys and Program Files require administrator rights.",
                "Valitun ajon .reg-tiedostot tuodaan takaisin rekisteriin ja karanteenin kansiot siirretään alkuperäisiin paikkoihinsa. HKLM-avaimet ja Program Files vaativat järjestelmänvalvojan."),
            options, Loc.T("Restore", "Palauta"));
        if (chosen is null) return;

        var session = sessions[options.IndexOf(chosen)];
        var cts = BeginBusy(Loc.T($"Restoring: {session.Timestamp}", $"Palautetaan: {session.Timestamp}"));
        BackupSessions.RestoreResult result;
        try
        {
            result = await Task.Run(() => BackupSessions.Restore(session.Timestamp, _log), cts.Token);
        }
        finally
        {
            EndBusy();
        }

        var text = Loc.T(
            $"Registry: restored {result.RegistryImported}, failed {result.RegistryFailed}. " +
            $"Quarantine: restored {result.QuarantineRestored}, skipped {result.QuarantineSkipped}.",
            $"Rekisteri: palautettu {result.RegistryImported}, epäonnistui {result.RegistryFailed}. " +
            $"Karanteeni: palautettu {result.QuarantineRestored}, ohitettu {result.QuarantineSkipped}.");
        if (result.Messages.Count > 0) text += "\n\n" + string.Join("\n", result.Messages.Take(5));
        await _dialogs.InfoAsync(Loc.T("Restore complete", "Palautus valmis"), text);

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
            _log.Error(Loc.T($"Reading the program list failed: {ex}", $"Ohjelmalistan luku epäonnistui: {ex}"));
            await _dialogs.InfoAsync(Loc.T("Could not read the program list", "Ohjelmalistaa ei voitu lukea"), ex.Message);
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
        if (!await _dialogs.ConfirmAsync(Loc.T("Uninstall program", "Poista ohjelma"),
                Loc.T(
                    $"Run the uninstaller of '{name}'? Leftovers are searched for automatically afterwards.",
                    $"Käynnistetäänkö ohjelman '{name}' oma poisto-ohjelma? Sen jälkeen jäänteet etsitään automaattisesti."),
                Loc.T("Run", "Käynnistä")))
            return;

        UninstallRunResult result;
        var cts = BeginBusy(Loc.T($"Waiting for uninstaller: {name}", $"Odotetaan poisto-ohjelmaa: {name}"));
        ProgressMessage = Loc.T(
            "The uninstaller is running. Cancel only stops the waiting.",
            "Poisto-ohjelma on käynnissä. Peruuta lopettaa vain odotuksen.");
        try
        {
            result = await module.RunUninstallerAsync(program, quiet: false, CreateContext(dryRun: true, cts), cts.Token);
        }
        catch (OperationCanceledException)
        {
            EndBusy();
            _log.Warning(Loc.T($"Stopped waiting for uninstaller: {name}", $"Poisto-ohjelman odotus lopetettu: {name}"));
            await _dialogs.InfoAsync(Loc.T("Waiting stopped", "Odotus lopetettu"), Loc.T(
                "The uninstaller was not interrupted. You can search for leftovers later with the Find leftovers button.",
                "Poisto-ohjelmaa ei keskeytetty. Voit etsiä jäänteet myöhemmin painikkeella Etsi jäänteet."));
            return;
        }
        catch (Exception ex)
        {
            EndBusy();
            _log.Error(Loc.T($"Running the uninstaller failed: {ex}", $"Poisto-ohjelman ajo epäonnistui: {ex}"));
            await _dialogs.InfoAsync(Loc.T("Uninstaller did not start", "Poisto-ohjelma ei käynnistynyt"),
                ex.Message + Loc.T("\n\nYou can still search for leftovers.", "\n\nVoit silti etsiä jäänteet."));
            return;
        }
        finally
        {
            EndBusy();
        }

        if (result.Error is not null)
        {
            await _dialogs.InfoAsync(Loc.T("Uninstaller did not start", "Poisto-ohjelma ei käynnistynyt"),
                result.Error + Loc.T("\n\nYou can still search for leftovers.", "\n\nVoit silti etsiä jäänteet."));
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
        Ai.OnModuleStateChanged();

        // Ohjelmalista luetaan vasta, kun sivu avataan ensimmäisen kerran.
        if (IsUninstallSelected && _allPrograms.Count == 0)
            _ = RefreshProgramsAsync();
    }

    private void OnModulePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ModuleViewModel.SelectedCount) or nameof(ModuleViewModel.HasScanned))
            CleanCommand.NotifyCanExecuteChanged();
        if (e.PropertyName is nameof(ModuleViewModel.SelectedCount) or nameof(ModuleViewModel.HasScanned) or nameof(ModuleViewModel.HasReview))
            Ai.OnModuleStateChanged();
    }

    public void Dispose()
    {
        Loc.Changed -= OnLanguageChanged;
        Ai.CancelCommand.Execute(null);
        _cts?.Cancel();
        _cts?.Dispose();
        _fileLog.Dispose();
    }
}

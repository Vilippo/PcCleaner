using System.Collections.ObjectModel;
using System.Text;
using Cleaner.Ai;
using Cleaner.Core.Localization;
using Cleaner.Core.Logging;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace Cleaner.UI.ViewModels;

/// <summary>
/// Tekoälyavustajan paneelin tila (REQ-12–REQ-18): keskustelu, arvio valitun moduulin tuloksista, koneen kunto,
/// vapaat kysymykset ja Auto-tila. Kutsut awaitataan UI-säikeessä; suoratoiston deltat tulevat taustasäikeestä ja
/// viedään viestiin <see cref="DispatcherQueue.TryEnqueue(DispatcherQueueHandler)"/>-kutsulla (CON-9).
/// Kahta avustajan kutsua ei ajeta koskaan yhtä aikaa: jokainen polku tarkistaa <see cref="IsThinking"/>-lipun.
/// </summary>
public sealed partial class AiAssistantViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly ICleanerLogger _log;
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<IAiBackend> _backendFactory;
    private AiAssistant? _assistant;
    private CancellationTokenSource? _cts;
    private bool _autoMode;

    public ObservableCollection<AiMessageViewModel> Messages { get; } = new();

    /// <summary>Onko API-avain asetettu (ympäristömuuttuja ANTHROPIC_API_KEY). Luetaan kerran käynnistyksessä.</summary>
    public bool IsAvailable { get; }

    public string UnavailableText => Loc.T(
        "The AI assistant needs an API key in the environment variable ANTHROPIC_API_KEY. Set it and restart PC Cleaner.",
        "Tekoälyavustaja tarvitsee API-avaimen ympäristömuuttujaan ANTHROPIC_API_KEY. Aseta se ja käynnistä PC Cleaner uudelleen.");

    /// <summary>Mallin tunnus settings.json-kentästä aiModel.</summary>
    public string ModelName { get; }

    /// <summary>Asetussivun vihje avaimesta.</summary>
    public string KeyHint => IsAvailable
        ? Loc.T("API key: ANTHROPIC_API_KEY environment variable", "API-avain: ympäristömuuttuja ANTHROPIC_API_KEY")
        : UnavailableText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReviewCommand), nameof(HealthCheckCommand), nameof(AskCommand), nameof(CancelCommand))]
    public partial bool IsThinking { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskCommand))]
    public partial string Question { get; set; }

    /// <summary>
    /// Auto-tila: arvio valitsee kohteet itse ja Automaattinen siivous on käytössä. Tallennetaan settings.json-kenttään
    /// aiAutoMode; epäonnistunut tallennus kirjataan varoituksena eikä estä vaihtoa.
    /// </summary>
    public bool AutoMode
    {
        get => _autoMode;
        set
        {
            if (value == _autoMode) return;
            _autoMode = value;
            try
            {
                AppSettings.Update(s => s.AiAutoMode = value);
            }
            catch (Exception ex)
            {
                _log.Warning(Loc.T($"Saving the auto mode setting failed: {ex.Message}", $"Auto-tilan tallennus epäonnistui: {ex.Message}"));
            }
            OnPropertyChanged();
            _main.AutoCleanCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Valitulla moduulilla on arvio ja vähintään yksi valittu kohde on Caution tai Keep.</summary>
    public bool HasFlagged => _main.SelectedModule?.HasFlaggedSelection() ?? false;

    public AiAssistantViewModel(MainViewModel main, ICleanerLogger log, DispatcherQueue dispatcher, Func<IAiBackend> backendFactory,
        bool isAvailable, AppSettings settings)
    {
        _main = main;
        _log = log;
        _dispatcher = dispatcher;
        _backendFactory = backendFactory;
        IsAvailable = isAvailable;
        ModelName = settings.AiModel;
        _autoMode = settings.AiAutoMode;
        Question = "";
    }

    /// <summary>Luodaan ensimmäisellä kutsulla, jotta käynnistyksessä ei luoda asiakasta eikä tehdä API-kutsua.</summary>
    private AiAssistant Assistant => _assistant ??= new AiAssistant(_backendFactory(), new AiAssistantOptions { Model = ModelName });

    // ---------- Tilan ilmoitukset MainViewModelista ----------

    /// <summary>IsBusy muuttui: komentojen tila.</summary>
    internal void OnMainBusyChanged()
    {
        ReviewCommand.NotifyCanExecuteChanged();
        HealthCheckCommand.NotifyCanExecuteChanged();
        AskCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Valittu moduuli tai sen skannaus, valinta tai arvio muuttui.</summary>
    internal void OnModuleStateChanged()
    {
        ReviewCommand.NotifyCanExecuteChanged();
        RefreshFlagged();
    }

    private void RefreshFlagged()
    {
        OnPropertyChanged(nameof(HasFlagged));
        DeselectFlaggedCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsThinkingChanged(bool value) => _main.AutoCleanCommand.NotifyCanExecuteChanged();

    // ---------- Arvio (REQ-13) ----------

    private bool CanReview() =>
        IsAvailable && !_main.IsBusy && !IsThinking && _main.SelectedModule is { HasScanned: true, HasResults: true };

    [RelayCommand(CanExecute = nameof(CanReview))]
    private async Task ReviewAsync()
    {
        if (_main.SelectedModule is not { } module) return;
        await ReviewModuleAsync(module, CancellationToken.None);
    }

    /// <summary>
    /// Pyytää arvion moduulin kaikista kohteista ja näyttää sen paneelissa. Yhteinen Arvioi-komennolle ja
    /// automaattiselle siivoukselle; <paramref name="ct"/> linkitetään omaan peruutukseen, joten sekä alapalkin että
    /// paneelin Peruuta keskeyttävät. Palauttaa null virheessä tai peruutuksessa (syy näkyy paneelissa).
    /// </summary>
    public async Task<AiReview?> ReviewModuleAsync(ModuleViewModel module, CancellationToken ct)
    {
        if (IsThinking || !IsAvailable) return null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _cts = cts;
        Messages.Add(new AiMessageViewModel(AiMessageRole.User,
            Loc.T($"Review results: {module.DisplayName}", $"Tarkista tulokset: {module.DisplayName}")));
        IsThinking = true;
        try
        {
            var generation = module.ScanGeneration;
            // ItemViewModel-arvot luetaan UI-säikeessä; digest rakennetaan taustalla (14 000 kohteen lajittelu).
            var entries = module.Categories.Concat(module.HighRiskCategories)
                .SelectMany(c => c.Items)
                .Select(i => (i.Item, i.IsSelected))
                .ToList();
            var name = module.DisplayName;
            var sumSizes = module.SelectedByDefault;
            var digest = await Task.Run(
                () => ScanDigest.Build(name, entries, sumSizes, new DigestOptions(), PathSanitizer.ForCurrentUser()), cts.Token);

            var review = await Assistant.ReviewAsync(digest, Loc.Current, cts.Token);
            _log.Debug($"AI review: {review.Usage.InputTokens} in / {review.Usage.OutputTokens} out tokens");

            var text = new StringBuilder(ReviewText(review, digest));
            // EDGE-11: tulokset vaihtuivat kesken (uusi skannaus) → arvio näytetään vain tekstinä.
            if (module.ScanGeneration == generation)
            {
                module.ApplyReview(review);
                // Opt-in-moduulit (käynnistys, levytila, rekisteri, ohjelman poisto, prosessit) jäävät aina Auto-tilan
                // ulkopuolelle: niissä arvio vain neuvoo, ja kohteet valitaan yksitellen.
                if (AutoMode && module.SelectedByDefault)
                {
                    var (selected, deselected) = module.ApplyAutoSelection();
                    text.Append("\n\n").Append(Loc.T($"Selected {selected}, deselected {deselected}",
                        $"Valittu {selected}, poistettu valinnasta {deselected}"));
                    _main.NotifySelectionChanged();
                }
            }
            Messages.Add(new AiMessageViewModel(AiMessageRole.Assistant, text.ToString()));
            return review;
        }
        catch (AiException ex)
        {
            ShowError(ex);
            return null;
        }
        catch (OperationCanceledException)
        {
            Messages.Add(new AiMessageViewModel(AiMessageRole.Info, Loc.T("(canceled)", "(peruutettu)")));
            return null;
        }
        finally
        {
            _cts = null;
            IsThinking = false;
            RefreshFlagged();
        }
    }

    /// <summary>Yhteenveto ja sen alla jokaisesta Caution- ja Keep-arviosta rivi "arvio: polku tai kategoria – perustelu".</summary>
    private static string ReviewText(AiReview review, ScanDigest digest)
    {
        var sb = new StringBuilder(review.Summary.Trim());
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in digest.KeyToItem.Values) paths.TryAdd(item.Id, item.Path);

        var lines = new List<string>();
        foreach (var c in review.Categories.Where(c => c.Verdict != AiVerdict.Safe))
            lines.Add($"{ItemViewModel.VerdictTextFor(c.Verdict)}: {c.Category} – {c.Reason}");
        foreach (var i in review.Items.Where(i => i.Verdict != AiVerdict.Safe))
            lines.Add($"{ItemViewModel.VerdictTextFor(i.Verdict)}: {(paths.TryGetValue(i.ItemId, out var p) ? p : i.ItemId)} – {i.Reason}");
        if (lines.Count > 0) sb.Append("\n\n").Append(string.Join("\n", lines));
        return sb.ToString();
    }

    // ---------- Koneen kunto (REQ-14) ja kysymys (REQ-15) ----------

    private bool CanHealthCheck() => IsAvailable && !_main.IsBusy && !IsThinking;

    [RelayCommand(CanExecute = nameof(CanHealthCheck))]
    private Task HealthCheckAsync() =>
        StreamAsync(Loc.T("PC health check", "Koneen kunnon tarkistus"), async (assistant, onDelta, token) =>
        {
            var facts = await Task.Run(SystemFactsCollector.Collect, token);
            var text = HealthDigest.Build(facts, ModuleSummaries());
            return await assistant.HealthCheckAsync(text, Loc.Current, onDelta, token);
        });

    private bool CanAsk() => IsAvailable && !_main.IsBusy && !IsThinking && !string.IsNullOrWhiteSpace(Question);

    [RelayCommand(CanExecute = nameof(CanAsk))]
    private Task AskAsync()
    {
        var question = Question.Trim();
        if (question.Length == 0) return Task.CompletedTask;
        Question = "";
        return StreamAsync(question, (assistant, onDelta, token) => assistant.AskAsync(question, Loc.Current, onDelta, token));
    }

    /// <summary>Moduulien yhteenvedot nykyisistä skannaustuloksista. Opt-in-moduulien kohteet voivat olla sisäkkäisiä: ei summia.</summary>
    private List<ModuleScanSummary> ModuleSummaries() => _main.Modules.Select(m =>
    {
        var categories = m.Categories.Concat(m.HighRiskCategories).ToList();
        return new ModuleScanSummary(m.Id, m.DisplayName, m.HasScanned,
            categories.Sum(c => c.Items.Count), categories.Sum(c => c.TotalBytes), m.SelectedByDefault,
            categories.Select(c => (c.Name, c.Items.Count, c.TotalBytes)).ToList());
    }).ToList();

    /// <summary>
    /// Suoratoistettu vastaus: avustajan viesti luodaan tyhjänä ja kasvaa deltojen mukana UI-säikeessä.
    /// Peruutus ja virhe jättävät jo saadun tekstin.
    /// </summary>
    private async Task StreamAsync(string userText,
        Func<AiAssistant, Action<string>, CancellationToken, Task<AiTextResult>> call)
    {
        if (IsThinking || !IsAvailable) return;

        using var cts = new CancellationTokenSource();
        _cts = cts;
        Messages.Add(new AiMessageViewModel(AiMessageRole.User, userText));
        var answer = new AiMessageViewModel(AiMessageRole.Assistant, "");
        Messages.Add(answer);
        IsThinking = true;

        // Delta voi tulla taustasäikeestä tai UI-säikeestä (SDK:n silmukka voi jatkaa kummassa tahansa), joten jonoon
        // viedyt päivitykset voivat ajautua vasta kutsun päätyttyä. Siksi saatu teksti kerätään lukon alla, jokainen
        // päivitys asettaa koko tekstin, ja lopuksi viesti asetetaan kerran lopulliseen muotoonsa (finished estää
        // myöhästyneet päivitykset). Näin "(vastaus katkesi)" ja "(peruutettu)" ovat aina tekstin lopussa.
        var received = new StringBuilder();
        var gate = new object();
        var finished = false;
        string Received() { lock (gate) return received.ToString(); }
        void Finish()
        {
            lock (gate) finished = true;
            answer.Text = Received();
        }
        void OnDelta(string delta)
        {
            lock (gate)
            {
                if (finished) return;
                received.Append(delta);
            }
            if (_dispatcher.HasThreadAccess) answer.Text = Received();
            else _dispatcher.TryEnqueue(() =>
            {
                // Ajetaan UI-säikeessä kuten Finish, joten tarkistus ja asetus eivät lomitu sen kanssa.
                lock (gate) { if (finished) return; }
                answer.Text = Received();
            });
        }

        try
        {
            var result = await call(Assistant, OnDelta, cts.Token);
            Finish();
            _log.Debug($"AI answer: {result.Usage.InputTokens} in / {result.Usage.OutputTokens} out tokens");
            if (result.Truncated) answer.Append(Loc.T("\n(answer was cut short)", "\n(vastaus katkesi)"));
        }
        catch (AiException ex)
        {
            Finish();
            if (answer.Text.Length == 0) Messages.Remove(answer);
            ShowError(ex);
        }
        catch (OperationCanceledException)
        {
            Finish();
            if (answer.Text.Length == 0)
            {
                Messages.Remove(answer);
                Messages.Add(new AiMessageViewModel(AiMessageRole.Info, Loc.T("(canceled)", "(peruutettu)")));
            }
            else
            {
                answer.Append(Loc.T("\n(canceled)", "\n(peruutettu)"));
            }
        }
        finally
        {
            _cts = null;
            IsThinking = false;
        }
    }

    // ---------- Virheet (REQ-16) ----------

    private void ShowError(AiException ex)
    {
        Messages.Add(new AiMessageViewModel(AiMessageRole.Error, ErrorText(ex)));
        _log.Warning(Loc.T($"AI assistant: {ex.Kind}: {ex.Message}", $"Tekoälyavustaja: {ex.Kind}: {ex.Message}"));
    }

    private string ErrorText(AiException ex) => ex.Kind switch
    {
        AiErrorKind.MissingKey => UnavailableText,
        AiErrorKind.Unauthorized => Loc.T("The API key was rejected (401).", "API-avain hylättiin (401)."),
        AiErrorKind.RateLimited => Loc.T("Rate limit reached, try again in a moment.", "Pyyntöraja täynnä, yritä hetken kuluttua."),
        AiErrorKind.Network => Loc.T($"Network error: {ex.Message}", $"Verkkovirhe: {ex.Message}"),
        AiErrorKind.Server => Loc.T($"Server error: {ex.Message}", $"Palvelinvirhe: {ex.Message}"),
        AiErrorKind.BadRequest => Loc.T($"Request rejected: {ex.Message}", $"Pyyntö hylättiin: {ex.Message}"),
        AiErrorKind.Refused => Loc.T($"The assistant declined (category {ex.Category ?? "unknown"}).",
            $"Avustaja kieltäytyi (luokka {ex.Category ?? "unknown"})."),
        AiErrorKind.InvalidResponse => Loc.T("The answer could not be interpreted.", "Vastausta ei voitu tulkita."),
        _ => ex.Message
    };

    // ---------- Valinnat ja keskustelu (REQ-18) ----------

    private bool CanDeselectFlagged() => HasFlagged;

    [RelayCommand(CanExecute = nameof(CanDeselectFlagged))]
    private void DeselectFlagged()
    {
        if (_main.SelectedModule is not { } module) return;
        var count = module.DeselectFlagged();
        Messages.Add(new AiMessageViewModel(AiMessageRole.Info,
            Loc.T($"Deselected {count} items", $"Poistettu valinnasta {count} kohdetta")));
        _main.NotifySelectionChanged();
        RefreshFlagged();
    }

    /// <summary>Uusi keskustelu: historia ja viestit tyhjenevät, moduulien merkit jäävät.</summary>
    [RelayCommand]
    private void Reset()
    {
        _assistant?.Reset();
        Messages.Clear();
    }

    private bool CanCancel() => IsThinking;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Kutsu päättyi juuri ja CTS on jo vapautettu.
        }
    }

    // ---------- Kieli (REQ-21) ----------

    /// <summary>Kiinteät tekstit nykyisellä kielellä. Jo kirjoitetut viestit jäävät syntykieleensä.</summary>
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(UnavailableText));
        OnPropertyChanged(nameof(KeyHint));
        RefreshFlagged();
        ReviewCommand.NotifyCanExecuteChanged();
        HealthCheckCommand.NotifyCanExecuteChanged();
        AskCommand.NotifyCanExecuteChanged();
    }
}

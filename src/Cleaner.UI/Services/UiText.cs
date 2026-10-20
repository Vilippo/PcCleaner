using Cleaner.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cleaner.UI.Services;

/// <summary>
/// MainWindow.xaml-tiedoston kiinteät tekstit nykyisellä kielellä. XAML sitoo nämä muodossa
/// <c>{x:Bind ViewModel.T.X, Mode=OneWay}</c>, ja <see cref="Refresh"/> päivittää kaikki sidonnat kielen vaihtuessa.
/// </summary>
public sealed partial class UiText : ObservableObject
{
    // Tavallisen käyttäjän ilmoitus
    public string StandardUserTitle => Loc.T("Standard user", "Tavallinen käyttäjä");
    public string StandardUserMessage => Loc.T(
        "System folders (Windows\\Temp, SoftwareDistribution) and HKLM startup entries are skipped without administrator rights.",
        "Järjestelmäkansiot (Windows\\Temp, SoftwareDistribution) ja HKLM-käynnistyskohteet ohitetaan ilman järjestelmänvalvojan oikeuksia.");
    public string RestartElevated => Loc.T("Restart as administrator", "Käynnistä uudelleen järjestelmänvalvojana");

    // Navigointipaneeli
    public string ScanAll => Loc.T("Scan all", "Skannaa kaikki");
    public string ScanAllToolTip => Loc.T("Scan all modules included in the default cleanup", "Skannaa kaikki moduulit, jotka kuuluvat oletussiivoukseen");

    // Levytilan analyysi
    public string FolderHeader => Loc.T("Folder", "Kansio");
    public string FolderPlaceholder => Loc.T("Empty = all fixed drives", "Tyhjä = kaikki kiinteät asemat");
    public string Browse => Loc.T("Browse...", "Selaa...");
    public string Up => Loc.T("Up", "Ylös");

    // Ohjelman poisto
    public string ProgramSearchPlaceholder => Loc.T("Search programs by name or publisher", "Hae ohjelmaa nimellä tai julkaisijalla");
    public string SortToolTip => Loc.T("List order", "Listan järjestys");
    public string SortByName => Loc.T("Name", "Nimi");
    public string SortByInstallDate => Loc.T("Install date, newest first", "Asennuspäivä, uusin ensin");
    public string RefreshList => Loc.T("Refresh list", "Päivitä lista");
    public string UninstallProgram => Loc.T("Uninstall program", "Poista ohjelma");
    public string FindLeftovers => Loc.T("Find leftovers", "Etsi jäänteet");
    public string UninstallHelp => Loc.T(
        "Uninstall program runs the program's own uninstaller and then searches for leftovers. Leftovers are moved to quarantine, and Restore backup... brings them back.",
        "Poista ohjelma ajaa ohjelman oman poisto-ohjelman ja etsii sen jälkeen jäänteet. Jäänteet siirretään karanteeniin, josta Palauta varmuuskopio... tuo ne takaisin.");

    // Tulokset
    public string NotesTitle => Loc.T("Warnings", "Huomautukset");
    public string HighRiskHeader => Loc.T("High-risk items", "Korkean riskin kohteet");
    public string HighRiskHint => Loc.T(
        "Never selected by default. Check each item before selecting it.",
        "Ei koskaan valittuna oletuksena. Tarkista jokainen kohde ennen valintaa.");
    public string NothingFound => Loc.T("Nothing found.", "Ei löydöksiä.");

    // Alapalkki
    public string RestoreBackup => Loc.T("Restore backup...", "Palauta varmuuskopio...");
    public string RestoreBackupToolTip => Loc.T(
        "Bring back registry backups and quarantined items from an earlier run",
        "Tuo aiemman ajon rekisterivarmuuskopiot ja karanteeniin siirretyt kohteet takaisin");
    public string Cancel => Loc.T("Cancel", "Peruuta");
    public string Scan => Loc.T("Scan", "Skannaa");
    public string Clean => Loc.T("Clean", "Puhdista");
    public string Log => Loc.T("Log", "Loki");
    public string ClearLog => Loc.T("Clear", "Tyhjennä");
    public string Close => Loc.T("Close", "Sulje");

    // Asetussivu
    public string SettingsTitle => Loc.T("Settings", "Asetukset");
    public string AppearanceHeader => Loc.T("Appearance", "Ulkoasu");
    public string PaletteHeader => Loc.T("Color palette", "Väripaletti");
    public string FontHeader => Loc.T("Font", "Fontti");
    public string LanguageHeader => Loc.T("Language", "Kieli");

    // Moduulisivu: tyhjä tila, laatat ja merkit
    public string EmptyStateTitle => Loc.T("Nothing scanned yet", "Ei vielä skannattu");
    public string EmptyStateBody => Loc.T(
        "Scan shows what can be removed. Nothing is deleted before you confirm.",
        "Skannaus näyttää, mitä voi poistaa. Mitään ei poisteta ennen vahvistustasi.");
    public string FoundLabel => Loc.T("Found", "Löytyi");
    public string SelectedLabel => Loc.T("Selected", "Valittu");
    public string AdminBadge => Loc.T("Administrator", "Järjestelmänvalvoja");
    public string OptInBadge => Loc.T("Manual selection", "Valitaan erikseen");

    // Tekoälyavustaja: paneeli ja asetussivun osio
    public string AiTitle => Loc.T("AI assistant", "Tekoälyavustaja");
    public string AiAutoMode => Loc.T("Auto mode", "Auto-tila");
    public string AiAutoModeHint => Loc.T(
        "The assistant selects items itself and Automatic cleanup runs scan, review and cleanup with one confirmation. High-risk items and opt-in modules are never included.",
        "Avustaja valitsee kohteet itse, ja Automaattinen siivous skannaa, arvioi ja puhdistaa yhdellä vahvistuksella. Korkean riskin kohteet ja erikseen valittavat moduulit eivät ole koskaan mukana.");
    public string AiReview => Loc.T("Review results", "Tarkista tulokset");
    public string AiHealth => Loc.T("PC health", "Koneen kunto");
    public string AiAutoClean => Loc.T("Automatic cleanup", "Automaattinen siivous");
    public string AiDeselectFlagged => Loc.T("Deselect flagged", "Poista varoitetut valinnasta");
    public string AiReset => Loc.T("New conversation", "Uusi keskustelu");
    public string AiAskPlaceholder => Loc.T("Ask the assistant", "Kysy avustajalta");
    public string AiSend => Loc.T("Send", "Lähetä");
    public string AiThinking => Loc.T("Thinking...", "Ajattelee...");
    public string AiSettingsHeader => Loc.T("AI assistant", "Tekoälyavustaja");
    public string AiModelLabel => Loc.T("Model", "Malli");
    public string AiPrivacyNote => Loc.T(
        "Paths are sent with the user name hidden. Large lists are summarized.",
        "Polut lähetetään käyttäjänimi piilotettuna. Suuret listat tiivistetään.");

    /// <summary>Nostaa PropertyChanged-tapahtuman tyhjällä nimellä: kaikki ominaisuudet luetaan uudelleen.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}

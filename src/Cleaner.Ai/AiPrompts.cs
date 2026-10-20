using Cleaner.Core.Localization;

namespace Cleaner.Ai;

/// <summary>
/// Kehotteet englanniksi ja suomeksi samasta rakenteesta. Kieli valitaan parametrilla eikä <c>Loc.Current</c>-arvosta,
/// jotta kutsuja päättää vastauksen kielen.
/// </summary>
public static class AiPrompts
{
    public static string System(Language language) => language == Language.Fi ? SystemFi : SystemEn;

    public static string ReviewInstruction(Language language) => language == Language.Fi ? ReviewFi : ReviewEn;

    public static string HealthInstruction(Language language) => language == Language.Fi ? HealthFi : HealthEn;

    private const string SystemEn =
        """
        You are the assistant inside PC Cleaner, a Windows cleanup program. Its modules are: file cleanup (temporary
        files, logs, caches), browser caches, Recycle Bin, startup programs, disk usage analysis, registry cleanup,
        program uninstall and running processes. Every cleanup starts with a dry run, files can go to quarantine,
        registry changes are backed up as .reg files, and every deletion is written to a log.

        You review scan results and the condition of the computer, and you answer the user's questions.
        Verdicts:
        - safe: can be deleted without losing any data.
        - caution: probably safe, but the user should check it first.
        - keep: must not be deleted, for example the user's documents, programs in use and anything you do not recognize.

        Never recommend deleting the user's documents or system files. Paths contain the markers %USERPROFILE% and
        %USERNAME% on purpose: the user name is hidden for privacy, so do not ask for it.
        Write plain text without markdown (no headings, bold, tables or code blocks). Keep answers short and concrete.
        Answer in English.
        """;

    private const string SystemFi =
        """
        Olet PC Cleaner -ohjelman avustaja. PC Cleaner on Windowsin siivousohjelma, jonka moduulit ovat:
        tiedostosiivous (väliaikaistiedostot, lokit, välimuistit), selainten välimuistit, roskakori,
        käynnistysohjelmat, levytilan analyysi, rekisterin siivous, ohjelman poisto ja käynnissä olevat prosessit.
        Jokainen siivous alkaa kuivalla ajolla, tiedostot voivat mennä karanteeniin, rekisterimuutoksista otetaan
        .reg-varmuuskopiot ja jokainen poisto kirjataan lokiin.

        Arvioit skannaustuloksia ja koneen kuntoa ja vastaat käyttäjän kysymyksiin.
        Arviot:
        - safe: voi poistaa ilman tietojen menetystä.
        - caution: luultavasti turvallinen, mutta käyttäjän kannattaa tarkistaa ensin.
        - keep: ei saa poistaa, esimerkiksi käyttäjän dokumentit, käytössä olevat ohjelmat ja kaikki, mitä et tunnista.

        Älä koskaan suosittele käyttäjän dokumenttien tai järjestelmätiedostojen poistoa. Poluissa on merkinnät
        %USERPROFILE% ja %USERNAME% tarkoituksella: käyttäjänimi on piilotettu tietosuojan vuoksi, joten älä kysy sitä.
        Kirjoita tavallista tekstiä ilman markdownia (ei otsikoita, lihavointia, taulukoita eikä koodilohkoja).
        Vastaa lyhyesti ja konkreettisesti. Vastaa suomeksi.
        """;

    private const string ReviewEn =
        """
        Review the scan results below. Give a verdict for every listed category, and item verdicts only for items
        that need something specific said (not for every item). A category verdict applies to all of its items,
        also the ones that are not listed. Write the summary in 2 to 4 sentences and each reason in one sentence.
        Use the item keys (i1, i2, ...) and category names exactly as they appear. Answer with JSON that follows the schema.
        """;

    private const string ReviewFi =
        """
        Arvioi alla olevat skannaustulokset. Anna arvio jokaisesta listatusta kategoriasta ja kohdekohtainen arvio
        vain kohteista, joista on jotain erityistä sanottavaa (ei jokaisesta kohteesta). Kategorian arvio koskee
        kaikkia sen kohteita, myös listaamattomia. Kirjoita yhteenveto (summary) 2–4 lauseella ja jokainen perustelu
        yhdellä lauseella. Käytä kohteiden avaimia (i1, i2, ...) ja kategorioiden nimiä täsmälleen sellaisina kuin ne
        ovat. Vastaa JSON-muodossa skeeman mukaan.
        """;

    private const string HealthEn =
        """
        Assess the condition of this computer from the facts below. Then give 3 to 6 concrete actions in order of
        importance, and name the cleanup modules that are worth scanning or cleaning. A module marked "not scanned"
        has no results yet.
        """;

    private const string HealthFi =
        """
        Arvioi tämän koneen kunto alla olevien tietojen perusteella. Anna sen jälkeen 3–6 konkreettista
        toimenpidettä tärkeysjärjestyksessä ja nimeä siivousmoduulit, jotka kannattaa skannata tai puhdistaa.
        Moduulilla, jonka kohdalla lukee "not scanned", ei ole vielä tuloksia.
        """;
}

# Plan: Englannin kieli, EN/FI-vaihto, README englanniksi ja MIT-lisenssi
Branch: feature/english-localization
Created: 2026-10-20

## Background & scope

PcCleaner julkaistaan julkisena GitHub-repona. Kaikki käyttäjälle näkyvä teksti on nyt kovakoodattua suomea
(noin 280 merkkijonoa 40 tiedostossa: Core, 7 moduulia, JSON-säännöt, CLI ja WinUI-sovellus). Tavoite:

1. Ohjelma toimii englanniksi ja suomeksi. Oletuskieli on aina englanti. Graafisessa sovelluksessa on EN/FI-valitsin,
   joka vaihtaa kielen heti ilman uudelleenkäynnistystä ja muistaa valinnan. CLI saa lipun `--lang en|fi`.
2. README.md on englanniksi (GitHubin etusivu), suomenkielinen versio siirtyy tiedostoon README.fi.md.
3. Vastuuvapaus toteutetaan MIT-lisenssillä (LICENSE, "AS IS, without warranty").

Käyttäjän päätökset (kysytty 20.10.2026):

- Oletuskieli on aina englanti, Windowsin kieli ei vaikuta. Valinta tallennetaan.
- Englanniksi käännetään kaikki näkyvä teksti: UI, CLI, moduulien kohdenimet ja kuvaukset, JSON-sääntöjen nimet ja
  kuvaukset, virheilmoitukset ja lokirivit.
- Vastuuvapaus vain MIT-lisenssinä, ei ensikäynnistyksen dialogia eikä erillistä Tietoja-näkymää.
- README.md englanniksi + README.fi.md suomeksi, kummankin alussa linkki toiseen.
- Lisenssin tekijänoikeusrivi: `Copyright (c) 2026 Vilippo`.

Ulkopuolella: repon muuttaminen julkiseksi GitHubissa (käyttäjä tekee itse), kolmas kieli, Windowsin omien
poikkeusviestien (`ex.Message`) kääntäminen, karanteenin ja rekisterivarmuuskopioiden manifest.txt-tiedostojen sisältö
(pysyvä tiedostomuoto, ks. CON-2), ensikäynnistyksen vastuuvapausdialogi.

## Glossary

Kaikki tehtävät käyttävät näitä englanninkielisiä termejä samoin (amerikanenglanti). Tehtävätekstit toistavat taulukon.

| Suomi | English |
|---|---|
| Tiedostosiivous / Väliaikaistiedostot, välimuistit, lokit. | File cleanup / Temporary files, caches, logs. |
| Selainten välimuistit | Browser caches |
| Roskakori | Recycle Bin |
| Käynnistysohjelmat | Startup programs |
| Levytilan analyysi | Disk usage |
| Rekisterin puhdistus | Registry cleanup |
| Ohjelman poisto | Uninstall programs |
| Prosessit | Processes |
| kuiva ajo, lokietuliite `[KUIVA] ` | dry run, log prefix `[DRY RUN] ` |
| karanteeni | quarantine |
| jäänteet | leftovers |
| varmuuskopio | backup |
| poisto-ohjelma | uninstaller |
| järjestelmänvalvoja | administrator |
| Skannaa / Puhdista / Peruuta / Sulje / Valmis | Scan / Clean / Cancel / Close / Done |
| Kohde ei kuulu tälle moduulille | Item does not belong to this module |
| Virheellinen tunniste | Invalid item id |
| Ei oikeuksia | Access denied |
| Kohdetta ei enää ole | Item no longer exists |
| Tarvitsee järjestelmänvalvojan oikeudet | Requires administrator rights |
| Varmuuskopio epäonnistui: {x} | Backup failed: {x} |
| ESTETTY | BLOCKED |
| Suojattu | Protected |
| Peruutettu | Canceled |
| Korkea riski / Keskiriski / Matala riski | High risk / Medium risk / Low risk |
| {n} kpl | {n} items |
| tavuyksiköt t, kt, Mt, Gt, Tt | B, KB, MB, GB, TB |

## Requirements

Ydin (Core)

- REQ-1: `Cleaner.Core.Localization.Loc` tarjoaa kielen `Loc.Current` (enum `Language { En, Fi }`, oletus `En`),
  valinnan `Loc.T(en, fi)`, joka palauttaa nykyisen kielen version, tapahtuman `Loc.Changed`, joka laukeaa kerran
  jokaisesta todellisesta kielen muutoksesta (ei, jos sama arvo asetetaan uudelleen), sekä `Loc.Culture`
  (`en-US` tai `fi-FI`, `CultureInfo.GetCultureInfo`). `Loc.ToCode` palauttaa `"en"`/`"fi"` ja `Loc.TryParse` hyväksyy
  `en`/`fi` kirjainkoosta ja ympäröivistä välilyönneistä riippumatta, muut arvot palauttavat `false`.
- REQ-2: `Cleaner.Core.Localization.LocalizedText(string En, string Fi)` on record struct, jonka `ToString()` palauttaa
  `Loc.T(En, Fi)`, ja jolla on implisiittinen muunnos `string → LocalizedText` (sama teksti molemmille kielille).
- REQ-3: `ByteFormatter.Format` käyttää englanniksi yksiköitä `B, KB, MB, GB, TB` ja `en-US`-desimaalipistettä
  (esim. `1.46 MB`, `0 B`, `512 B`), suomeksi nykyisiä yksiköitä `t, kt, Mt, Gt, Tt` ja `fi-FI`-desimaalipilkkua
  (esim. `1,46 Mt`), käyttöjärjestelmän kulttuurista riippumatta.
- REQ-4: `Cleaner.Core.Services.AppSettings` lukee ja kirjoittaa tiedoston `%LOCALAPPDATA%\PcCleaner\settings.json`
  muodossa `{ "language": "en" }` tai `{ "language": "fi" }`. `Load` ei heitä koskaan poikkeusta. `Save` luo kansion
  tarvittaessa.
- REQ-5: `CleanRule` saa valinnaiset kentät `NameFi` ja `DescriptionFi` (JSON `nameFi`, `descriptionFi`) sekä
  lasketut ominaisuudet `DisplayName => Loc.T(Name, NameFi ?? Name)` ja
  `DisplayDescription => Loc.T(Description, DescriptionFi ?? Description)`. `Name` ja `Description` ovat englanniksi.
- REQ-6: Coren palveluiden käyttäjälle näkyvät tekstit ovat molemmilla kielillä: `CleanerEngine` (lokit, virheet),
  `RuleLoader` (poikkeukset), `BackupSessions` (yhteenvedot, viestit), `PathGuard` (ohitussyyt), `Quarantine`
  (virheet, lokit, palautusviestit), `RegistryBackup` (virheet, lokit, yhteenveto `{n} kohdetta`), `RegistryGuard`
  (syyt), `SafeFileDeleter` (lokit, syyt). Kieli valitaan `Loc.Current`-arvon mukaan sillä hetkellä, kun teksti syntyy.
- REQ-7: Tiedostomoduulit: `FileCleanerModule` ottaa näyttönimen ja kuvauksen `LocalizedText`-tyyppisinä (myös
  `FromRuleFile`), käyttää kohteen kategoriana `rule.DisplayName` ja kuvauksena `rule.DisplayDescription`, ja kaikki
  sen omat tekstit (virheet, lokit, edistyminen, ohitussyyt) ovat molemmilla kielillä. `RecycleBinModule` samoin
  (nimi, kuvaus, kategoria `Recycle Bin`/`Roskakori`, polkuteksti, virheet, lokit).
- REQ-8: `rules/files.json` ja `rules/browsers.json`: jokaisen säännön `name` ja `description` ovat englanniksi ja
  nykyinen suomenkielinen teksti on sellaisenaan kentissä `nameFi` ja `descriptionFi`. Otsakekommentit englanniksi.
- REQ-9: Käynnistysohjelmat-moduuli (`StartupModule`, `StartupSource`, `StoreStartupTaskSource`, `TaskSchedulerSource`):
  nimi, kuvaus, kategoriat, kohdekuvaukset, virheet, edistyminen, lokit ja ohitussyyt molemmilla kielillä.
- REQ-10: Levytilan analyysi (`DiskUsageModule`): nimi, kuvaus, kategoriat, kuvaukset, virheet, edistyminen, lokit ja
  ohitussyyt molemmilla kielillä.
- REQ-11: Rekisterin puhdistus (`RegistryModule`): nimi, kuvaus, kategoriat, kuvaukset, virheet, poikkeukset,
  edistyminen, lokit (myös `Describe()`) ja ohitussyyt molemmilla kielillä.
- REQ-12: Ohjelman poisto (`UninstallModule`, `LeftoverScanner`, `LeftoverGuard`, `UninstallRunner`): nimi, kuvaus,
  kategoriat, kuvaukset (myös `MatchReason`-osat ja liite "ohjelma on yhä asennettu"), polkuteksti `(oletus)`,
  virheet, poikkeukset, edistyminen, lokit ja ohitussyyt molemmilla kielillä.
- REQ-13: Prosessit (`ProcessesModule`, `ProcessDescription`, `ProcessGuard`, `ProcessTerminator`, `ProcessInventory`,
  `ProcessRuleLoader`): nimi, kuvaus, kategoriat, kuvausten osat (myös `FormatAge`-yksiköt), virheet, poikkeukset,
  edistyminen, lokit ja ohitussyyt molemmilla kielillä. `ProcessRule` saa kentän `DescriptionFi` (JSON
  `descriptionFi`) ja laskettavan `DisplayDescription => Loc.T(Description, DescriptionFi ?? Description)`.
  `rules/processes/known.json`: `description` englanniksi, nykyinen teksti kentässä `descriptionFi`.
- REQ-14: Käyttäjälle näkyvien tekstien kulttuuririippuvainen numeromuotoilu (`P0`, desimaalit, sekunnit) käyttää
  `Loc.Culture`-arvoa, ei `CultureInfo.CurrentCulture`-arvoa.

CLI

- REQ-15: `pccleaner` hyväksyy lipun `--lang en|fi` missä kohtaa tahansa argumentteja, myös ennen komentoa
  (`pccleaner --lang fi scan`). Kieli valitaan järjestyksessä `--lang` > `settings.json` > `en`. CLI ei kirjoita
  `settings.json`-tiedostoa.
- REQ-16: Kaikki CLI:n tulosteet ovat molemmilla kielillä, myös ohjeteksti (`--help`), taulukko-otsikot, banneri,
  `modules`-listan `admin=yes/no`-tyyppiset sanat ja virheilmoitukset. Ohjeteksti mainitsee lipun `--lang`.
- REQ-17: Vahvistuskehote on englanniksi `[y/N]` ja suomeksi `[k/E]`. Molemmilla kielillä hyväksytään `y`, `yes`,
  `k` ja `kyllä` (kirjainkoko ei merkitse), kaikki muu peruuttaa. `--yes` ohittaa kehotteen kuten ennen.

Graafinen sovellus

- REQ-18: Sovellus lukee kielen `AppSettings.Load()`-kutsulla ennen pääikkunan luontia ja asettaa `Loc.Current`.
- REQ-19: NavigationView-paneelin alaosassa (PaneFooter, "Skannaa kaikki" -napin yhteydessä) on kielivalitsin
  (ComboBox), jossa on kaksi kohtaa `English` ja `Suomi` (aina omalla kielellään, ei käännetä) ja työkaluvihje
  `Language / Kieli`. Valitsin näyttää nykyisen kielen. Valinta asettaa `Loc.Current` ja tallentaa asetuksen.
- REQ-20: Kielen vaihtuessa nämä päivittyvät heti ilman uudelleenkäynnistystä: kaikki XAML-tekstit (Content, Header,
  PlaceholderText, ToolTip, Title, Message, InfoBar-tekstit), ikkunan otsikko, moduulien nimet ja kuvaukset
  navigaatiossa ja otsakkeessa, riskitekstit, admin- ja opt-in-vihjeet, moduulien `StatusText` ja `SelectionText`,
  kategorioiden riskiteksti, yhteenveto ja piilotettujen kohteiden teksti, ohjelmalistan järjestysvalikon kohdat,
  ohjelmalistan päivämäärät ja koot sekä treemapin tekstit. Vaihdon jälkeen avattavat dialogit ovat uudella kielellä.
- REQ-21: Kaikki UI:n omat tekstit ovat molemmilla kielillä: dialogien otsikot, viestit ja napit
  (`IDialogService`-oletusarvot mukaan lukien), kiireilmaisimen ja edistymisen tekstit, puhdistuksen kolmivaiheiset
  yhteenvedot, treemapin tekstit, `UiLogger`-tasotunnisteet (EN `ERROR`/`WARNING`, FI nykyiset `VIRHE`/`VAROITUS`)
  ja `UiModuleFactory`-nimet. Asennuspäivä näytetään englanniksi muodossa `yyyy-MM-dd`, suomeksi `d.M.yyyy`.

Dokumentaatio ja lisenssi

- REQ-22: Juuressa on `LICENSE`: MIT-lisenssin vakioteksti englanniksi, rivi `Copyright (c) 2026 Vilippo`.
- REQ-23: `README.md` on englanniksi ja kattaa kaiken nykyisen README:n sisällön (rakenne, vaatimukset, käännös ja
  ajo, CLI-esimerkit, moduulien kuvaukset, turvallisuusperiaatteet, laajentaminen), lisäksi kielen vaihdon
  (UI-valitsin, `--lang`, `settings.json`) ja osion `License`, joka viittaa LICENSE-tiedostoon ja kertoo, että ohjelma
  toimitetaan sellaisenaan ilman takuuta ja käyttö on omalla vastuulla. Ensimmäisellä rivillä otsikon alla on linkki
  `README.fi.md`-tiedostoon. Teksti ei kuvaa ohjelmaa enää "omaan käyttöön" tehdyksi.
- REQ-24: `README.fi.md` on suomeksi, sisältää saman kuin README.md (nykyinen suomenkielinen teksti päivitettynä
  kielen vaihdolla ja lisenssiosiolla `Lisenssi`) ja alussa linkin `README.md`-tiedostoon.
- REQ-25: `docs/ARCHITECTURE.md`: sääntö "kaikki käyttäjälle näkyvä teksti on suomeksi" korvataan lokalisoinnin
  säännöillä (`Loc.T`, `LocalizedText`, JSON-kentät `nameFi`/`descriptionFi`, suomenkielinen teksti pysyy ennallaan,
  testit ajetaan oletuksena suomeksi, `settings.json`).

Testit

- REQ-26: Testiprojekti asettaa `Loc.Current = Language.Fi` ennen ensimmäistä testiä (moduulialustaja), joten
  olemassa olevat suomenkieliset tarkistukset pysyvät voimassa. Englantia testaavat testit kuuluvat xUnit-kokoelmaan
  `Language`, jonka määrittely on `DisableParallelization = true`, ja ne palauttavat kielen suomeksi lopuksi.
- REQ-27: Englanninkieliset testit kattavat `Loc`-, `LocalizedText`-, `ByteFormatter`- ja `AppSettings`-toiminnan sekä
  jokaisesta moduulista vähintään `DisplayName`-arvon ja yhden moduulin tuottaman tekstin (kategoria, kuvaus tai
  ohitussyy) englanniksi.

## Edge cases

- EDGE-1: `settings.json` puuttuu, on rikki tai sen kieli on tuntematon → kieli on englanti, ei poikkeusta, tiedostoa
  ei kirjoiteta ennen kuin käyttäjä vaihtaa kielen.
- EDGE-2: Käyttäjän oma sääntötiedosto ilman kenttiä `nameFi`/`descriptionFi` → `name`/`description` näytetään
  molemmilla kielillä.
- EDGE-3: Kielen vaihto skannauksen tai puhdistuksen aikana → ei mahdollista, valitsin on pois käytöstä, kun
  sovellus on kiireinen.
- EDGE-4: Jo tuotetut moduulitulokset (kategorioiden nimet, kohteiden kuvaukset, Huomautukset-virheet) ja jo
  kirjoitetut lokirivit säilyvät kielellä, jolla ne syntyivät. Seuraava skannaus tuottaa uuden kielen.
- EDGE-5: `--lang` ilman arvoa tai tuntemattomalla arvolla → virheilmoitus (kielellä `settings.json` > `en`) ja
  paluukoodi 2. Kaksi `--lang`-lippua → viimeinen voittaa.
- EDGE-6: `settings.json`-tallennus epäonnistuu UI:ssa → varoitus lokiin, kieli vaihtuu silti tälle istunnolle.

## Constraints

- CON-1: Kohdetunnisteet (`--item`-arvot kuten `startup:hkcu-run:...`, `processes:<pid>:<aika>`), CLI:n komentojen ja
  lippujen nimet, `--sort`-arvot (myös suomenkieliset aliakset `nimi`, `paiva`, `päivä`) ja varmuuskopioiden
  aikaleimat pysyvät ennallaan.
- CON-2: Pysyvät tiedostomuodot pysyvät ennallaan: karanteenin ja rekisterivarmuuskopioiden `manifest.txt`
  (myös suomenkielinen erotinrivi `Käsitellyt kohteet:`, jota `RegistryBackup` jäsentää), `.reg`-tiedostot,
  karanteenin kansiorakenne, lokitiedostojen nimet ja rivimuoto (vain viestin teksti vaihtuu).
- CON-3: Jokainen nykyinen suomenkielinen teksti säilyy merkki merkiltä samana suomenkielisenä versiona. Ei
  uudelleenmuotoilua. Näin olemassa olevat testit pysyvät voimassa, eikä niitä muokata, ellei tehtäväteksti sano.
- CON-4: Ei uusia NuGet-paketteja, ei `.resx`- eikä `.resw`-tiedostoja. Käännökset kirjoitetaan koodiin
  `Loc.T(en, fi)`-kutsuina tai `LocalizedText`-arvoina, englanti ensin.
- CON-5: `dotnet build PcCleaner.sln` antaa 0 varoitusta ja 0 virhettä, ja kaikki testit menevät läpi.
- CON-6: Englanti on amerikanenglantia ja noudattaa Glossary-taulukkoa.

## Assumptions

- Lokalisointi toteutetaan koodissa `Loc.T(en, fi)` -parina eikä resx-resursseilla. Kaksi kieltä vierekkäin on
  helpompi ylläpitää ilman Visual Studiota, kääntäjä pakottaa molemmat versiot, eikä unpackaged WinUI tarvitse
  satelliittikokoonpanoja tai PRI-resursseja.
- `Loc.Current` on prosessinlaajuinen staattinen arvo, ei `CultureInfo.CurrentUICulture`. Taustasäikeet
  (`Task.Run`) näkevät saman arvon, eikä kulttuurin virtaus säikeiden välillä vaikuta.
- `CultureInfo.CurrentCulture` jätetään ennalleen. Vain käyttäjälle näkyvä numeromuotoilu käyttää `Loc.Culture`.
  `CultureInfo.GetCultureInfo("fi-FI")` tuottaa tällä koneella saman muotoilun kuin nykyinen kulttuuri
  (tarkistettu 20.10.2026: `0.62.ToString("P0")` → `62 %` tavallisella välilyönnillä, `1.46` → `1,46`).
- CLI lukee `settings.json`-tiedoston oletuskieleksi, jotta UI:ssa tehty valinta koskee myös komentoriviä.
  `--lang` ohittaa sen.
- Vahvistuskehote hyväksyy molempien kielten myöntävät vastaukset kummallakin kielellä, jotta skriptit ja tottumus
  eivät riko mitään.
- Kielen vaihto heti toteutetaan sidonnoilla (`x:Bind ... Mode=OneWay`) `UiText`-olioon, joka nostaa
  `PropertyChanged`-tapahtuman tyhjällä ominaisuusnimellä. Uudelleenkäynnistys vaatisi UAC-kehotteen joka kerta,
  koska sovellus pyytää aina järjestelmänvalvojan oikeudet.
- `FileCleanerModule`-konstruktorin parametrit vaihtuvat tyyppiin `LocalizedText`. Implisiittinen muunnos stringistä
  pitää vanhat kutsut (testit, tehtaat ennen aaltoa 3) kääntyvinä.
- `CleanRule.Category` (ei käytössä koodissa) saa oletuksen `General`, ja browsers.json-tiedoston `"category":
  "Selaimet"` vaihtuu arvoon `"Browsers"`. Kenttää ei näytetä missään.
- Olemassa olevat suomenkieliset testit jäävät ennalleen. Uudet englanninkieliset testit menevät erilliseen
  kokoelmaan, jota xUnit ajaa rinnakkaisten kokoelmien jälkeen yksinään (`DisableParallelization`, xUnit 2.9.2),
  joten staattisen kielen vaihto ei vaikuta muihin testeihin.
- Ajonaikainen sääntötiedostojen puuttumisvaroitus ja muut lokiviestit noudattavat samaa `Loc.T`-käytäntöä.
- Rinnakkaiset aallot: tehtävät rakentavat vain oman projektinsa `--no-restore -p:BuildProjectReferences=false`
  -lipuilla, jotta samanaikaiset buildit eivät kirjoita samoihin `bin`/`obj`-kansioihin. T3 rakentaa Coren erilliseen
  tulostekansioon, koska muut aallon 2 tehtävät lukevat Coren tavallista tulostetta. Koko ratkaisun build ja testit
  ajaa foreman aallon lopussa.

## Disposition

Läpileikkaavat rajoitteet. Jokainen niitä koskeva tehtäväteksti toistaa ne, ja lopputarkastelu tarkistaa ne.

- CON-1: täyttyy, kun tunnisteiden muodostusta ei muuteta. Toistettu tehtävissä T3–T11.
- CON-3: suomenkielinen teksti ennallaan. Toistettu tehtävissä T3–T11, tarkistetaan AC-2:lla (vanhat testit) ja AC-4:llä.
- CON-4: ei pakettimuutoksia eikä resurssitiedostoja. Toistettu tehtävissä T1 ja T11, näkyy diffistä.
- CON-6: sama sanasto jokaisessa tehtävätekstissä, myös T2:n README-teksteissä. Tarkistetaan AC-4:llä.

## Wave 1

Kaksi riippumatonta tehtävää, ajetaan rinnakkain: T1 koskee vain Corea ja testiprojektia, T2 vain dokumentteja.

### T1: Lokalisoinnin ydin, asetukset ja testien kielialustus
- Status: pending
- Model: opus
- Requirements: REQ-1, REQ-2, REQ-3, REQ-4, REQ-5, REQ-26
- Constraints/edge: EDGE-1
- Files: src/Cleaner.Core/Localization/Loc.cs, src/Cleaner.Core/Localization/LocalizedText.cs, src/Cleaner.Core/Services/AppSettings.cs, src/Cleaner.Core/Services/ByteFormatter.cs, src/Cleaner.Core/Rules/CleanRule.cs, tests/Cleaner.Tests/TestLanguage.cs, tests/Cleaner.Tests/LocalizationTests.cs
- Verify: `dotnet build PcCleaner.sln` → 0 warnings, 0 errors; `dotnet test` → kaikki läpi (218 vanhaa + uudet)
- Task:
  PcCleaner (C# .NET 8, ratkaisu `PcCleaner.sln`) saa englannin kielen suomen rinnalle. Tämä tehtävä tekee
  lokalisoinnin perustan, jota kaikki muut projektit käyttävät. Muut tehtävät kääntävät merkkijonot myöhemmin, älä
  käännä tässä mitään muuta kuin alla luetellut tiedostot.

  Luo `src/Cleaner.Core/Localization/Loc.cs` ja `LocalizedText.cs` täsmälleen tällä julkisella rajapinnalla
  (muut tehtävät on kirjoitettu sitä vasten):

  ```csharp
  namespace Cleaner.Core.Localization;

  public enum Language { En, Fi }

  public static class Loc
  {
      /// Oletus Language.En. Setteri nostaa Changed-tapahtuman vain, kun arvo todella muuttuu.
      /// Säieturvallinen luku (volatile-kenttä tai Volatile.Read/Write).
      public static Language Current { get; set; }
      public static event EventHandler? Changed;
      /// CultureInfo.GetCultureInfo("en-US") tai ("fi-FI") nykyisen kielen mukaan.
      public static CultureInfo Culture { get; }
      /// Palauttaa en tai fi nykyisen kielen mukaan.
      public static string T(string en, string fi);
      /// "en" tai "fi".
      public static string ToCode(Language language);
      /// Hyväksyy "en"/"fi" kirjainkoosta ja ympäröivistä välilyönneistä riippumatta; null ja muut → false.
      public static bool TryParse(string? code, out Language language);
  }

  public readonly record struct LocalizedText(string En, string Fi)
  {
      public static implicit operator LocalizedText(string text) => new(text, text);
      public override string ToString() => Loc.T(En, Fi);
  }
  ```

  Luo `src/Cleaner.Core/Services/AppSettings.cs` (nimiavaruus `Cleaner.Core.Services`):

  ```csharp
  public sealed class AppSettings
  {
      public string Language { get; set; } = "en";
      /// %LOCALAPPDATA%\PcCleaner\settings.json
      public static string DefaultPath { get; }
      /// Puuttuva, rikkinäinen tai luvuton tiedosto → oletusarvot. Ei heitä koskaan.
      public static AppSettings Load(string? path = null);
      /// Luo kansion tarvittaessa. Kirjoittaa JSONin { "language": "en" } (camelCase, sisennetty). Saa heittää IO-poikkeuksen.
      public void Save(string? path = null);
      /// Loc.TryParse(Language) onnistuu → se kieli, muuten Language.En.
      public Cleaner.Core.Localization.Language ResolveLanguage();
  }
  ```
  Käytä System.Text.Jsonia (on jo käytössä `RuleLoader.cs`-tiedostossa, katso sen asetukset). Ei uusia paketteja.

  `ByteFormatter.Format` (`src/Cleaner.Core/Services/ByteFormatter.cs`): englanniksi yksiköt `B, KB, MB, GB, TB` ja
  muotoilu `Loc.Culture`-arvolla (en-US: `1.46 MB`, `0 B`, `512 B`), suomeksi nykyiset yksiköt `t, kt, Mt, Gt, Tt` ja
  fi-FI (`1,46 Mt`, `0 t`, `512 t`). Muotoilu ei saa riippua `CultureInfo.CurrentCulture`-arvosta. Suomenkielinen
  tuloste pysyy merkki merkiltä samana kuin nyt tällä fi-FI-koneella. Päivitä dokumenttikommentti.

  `CleanRule` (`src/Cleaner.Core/Rules/CleanRule.cs`): lisää valinnaiset `public string? NameFi { get; init; }` ja
  `public string? DescriptionFi { get; init; }` sekä lasketut `public string DisplayName => Loc.T(Name, NameFi ?? Name);`
  ja `public string DisplayDescription => Loc.T(Description, DescriptionFi ?? Description);`. JSON-kentät
  ovat `nameFi` ja `descriptionFi`; `RuleLoader` jäsentää nimet kirjainkoosta riippumatta, joten erillistä
  kytkentää ei tarvita (tarkista). Vaihda `Category`-oletus `"Yleinen"` → `"General"` (kenttää ei käytetä missään).
  Lasketut ominaisuudet eivät saa näkyä JSON-sarjallistuksessa tai rikkoa jäsennystä; tarkista, sarjallistetaanko
  `CleanRule` missään, ja lisää tarvittaessa `[JsonIgnore]`.

  Testit (`tests/Cleaner.Tests`, xUnit 2.9.2): olemassa olevat 218 testiä tarkistavat suomenkielisiä tekstejä. Kun
  oletuskieli vaihtuu englanniksi, ne rikkoutuisivat. Luo `tests/Cleaner.Tests/TestLanguage.cs`:
  - `[ModuleInitializer]`-metodi, joka asettaa `Loc.Current = Language.Fi` ennen ensimmäistä testiä. Jos analysaattori
    varoittaa (CA2255), vaimenna se kohdennetulla `#pragma`-direktiivillä ja perustelukommentilla.
  - `[CollectionDefinition(TestLanguage.Collection, DisableParallelization = true)]`, `public const string Collection =
    "Language";`. Englantia testaavat luokat merkitään `[Collection(TestLanguage.Collection)]`, asettavat
    `Loc.Current = Language.En` ja palauttavat `Language.Fi` lopuksi (esim. `IDisposable`).
  Luo `tests/Cleaner.Tests/LocalizationTests.cs` kokoelmaan `Language`: `Loc.T` molemmilla kielillä, `Changed`
  laukeaa kerran muutoksesta eikä samasta arvosta, `TryParse` (`"EN"`, `" fi "`, `"sv"`, `null`), `LocalizedText`
  (myös implisiittinen muunnos), `ByteFormatter` molemmilla kielillä (0, 512, 1 536 000 tavua), `AppSettings`
  tilapäiskansiossa (Save + Load kierros, puuttuva tiedosto → `en`, rikkinäinen JSON → `en`, tuntematon kieli →
  `ResolveLanguage() == En`), `CleanRule.DisplayName` ja `DisplayDescription` molemmilla kielillä sekä `NameFi`-
  kentän puuttuessa. Katso nimeämis- ja rakennemalli tiedostosta `tests/Cleaner.Tests/QuarantineTests.cs`.

  Rajoitteet: älä muuta olemassa olevia testejä. Kommentit ovat suomeksi kuten muualla koodissa. Build 0 varoitusta.

### T2: LICENSE, README.md englanniksi, README.fi.md ja ARCHITECTURE.md
- Status: pending
- Model: opus
- Requirements: REQ-22, REQ-23, REQ-24, REQ-25
- Files: LICENSE, README.md, README.fi.md, docs/ARCHITECTURE.md
- Verify: `pwsh -NoProfile -c "Test-Path LICENSE, README.fi.md; git status --porcelain -- LICENSE README.md README.fi.md docs/ARCHITECTURE.md"` → `True`, `True` ja neljä muuttunutta tai uutta tiedostoa; tarkista käsin, että README.md:n ja README.fi.md:n linkit osoittavat toisiinsa ja LICENSE-tiedostoon (dokumenttitehtävä, ei koodia)
- Task:
  PcCleaner (Windows 11 -siivousohjelma, C# .NET 8, WinUI 3 + CLI) julkaistaan julkisena GitHub-repona
  https://github.com/Vilippo/PcCleaner. Samassa haarassa muut tehtävät lisäävät englannin kielen ohjelmaan. Tämä
  tehtävä kirjoittaa dokumentaation ja lisenssin. Älä koske koodiin.

  1. `LICENSE` juureen: MIT-lisenssin vakioteksti englanniksi täsmälleen OSI:n muodossa, rivi
     `Copyright (c) 2026 Vilippo`.
  2. `README.md` → kirjoita englanniksi. Lue nykyinen suomenkielinen README.md ensin ja kata kaikki sen osiot
     (rakenne, vaatimukset, käännös ja ajo, julkaisukansio, CLI-esimerkit, ohjelman poisto, rekisterin puhdistus,
     levytilan analyysi, prosessit, käynnistysohjelmat, turvallisuusperiaatteet, uuden siivouskohteen ja moduulin
     lisääminen). Kirjoita ohjelma julkiseksi projektiksi, ei "omaan käyttöön". Otsikon alle rivi
     `**English** | [Suomi](README.fi.md)`. Lisää:
     - Osio `Language`: oletuskieli on englanti; graafisessa sovelluksessa navigaatiopaneelin alaosassa on
       valitsin `English`/`Suomi`, joka vaihtaa kielen heti ja tallentaa valinnan tiedostoon
       `%LOCALAPPDATA%\PcCleaner\settings.json`; komentorivillä `--lang en|fi` (esim. `dotnet run -- --lang fi scan`),
       joka ohittaa tallennetun valinnan; jo skannatut tulokset päivittyvät uudelle kielelle seuraavalla
       skannauksella.
     - Vahvistuskehote on englanniksi `[y/N]` ja suomeksi `[k/E]` (README:ssä nyt `[k/E]`).
     - Turvallisuusperiaatteiden perään osio `Disclaimer`: ohjelma muuttaa tiedostoja, rekisteriä, käynnistysasetuksia
       ja lopettaa prosesseja; se toimitetaan sellaisenaan ilman minkäänlaista takuuta, tekijä ei vastaa
       vahingoista tai tietojen menetyksestä, käyttö on omalla vastuulla, tee varmuuskopio ennen puhdistusta.
     - Loppuun osio `License`: MIT, linkki `[LICENSE](LICENSE)`.
     Pidä CLI-komentojen, lippujen ja polkujen kirjoitusasu ennallaan. Termit: File cleanup, Browser caches,
     Recycle Bin, Startup programs, Disk usage, Registry cleanup, Uninstall programs, Processes, dry run, quarantine,
     leftovers, backup, uninstaller, administrator (amerikanenglanti).
  3. `README.fi.md`: nykyinen suomenkielinen README sisällöltään ennallaan, päivitettynä samoilla lisäyksillä
     suomeksi (osiot `Kieli`, `Vastuuvapaus`, `Lisenssi`; ohjelma kuvataan julkiseksi projektiksi). Otsikon alle rivi
     `[English](README.md) | **Suomi**`.
  4. `docs/ARCHITECTURE.md` (suomeksi): etsi sääntö, jonka mukaan kaikki käyttäjälle näkyvä teksti on suomeksi
     (noin rivi 433), ja korvaa se lokalisoinnin kuvauksella: `Cleaner.Core.Localization.Loc` (`Loc.Current`,
     `Loc.T(en, fi)`, `Loc.Changed`, `Loc.Culture`), `LocalizedText`, englanti oletuksena, sääntötiedostojen kentät
     `nameFi`/`descriptionFi` (ja prosessisäännöissä `descriptionFi`) puuttuessaan → englanti, `AppSettings` ja
     `%LOCALAPPDATA%\PcCleaner\settings.json`, CLI `--lang` > settings.json > en, UI vaihtaa kielen heti sidonnoilla,
     manifest.txt-tiedostot pysyvät suomeksi (pysyvä muoto), testit ajetaan oletuksena suomeksi ja englantia
     testataan kokoelmassa `Language`. Lisää myös LICENSE-maininta, jos dokumentissa on projektin perustiedot.

## Wave 2

Seitsemän riippumatonta tehtävää, ajetaan rinnakkain. Tiedostolistat ovat erilliset. Jokainen rakentaa vain oman
projektinsa (`--no-restore -p:BuildProjectReferences=false`). T3 rakentaa Coren erilliseen tulostekansioon, koska
T4–T9 lukevat Coren tavallista tulostetta. Koko ratkaisun build ja testit ajaa foreman aallon lopussa.

Yhteinen ohje kaikille aallon 2 tehtäville (toistettu jokaisen tehtävän tekstissä): Rajapinta on valmiina Coressa:

```csharp
using Cleaner.Core.Localization;
Loc.T("English text", "Suomenkielinen teksti")   // valitsee Loc.Current-kielen
Loc.Culture                                        // en-US / fi-FI numeromuotoiluun
public readonly record struct LocalizedText(string En, string Fi) // ToString() => Loc.T(En, Fi); implisiittinen string → LocalizedText
```

### T3: Coren palveluiden tekstit
- Status: pending
- Model: opus
- Requirements: REQ-6
- Constraints/edge: CON-2
- Files: src/Cleaner.Core/Engine/CleanerEngine.cs, src/Cleaner.Core/Rules/RuleLoader.cs, src/Cleaner.Core/Services/BackupSessions.cs, src/Cleaner.Core/Services/PathGuard.cs, src/Cleaner.Core/Services/Quarantine.cs, src/Cleaner.Core/Services/Registry/RegistryBackup.cs, src/Cleaner.Core/Services/Registry/RegistryGuard.cs, src/Cleaner.Core/Services/SafeFileDeleter.cs
- Verify: `dotnet build src/Cleaner.Core/Cleaner.Core.csproj --no-restore -p:OutDir=<scratchpad>/t3/bin/ -p:IntermediateOutputPath=<scratchpad>/t3/obj/` → 0 warnings, 0 errors (erillinen tulostekansio, ks. tehtävä)
- Task:
  PcCleaner (C# .NET 8) saa englannin kielen suomen rinnalle. Oletuskieli on englanti. Lokalisoinnin rajapinta on jo
  Coressa (`src/Cleaner.Core/Localization/Loc.cs`):

  ```csharp
  using Cleaner.Core.Localization;
  Loc.T("English text", "Suomenkielinen teksti")   // valitsee Loc.Current-kielen sillä hetkellä
  Loc.Culture                                        // CultureInfo en-US / fi-FI
  public readonly record struct LocalizedText(string En, string Fi) // ToString() => Loc.T(En, Fi)
  ```

  Tavoite: jokainen käyttäjälle näkyvä suomenkielinen merkkijono näissä tiedostoissa muuttuu muotoon
  `Loc.T("<englanti>", "<nykyinen suomi>")`: lokiviestit (myös `[KUIVA] ` → EN `[DRY RUN] `), `ScanResult.Errors`- ja
  `CleanResult.Skipped`-syyt, out-parametrien virheviestit, kutsujalle heitettävät poikkeusviestit
  (`RuleLoader`, `CleanerEngine`), `BackupSessions`-yhteenvedot (`.reg-tiedostoa`, `karanteenikohdetta`) ja
  palautusviestit, `RegistryBackup`-yhteenveto `{n} kohdetta`, `PathGuard`-, `RegistryGuard`- ja
  `SafeFileDeleter`-syyt (`ESTETTY` → `BLOCKED`, `Ei oikeuksia` → `Access denied`). Interpoloidut merkkijonot:
  kirjoita molemmat versiot interpoloituina. Kulttuuririippuvainen numeromuotoilu käyttäjälle näkyvässä tekstissä
  käyttää `Loc.Culture` (esim. `string.Create(Loc.Culture, $"...")` tai `x.ToString("0.##", Loc.Culture)`).

  Rajoitteet:
  - Suomenkielinen versio on merkki merkiltä sama kuin nykyinen teksti (testit tarkistavat sitä). Älä muotoile
    uudelleen.
  - `manifest.txt`-tiedostojen sisältö pysyy ennallaan suomeksi, myös rivi `Käsitellyt kohteet:`, jota
    `RegistryBackup` jäsentää (rivit noin `Quarantine.cs` 203–211 ja `RegistryBackup.cs` 83–90, 110). Ne ovat
    pysyvä tiedostomuoto, ja vanhat varmuuskopiot on voitava lukea. Jätä niihin kommentti, miksi niitä ei käännetä.
  - Tunnisteet, polut, aikaleimat ja tiedostomuodot eivät muutu. Ei julkisen rajapinnan muutoksia, paitsi yksi:
    `Quarantine.AccessDeniedMessage` (noin rivi 15, nyt `public const string`, ainoa lukija rivi noin 145) muuttuu
    muotoon `public static string AccessDeniedMessage => Loc.T("Access denied (requires administrator rights)",
    "Ei oikeuksia (tarvitsee järjestelmänvalvojan oikeudet)");`.
  - Dokumenttikommentit pysyvät suomeksi.
  - Älä koske muihin tiedostoihin, erityisesti ei `ByteFormatter.cs`, `CleanRule.cs`, `Localization/*` (valmiit).
  - Sanasto (amerikanenglanti): dry run, quarantine, leftovers, backup, uninstaller, administrator,
    "Item does not belong to this module", "Invalid item id", "Access denied", "Item no longer exists",
    "Requires administrator rights", "Backup failed: {x}", "BLOCKED", "Protected", "Canceled", "Done", `{n} items`.

  Verify: muut samanaikaiset tehtävät lukevat Coren tavallista `bin`/`obj`-tulostetta, joten rakenna Core erilliseen
  kansioon: `dotnet build src/Cleaner.Core/Cleaner.Core.csproj --no-restore -p:OutDir=<scratchpad>/t3/bin/
  -p:IntermediateOutputPath=<scratchpad>/t3/obj/`, jossa `<scratchpad>` on sinulle annettu tilapäiskansio (absoluuttinen
  polku, kauttaviiva lopussa). Älä aja `dotnet test` tai koko ratkaisun buildia.

### T4: Tiedostomoduulit ja sääntötiedostot
- Status: pending
- Model: opus
- Requirements: REQ-7, REQ-8
- Constraints/edge: EDGE-2
- Files: src/Cleaner.Modules.Files/FileCleanerModule.cs, src/Cleaner.Modules.Files/RecycleBinModule.cs, src/Cleaner.Modules.Files/rules/files.json, src/Cleaner.Modules.Files/rules/browsers.json
- Verify: `dotnet build src/Cleaner.Modules.Files/Cleaner.Modules.Files.csproj --no-restore -p:BuildProjectReferences=false` → 0 warnings, 0 errors
- Task:
  PcCleaner (C# .NET 8) saa englannin kielen suomen rinnalle. Oletuskieli on englanti. Lokalisoinnin rajapinta on jo
  Coressa:

  ```csharp
  using Cleaner.Core.Localization;
  Loc.T("English text", "Suomenkielinen teksti")   // valitsee Loc.Current-kielen sillä hetkellä
  Loc.Culture                                        // CultureInfo en-US / fi-FI
  public readonly record struct LocalizedText(string En, string Fi) // ToString() => Loc.T(En, Fi); implisiittinen string → LocalizedText
  // CleanRule (Core) sisältää jo: string? NameFi, string? DescriptionFi,
  //   string DisplayName => Loc.T(Name, NameFi ?? Name), string DisplayDescription => Loc.T(Description, DescriptionFi ?? Description)
  ```

  `FileCleanerModule`:
  - Konstruktorin ja `FromRuleFile`-metodin `displayName`- ja `description`-parametrit vaihtuvat tyyppiin
    `LocalizedText`. `DisplayName`/`Description`-ominaisuudet palauttavat `ToString()`-arvon joka lukukerralla, jotta
    kielen vaihto näkyy heti. Implisiittinen muunnos pitää nykyiset string-kutsut (`src/Cleaner.Cli/ModuleFactory.cs`,
    `src/Cleaner.UI/Services/UiModuleFactory.cs`, testit) kääntyvinä; älä muokkaa niitä, myöhempi tehtävä tekee sen.
  - Kohteen kategoria on `rule.DisplayName` (nyt `rule.Name`) ja kuvaus `rule.DisplayDescription`. Myös edistymis- ja
    lokiviestit, joissa säännön nimi näkyy, käyttävät `rule.DisplayName`.
  - Kaikki moduulin omat suomenkieliset tekstit (virheet, lokit, edistyminen `Valmis`, ohitussyyt) muotoon
    `Loc.T("<englanti>", "<nykyinen suomi>")`.

  `RecycleBinModule`: nimi `Recycle Bin`/`Roskakori`, kuvaus, kategoria, polkuteksti `Roskakori (kaikki asemat)`,
  kuvaukset, virheet ja lokit `Loc.T`-pareiksi.

  `rules/files.json` ja `rules/browsers.json`: jokaiselle säännölle `name` ja `description` englanniksi, nykyinen
  suomenkielinen teksti sellaisenaan kenttiin `nameFi` ja `descriptionFi` (heti vastaavan englanninkielisen kentän
  perään). Otsakekommentit englanniksi, ja ne mainitsevat valinnaiset `nameFi`/`descriptionFi`-kentät (puuttuessa
  käytetään englantia). browsers.json `"category": "Selaimet"` → `"Browsers"`. Selainten tuotenimet säilyvät
  (`Microsoft Edge cache` jne.). Älä muuta `id`-, `paths`-, `patterns`-, `risk`- tai muita kenttiä.

  Rajoitteet:
  - Suomenkieliset versiot merkki merkiltä samat kuin nyt (testit tarkistavat niitä suomeksi). Ei uudelleenmuotoilua.
  - Kohdetunnisteet (`Id`) eivät muutu.
  - Kulttuuririippuvainen numeromuotoilu käyttäjälle näkyvässä tekstissä käyttää `Loc.Culture`.
  - Dokumenttikommentit pysyvät suomeksi. Älä koske muihin tiedostoihin (Core on toisen tehtävän alla).
  - Sanasto: File cleanup, Browser caches, Recycle Bin, dry run, "Item does not belong to this module",
    "Invalid item id", "Access denied", "Item no longer exists", "Requires administrator rights", "Done",
    `{n} items` (amerikanenglanti).
  - Verify rakentaa vain tämän projektin (`--no-restore -p:BuildProjectReferences=false`), koska samanaikaiset
    tehtävät rakentavat muita projekteja. Älä aja `dotnet test` tai koko ratkaisun buildia.

### T5: Käynnistysohjelmat-moduuli
- Status: pending
- Model: opus
- Requirements: REQ-9
- Files: src/Cleaner.Modules.Startup/StartupModule.cs, src/Cleaner.Modules.Startup/StartupSource.cs, src/Cleaner.Modules.Startup/StoreStartupTaskSource.cs, src/Cleaner.Modules.Startup/TaskSchedulerSource.cs
- Verify: `dotnet build src/Cleaner.Modules.Startup/Cleaner.Modules.Startup.csproj --no-restore -p:BuildProjectReferences=false` → 0 warnings, 0 errors
- Task:
  PcCleaner (C# .NET 8) saa englannin kielen suomen rinnalle. Oletuskieli on englanti. Lokalisoinnin rajapinta on jo
  Coressa:

  ```csharp
  using Cleaner.Core.Localization;
  Loc.T("English text", "Suomenkielinen teksti")   // valitsee Loc.Current-kielen sillä hetkellä
  Loc.Culture                                        // CultureInfo en-US / fi-FI
  public readonly record struct LocalizedText(string En, string Fi) // ToString() => Loc.T(En, Fi); implisiittinen string → LocalizedText
  ```

  Tavoite: jokainen käyttäjälle näkyvä suomenkielinen merkkijono Käynnistysohjelmat-moduulissa muuttuu muotoon
  `Loc.T("<englanti>", "<nykyinen suomi>")`: `DisplayName` (`Startup programs`), `Description`, kategoriat
  (`TaskCategory`, `StartupSource`-lähteiden näyttönimet, `StoreStartupTaskSource.Category`), kohdekuvaukset
  (myös `(muu kuin ohjelman käynnistys)`), `ScanResult.Errors`, poikkeusviestit, edistyminen, lokit ja ohitussyyt.
  Näyttönimien on palautettava nykyisen kielen arvo joka lukukerralla. Jos `public const string` -kategoriavakio
  muuttuu laskettavaksi (`public static string X => Loc.T(...)`), tarkista sen käyttökohteet tässä projektissa ja
  testeissä (`tests/Cleaner.Tests/StoreStartupTaskSourceTests.cs` vertaa `StoreStartupTaskSource.Category`-arvoon);
  `case`-lausekkeet ja attribuutit eivät hyväksy ei-vakioita, joten korjaa tämän projektin sellaiset käytöt.
  `StartupSource`-lähteen sisäinen tunniste (joka on osa kohteen `Id`-arvoa, esim. `hkcu-run`) ei saa muuttua.

  Rajoitteet:
  - Suomenkieliset versiot merkki merkiltä samat kuin nyt (testit tarkistavat niitä suomeksi). Ei uudelleenmuotoilua.
  - Kohdetunnisteet, rekisteripolut ja `StartupApproved`-arvomuoto eivät muutu.
  - Kulttuuririippuvainen numeromuotoilu käyttäjälle näkyvässä tekstissä käyttää `Loc.Culture`.
  - Dokumenttikommentit pysyvät suomeksi. Älä koske muihin tiedostoihin, ei myöskään testeihin.
  - Sanasto: Startup programs, dry run, administrator, "Item does not belong to this module", "Invalid item id",
    "Access denied", "Item no longer exists", "Requires administrator rights", "Done", "Protected"
    (amerikanenglanti).
  - Verify rakentaa vain tämän projektin (`--no-restore -p:BuildProjectReferences=false`). Älä aja `dotnet test` tai
    koko ratkaisun buildia.

### T6: Levytilan analyysi -moduuli
- Status: pending
- Model: opus
- Requirements: REQ-10, REQ-14
- Files: src/Cleaner.Modules.DiskUsage/DiskUsageModule.cs
- Verify: `dotnet build src/Cleaner.Modules.DiskUsage/Cleaner.Modules.DiskUsage.csproj --no-restore -p:BuildProjectReferences=false` → 0 warnings, 0 errors
- Task:
  PcCleaner (C# .NET 8) saa englannin kielen suomen rinnalle. Oletuskieli on englanti. Lokalisoinnin rajapinta on jo
  Coressa:

  ```csharp
  using Cleaner.Core.Localization;
  Loc.T("English text", "Suomenkielinen teksti")   // valitsee Loc.Current-kielen sillä hetkellä
  Loc.Culture                                        // CultureInfo en-US / fi-FI
  ```

  Tavoite: jokainen käyttäjälle näkyvä suomenkielinen merkkijono `DiskUsageModule.cs`-tiedostossa muuttuu muotoon
  `Loc.T("<englanti>", "<nykyinen suomi>")`: `DisplayName` (`Disk usage`), `Description`, kategoriavakiot (rivit
  noin 29–32), kuvaukset (rivit noin 143, 159, 169), virheet, edistyminen (`Valmis` → `Done`), lokit ja ohitussyyt
  (`Kansioita ei poisteta` jne.). Kategoriavakiot ovat julkisia ja testit vertaavat niihin
  (`tests/Cleaner.Tests/DiskUsageModuleTests.cs`); jos `const` muuttuu laskettavaksi `static`-ominaisuudeksi, tarkista
  tämän projektin `case`-/attribuuttikäytöt. `{share:P0}` ja muu numeromuotoilu käyttävät `Loc.Culture`
  (suomeksi tulos pysyy `62 %`). Älä koske `DirectoryNode.cs`-tiedostoon: sen `Combine`-nimiö (`Kaikki asemat`) tulee
  UI:sta.

  Rajoitteet:
  - Suomenkieliset versiot merkki merkiltä samat kuin nyt. Ei uudelleenmuotoilua.
  - Kohdetunnisteet (`DirPrefix` ym.) eivät muutu.
  - Dokumenttikommentit pysyvät suomeksi. Älä koske muihin tiedostoihin, ei myöskään testeihin.
  - Sanasto: Disk usage, "Item does not belong to this module", "Invalid item id", "Access denied",
    "Item no longer exists", "Done", `{n} items` (amerikanenglanti).
  - Verify rakentaa vain tämän projektin (`--no-restore -p:BuildProjectReferences=false`). Älä aja `dotnet test` tai
    koko ratkaisun buildia.

### T7: Rekisterin puhdistus -moduuli
- Status: pending
- Model: opus
- Requirements: REQ-11
- Files: src/Cleaner.Modules.Registry/RegistryModule.cs
- Verify: `dotnet build src/Cleaner.Modules.Registry/Cleaner.Modules.Registry.csproj --no-restore -p:BuildProjectReferences=false` → 0 warnings, 0 errors
- Task:
  PcCleaner (C# .NET 8) saa englannin kielen suomen rinnalle. Oletuskieli on englanti. Lokalisoinnin rajapinta on jo
  Coressa:

  ```csharp
  using Cleaner.Core.Localization;
  Loc.T("English text", "Suomenkielinen teksti")   // valitsee Loc.Current-kielen sillä hetkellä
  Loc.Culture                                        // CultureInfo en-US / fi-FI
  ```

  Tavoite: jokainen käyttäjälle näkyvä suomenkielinen merkkijono `RegistryModule.cs`-tiedostossa (nimiavaruus
  `Cleaner.Modules.RegistryCleaner`) muuttuu muotoon `Loc.T("<englanti>", "<nykyinen suomi>")`: `DisplayName`
  (`Registry cleanup`), `Description`, kategoriavakiot (rivit noin 30–33, testit vertaavat `MruCategory`-arvoon),
  kuvaukset (`{n} merkintää` jne.), virheet, poikkeukset, edistyminen, lokit, `Describe()`-tekstit ja ohitussyyt.
  Jos `const` muuttuu laskettavaksi `static`-ominaisuudeksi, tarkista tämän projektin `case`-/attribuuttikäytöt.

  Rajoitteet:
  - Suomenkieliset versiot merkki merkiltä samat kuin nyt (testit tarkistavat esim. `3 merkintää`, `6 kohdetta`,
    `Suojattu`). Ei uudelleenmuotoilua.
  - Kohdetunnisteet (`registry:mru:` ym.), rekisteripolut ja `.reg`-varmuuskopioiden muoto eivät muutu.
  - Kulttuuririippuvainen numeromuotoilu käyttäjälle näkyvässä tekstissä käyttää `Loc.Culture`.
  - Dokumenttikommentit pysyvät suomeksi. Älä koske muihin tiedostoihin, ei myöskään testeihin.
  - Sanasto: Registry cleanup, backup, dry run, "Item does not belong to this module", "Invalid item id",
    "Access denied", "Item no longer exists", "Requires administrator rights", "Backup failed: {x}", "Protected",
    "Done", `{n} items` (amerikanenglanti).
  - Verify rakentaa vain tämän projektin (`--no-restore -p:BuildProjectReferences=false`). Älä aja `dotnet test` tai
    koko ratkaisun buildia.

### T8: Ohjelman poisto -moduuli
- Status: pending
- Model: opus
- Requirements: REQ-12
- Files: src/Cleaner.Modules.Uninstall/UninstallModule.cs, src/Cleaner.Modules.Uninstall/LeftoverScanner.cs, src/Cleaner.Modules.Uninstall/LeftoverGuard.cs, src/Cleaner.Modules.Uninstall/UninstallRunner.cs
- Verify: `dotnet build src/Cleaner.Modules.Uninstall/Cleaner.Modules.Uninstall.csproj --no-restore -p:BuildProjectReferences=false` → 0 warnings, 0 errors
- Task:
  PcCleaner (C# .NET 8) saa englannin kielen suomen rinnalle. Oletuskieli on englanti. Lokalisoinnin rajapinta on jo
  Coressa:

  ```csharp
  using Cleaner.Core.Localization;
  Loc.T("English text", "Suomenkielinen teksti")   // valitsee Loc.Current-kielen sillä hetkellä
  Loc.Culture                                        // CultureInfo en-US / fi-FI
  ```

  Tavoite: jokainen käyttäjälle näkyvä suomenkielinen merkkijono näissä neljässä tiedostossa muuttuu muotoon
  `Loc.T("<englanti>", "<nykyinen suomi>")`: `DisplayName` (`Uninstall programs`), `Description`, `LeftoverScanner`-
  kategoriavakiot (rivit noin 16–21; testit vertaavat niihin), kuvaukset, `MatchReason`-osat (rivit noin 513–516),
  liite ` (ohjelma on yhä asennettu, ei esivalittu)`, polkuteksti `(oletus)`, `UninstallRunResult.Error`-viestit,
  `ScanResult.Errors` (`Valitse ohjelma ensin.`), poikkeukset, edistyminen, lokit (myös `Describe()`), ohitussyyt ja
  `UninstallRunner`-lokien `kyllä`/`ei`. Jos `const` muuttuu laskettavaksi `static`-ominaisuudeksi, tarkista tämän
  projektin `case`-/attribuuttikäytöt. Älä koske `NameMatcher.cs`-tiedostoon (siinä ei ole käyttäjälle näkyvää
  tekstiä).

  Rajoitteet:
  - Suomenkieliset versiot merkki merkiltä samat kuin nyt (testit tarkistavat esim. `yhä asennettu`,
    `Rekisteriarvot`, `Valmis`, `Valitse ohjelma ensin.`, `Peruskansiota ei poisteta`). Ei uudelleenmuotoilua.
  - Kohdetunnisteet (`uninstall:dir:` ym.), karanteenin rakenne ja manifest-muoto eivät muutu.
  - Kulttuuririippuvainen numeromuotoilu käyttäjälle näkyvässä tekstissä käyttää `Loc.Culture`.
  - Dokumenttikommentit pysyvät suomeksi. Älä koske muihin tiedostoihin, ei myöskään testeihin.
  - Sanasto: Uninstall programs, uninstaller, leftovers, quarantine, backup, dry run, administrator,
    "Item does not belong to this module", "Invalid item id", "Access denied", "Item no longer exists",
    "Requires administrator rights", "Backup failed: {x}", "Protected", "Done" (amerikanenglanti).
  - Verify rakentaa vain tämän projektin (`--no-restore -p:BuildProjectReferences=false`). Älä aja `dotnet test` tai
    koko ratkaisun buildia.

### T9: Prosessit-moduuli ja prosessisäännöt
- Status: pending
- Model: opus
- Requirements: REQ-13
- Files: src/Cleaner.Modules.Processes/ProcessesModule.cs, src/Cleaner.Modules.Processes/ProcessDescription.cs, src/Cleaner.Modules.Processes/ProcessGuard.cs, src/Cleaner.Modules.Processes/ProcessTerminator.cs, src/Cleaner.Modules.Processes/ProcessInventory.cs, src/Cleaner.Modules.Processes/ProcessRuleLoader.cs, src/Cleaner.Modules.Processes/ProcessRule.cs, src/Cleaner.Modules.Processes/rules/processes/known.json
- Verify: `dotnet build src/Cleaner.Modules.Processes/Cleaner.Modules.Processes.csproj --no-restore -p:BuildProjectReferences=false` → 0 warnings, 0 errors
- Task:
  PcCleaner (C# .NET 8) saa englannin kielen suomen rinnalle. Oletuskieli on englanti. Lokalisoinnin rajapinta on jo
  Coressa:

  ```csharp
  using Cleaner.Core.Localization;
  Loc.T("English text", "Suomenkielinen teksti")   // valitsee Loc.Current-kielen sillä hetkellä
  Loc.Culture                                        // CultureInfo en-US / fi-FI
  ```

  Tavoite: jokainen käyttäjälle näkyvä suomenkielinen merkkijono luetelluissa tiedostoissa muuttuu muotoon
  `Loc.T("<englanti>", "<nykyinen suomi>")`: `DisplayName` (`Processes`), `Description`, kategoriat
  (`ProcessDescription.CategoryName`), kuvausten osat (`ProcessDescription.Build`, myös `FormatAge`-yksiköt
  `min`/`h`/`d`, jotka ovat englanniksi samat), `ProcessGuard`- ja `ProcessTerminator`-syyt, `ProcessInventory`-
  virheet, `ProcessRuleLoader`-poikkeukset, edistyminen, lokit ja ohitussyyt. Sekuntimuotoilu
  (`{SampleInterval.TotalSeconds:0}`) ja muu numeromuotoilu käyttävät `Loc.Culture`. Erotin ` · ` pysyy.

  `ProcessRule`: lisää valinnainen `public string? DescriptionFi { get; init; }` (JSON `descriptionFi`; lataaja
  jäsentää nimet kirjainkoosta riippumatta, tarkista) ja laskettava
  `public string DisplayDescription => Loc.T(Description, DescriptionFi ?? Description);`. `ProcessDescription.Build`
  käyttää `Rule.DisplayDescription`-arvoa. Jos `ProcessRuleLoader` validoi kenttiä, `descriptionFi` on valinnainen.
  `rules/processes/known.json`: `description` englanniksi, nykyinen suomenkielinen teksti sellaisenaan kenttään
  `descriptionFi` heti perään. Otsakekommentit englanniksi. `name` (tuotenimet) ja muut kentät eivät muutu.

  Rajoitteet:
  - Suomenkieliset versiot merkki merkiltä samat kuin nyt (testit tarkistavat esim. `Järjestelmäprosessi`,
    `2 aliprosessia`, `käynnistäjä explorer (PID 50)`, `ei suorittimen käyttöä 3 s aikana`, `Lopetettiin ...`,
    `[KUIVA] Lopetettaisiin `, `PID on käytetty uudelleen`). Ei uudelleenmuotoilua.
  - Kohdetunniste `processes:<pid>:<startFileTime>` ja `ProcessItemId` eivät muutu.
  - Dokumenttikommentit pysyvät suomeksi. Älä koske muihin tiedostoihin, ei myöskään testeihin.
  - Sanasto: Processes, dry run (`[DRY RUN] `), "Item does not belong to this module", "Invalid item id",
    "Access denied", "Item no longer exists", "Protected", "Done" (amerikanenglanti).
  - Verify rakentaa vain tämän projektin (`--no-restore -p:BuildProjectReferences=false`). Älä aja `dotnet test` tai
    koko ratkaisun buildia.

## Wave 3

Kolme riippumatonta tehtävää, ajetaan rinnakkain: T10 koskee vain CLI-projektia, T11 vain UI-projektia, T12 vain
uutta testitiedostoa. Kaikki rakentavat vain oman projektinsa `-p:BuildProjectReferences=false`-lipulla; aallon 2
lopun build on jo tuottanut riippuvuuksien tulosteet.

### T10: CLI: --lang ja tekstit
- Status: pending
- Model: opus
- Requirements: REQ-15, REQ-16, REQ-17
- Constraints/edge: EDGE-5
- Files: src/Cleaner.Cli/Program.cs, src/Cleaner.Cli/UninstallCommand.cs, src/Cleaner.Cli/RestoreCommand.cs, src/Cleaner.Cli/ModuleFactory.cs
- Verify: `dotnet build src/Cleaner.Cli/Cleaner.Cli.csproj --no-restore -p:BuildProjectReferences=false` → 0 warnings, 0 errors; `dotnet run --project src/Cleaner.Cli --no-build -- modules` → englanninkieliset moduulinimet (File cleanup, Browser caches, Recycle Bin, ...); `dotnet run --project src/Cleaner.Cli --no-build -- --lang fi modules` → suomenkieliset nimet; `dotnet run --project src/Cleaner.Cli --no-build -- --lang xx modules` → virheilmoitus, paluukoodi 2; `dotnet run --project src/Cleaner.Cli --no-build -- --help` ja `-- --lang fi --help` → ohjeteksti oikealla kielellä, mainitsee `--lang`
- Task:
  PcCleaner (C# .NET 8) saa englannin kielen suomen rinnalle; oletus on englanti. Core ja moduulit on jo käännetty.
  Tämä tehtävä tekee komentorivin (`src/Cleaner.Cli`, exe `pccleaner`). Valmiina Coressa:

  ```csharp
  using Cleaner.Core.Localization;   // Loc.Current (Language.En/Fi), Loc.T(en, fi), Loc.Culture, Loc.TryParse(code, out lang), Loc.ToCode(lang)
  // public readonly record struct LocalizedText(string En, string Fi) — implisiittinen string → LocalizedText
  using Cleaner.Core.Services;       // AppSettings.Load() (ei heitä), settings.ResolveLanguage() → Language
  // FileCleanerModule.FromRuleFile(id, LocalizedText displayName, LocalizedText description, path, filter)
  ```

  1. Kieli (`Program.cs`, top-level statements, käsin kirjoitettu jäsennys, apufunktiot `Flag`/`Values` noin
     rivillä 249–261): heti `args_`-listan luonnin jälkeen ja ennen `--help`-tarkistusta ja komennon lukua
     (`args_[0]`, noin rivi 20) aseta ensin `Loc.Current = AppSettings.Load().ResolveLanguage()`, poista sitten
     kaikki `--lang <arvo>` -parit argumenteista ja aseta viimeisen arvon kieli. Näin `pccleaner --lang fi scan` toimii.
     `--lang` ilman arvoa tai tuntematon arvo → virheilmoitus stderriin (kielellä settings.json > en), paluukoodi 2.
     CLI ei kirjoita `settings.json`-tiedostoa.
  2. Kaikki suomenkieliset tulosteet `Program.cs`-, `UninstallCommand.cs`- ja `RestoreCommand.cs`-tiedostoissa
     muotoon `Loc.T("<englanti>", "<nykyinen suomi>")`: banneri (`järjestelmänvalvoja`/`tavallinen käyttäjä`/`loki:`),
     `modules`-lista (`riski=`, `admin=kyllä/ei`, `vain --module`; tasaa sarakkeet molemmilla kielillä), `kpl`,
     virheet, yhteenvedot, taulukko-otsikot (`Nimi`, `Versio`, `Julkaisija`, `Asennettu`, `Koko`, `Avain`) ja
     peruutusviestit. Ohjeteksti (`PrintHelp`, raw string noin rivit 265–310): kaksi versiota (englanti ja nykyinen
     suomi), kumpikin mainitsee `--lang en|fi` ja sen, että oletus tulee settings.json-tiedostosta tai on englanti.
  3. Vahvistuskehotteet (`Program.cs` noin 216, `UninstallCommand.cs` noin 122, `RestoreCommand.cs` noin 39):
     englanniksi `[y/N]`, suomeksi `[k/E]`. Molemmilla kielillä hyväksytään `y`, `yes`, `k`, `kyllä`
     (`Trim().ToLowerInvariant()`), kaikki muu peruuttaa. `--yes` ohittaa kuten ennen.
  4. `ModuleFactory.cs`: `new LocalizedText("File cleanup", "Tiedostosiivous")`, kuvaus
     `("Temporary files, caches, logs.", "Väliaikaistiedostot, välimuistit, lokit.")`, `("Browser caches",
     "Selainten välimuistit")` ja `"Edge, Chrome, Firefox, Brave."` sekä lokivaroitus `Sääntötiedostoa ei löydy`
     `Loc.T`-pariksi.
  Riskitasot (`{m.Risk}`) tulostuvat edelleen enum-niminä (Low/Medium/High), se on molemmilla kielillä sama.

  Rajoitteet: komentojen ja lippujen nimet, `--sort`-arvot (myös `nimi`, `paiva`, `päivä`), kohdetunnisteet,
  aikaleimat ja paluukoodit eivät muutu. Suomenkieliset tekstit merkki merkiltä samat kuin nyt. Ei ulkoisia
  kirjastoja. Kommentit suomeksi. `ConsoleLogger.cs` ei sisällä käännettävää. Älä koske muihin tiedostoihin.
  Sanasto (amerikanenglanti): File cleanup, Browser caches, dry run, quarantine, leftovers, backup, uninstaller,
  administrator, Canceled, Done, `{n} items`.
  Verify rakentaa vain tämän projektin (`--no-restore -p:BuildProjectReferences=false`), koska samanaikaiset tehtävät
  rakentavat UI- ja testiprojektia. `dotnet run --no-build` ajaa juuri rakennetun exen.

### T11: Graafinen sovellus: kielivalitsin, välitön vaihto ja tekstit
- Status: pending
- Model: opus
- Requirements: REQ-18, REQ-19, REQ-20, REQ-21
- Constraints/edge: EDGE-3, EDGE-4, EDGE-6
- Files: src/Cleaner.UI/App.xaml.cs, src/Cleaner.UI/MainWindow.xaml, src/Cleaner.UI/MainWindow.xaml.cs, src/Cleaner.UI/Services/UiText.cs, src/Cleaner.UI/Services/UiLogger.cs, src/Cleaner.UI/Services/UiModuleFactory.cs, src/Cleaner.UI/Services/IDialogService.cs, src/Cleaner.UI/ViewModels/MainViewModel.cs, src/Cleaner.UI/ViewModels/ModuleViewModel.cs, src/Cleaner.UI/ViewModels/CategoryViewModel.cs, src/Cleaner.UI/ViewModels/InstalledProgramViewModel.cs
- Verify: `dotnet build src/Cleaner.UI/Cleaner.UI.csproj --no-restore -p:BuildProjectReferences=false` → 0 warnings, 0 errors
  Manual (käyttäjä, vaatii UAC-hyväksynnän): käynnistä `PcCleaner.exe`, tarkista että oletus on englanti, vaihda
  Suomi → kaikki napit, otsikot, vihjeet, moduulien nimet ja tilarivit vaihtuvat heti; skannaa, vaihda kieli, avaa
  puhdistusdialogi → dialogi uudella kielellä; käynnistä uudelleen → valinta säilyy
- Task:
  PcCleaner (C# .NET 8) saa englannin kielen suomen rinnalle; oletus on englanti. Core ja moduulit on jo käännetty.
  Tämä tehtävä tekee WinUI 3 -sovelluksen (`src/Cleaner.UI`, unpackaged, CommunityToolkit.Mvvm 8.4.2, yksi sivu:
  NavigationView, jonka `MenuItemsSource` on `ViewModel.Modules`). Valmiina Coressa:

  ```csharp
  using Cleaner.Core.Localization;   // Loc.Current (Language.En/Fi; setteri nostaa Loc.Changed vain muutoksesta),
                                     // static event EventHandler? Loc.Changed, Loc.T(en, fi), Loc.Culture, Loc.ToCode
  // public readonly record struct LocalizedText(string En, string Fi) — implisiittinen string → LocalizedText
  using Cleaner.Core.Services;       // AppSettings.Load() (ei heitä), .ResolveLanguage(), .Language ("en"/"fi"), .Save() (voi heittää IO-poikkeuksen)
  // FileCleanerModule.FromRuleFile(id, LocalizedText displayName, LocalizedText description, path, filter)
  // ICleanerModule.DisplayName/Description palauttavat nykyisen kielen arvon joka lukukerralla.
  // ByteFormatter.Format(bytes) noudattaa Loc.Current-kieltä.
  ```

  Vaatimukset:
  1. Käynnistys (`App.xaml.cs` `OnLaunched`): `Loc.Current = AppSettings.Load().ResolveLanguage();` ennen
     `new MainWindow()`. Oletus on englanti (EDGE-1).
  2. `src/Cleaner.UI/Services/UiText.cs`: `public sealed partial class UiText : ObservableObject`, jossa jokaiselle
     XAML-tekstille get-only ominaisuus `=> Loc.T("...", "...")` ja metodi `Refresh()`, joka kutsuu
     `OnPropertyChanged(string.Empty)`. `MainViewModel` paljastaa sen ominaisuutena `T`. Jokainen XAML-literaali
     `MainWindow.xaml`-tiedostossa (noin 30 kpl: InfoBarit, PaneTitle, "Skannaa kaikki", ToolTipit, Header,
     PlaceholderText, napit, ComboBoxItemit "Nimi"/"Asennuspäivä, uusin ensin", "Korkean riskin kohteet",
     "Ei löydöksiä.", alapalkin napit, "Loki", "Tyhjennä") sidotaan muodossa `{x:Bind ViewModel.T.X, Mode=OneWay}`.
     Poikkeus: DataTemplate-mallien sisällä `ViewModel` ei ratkea (esim. XAML noin rivi 144
     `<DataTemplate x:DataType="vm:InstalledProgramViewModel">` ja sen rivin noin 167 ToolTip "Asennuspäivä").
     Sellainen teksti tulee mallin oman näkymämallin ominaisuudesta (esim.
     `InstalledProgramViewModel.InstallDateToolTip => Loc.T("Install date", "Asennuspäivä")`), sidottuna `Mode=OneWay`.
     Tarkista, että x:Bindin generoima koodi päivittää tyhjällä ominaisuusnimellä kaikki sidonnat; jos ei, nosta
     `PropertyChanged` jokaiselle nimelle erikseen.
  3. Kielivalitsin NavigationView-paneelin alaosaan (`PaneFooter`, "Skannaa kaikki" -napin yhteyteen): ComboBox,
     kohdat `English` ja `Suomi` (aina näin, ei käännetä), ToolTip `Language / Kieli`, valinta näyttää nykyisen
     kielen, ja valitsin on pois käytöstä aina kun sovellus on kiireinen (skannaus, puhdistus, palautus, poisto-
     ohjelman odotus; käytä olemassa olevaa busy-tilaa, EDGE-3). Kun valittu kieli eroaa `Loc.Current`-arvosta,
     valinta asettaa `Loc.Current` ja tallentaa `new AppSettings { Language = Loc.ToCode(...) }.Save()`. Valitsimen
     alkuvalinta käynnistyksessä ei saa kirjoittaa `settings.json`-tiedostoa (EDGE-1: tiedostoa ei kirjoiteta ennen kuin
     käyttäjä vaihtaa kielen). Tallennusvirhe → varoitus lokiin, kieli vaihtuu silti (EDGE-6).
  4. `Loc.Changed`-käsittelijä (staattinen tapahtuma: tilaa `MainViewModel`issa, peru tilaus ikkunan `Closed`-
     tapahtumassa; tapahtuma voi tulla mistä säikeestä tahansa, joten aja päivitys `DispatcherQueue.TryEnqueue`-
     kutsulla): `T.Refresh()`, ikkunan otsikko (`MainWindow.xaml.cs` asettaa nyt `Title = ViewModel.TitleText`
     kerran, päivitä se vaihdossa), ja jokaiselle `ModuleViewModel`ille uusi metodi `RefreshLanguage()`, joka nostaa
     `PropertyChanged` laskettaville ominaisuuksille (`DisplayName`, `Description`, `RiskText`, `AdminHint`,
     `OptInHint`), laskee `StatusText`- ja `SelectionText`-tekstit uudelleen ja kutsuu kategorioiden
     `CategoryViewModel.RefreshLanguage()` (`RiskText`, `SummaryText`, `HiddenText`; `SummaryText` on nyt
     konstruktorissa laskettu, muuta laskettavaksi tai laske uudelleen).
     `StatusText` ei nyt ole johdettavissa tallennetusta tilasta: `ModuleViewModel` asettaa sen konstruktorissa
     (noin rivi 84) ja `LoadScan`-metodissa (noin rivit 114–118), ja `MainViewModel` kirjoittaa sen suoraan
     merkkijonona (noin rivi 206 `"Valitse ohjelma listasta ..."` ja noin rivi 235 `"Skannaus peruutettu."`). Lisää
     `ModuleViewModel`iin tilatyyppi (esim. enum `NotScanned`, `NoFindings`, `Found`, `SelectProgram`, `ScanCanceled`
     ja tarvittavat luvut), jonka mukaan `StatusText` lasketaan nykyisellä kielellä, ja vaihda molemmat
     `MainViewModel`in kirjoituskohdat asettamaan tila merkkijonon sijaan.
     Ohjelmalistan rivit: `InstalledProgramViewModel` (nyt tavallinen `sealed class` ilman `INotifyPropertyChanged`)
     muutetaan `ObservableObject`iksi, jolla on `RefreshLanguage()` (`InstallDateText` EN `yyyy-MM-dd`, FI nykyinen
     `d.M.yyyy`; `SizeText`; tooltip-ominaisuus, ks. kohta 2), ja `MainViewModel` kutsuu sitä jokaiselle `Programs`-
     riville kielen vaihtuessa. Treemapin tekstit (`MainWindow.xaml.cs` noin rivit 96–153) ja juurinimiö
     `Kaikki asemat` (`MainViewModel` noin rivi 228) piirretään uudelleen tai lasketaan seuraavalla piirrolla
     nykyisellä kielellä.
     Sidontatilat: x:Bind on oletuksena OneTime, joten `PropertyChanged` ei tavoita niitä. Vaihda `Mode=OneWay`
     ainakin näihin (rivit noin): navigaation DataTemplate `DisplayName`/`Description` 40–41, otsakkeen sidonnat
     67–79, kategorian `RiskText` 225, `SummaryText` 228 ja 304, `HiddenText` 261 ja 336, ohjelmalistan
     `InstallDateText` 163 ja `SizeText` 168, sekä kaikki muut kielestä riippuvat sidonnat, jotka löydät.
  5. Jo tuotetut moduulitulokset (kategorioiden nimet, kohteiden kuvaukset, `ErrorText`) ja jo kirjoitetut lokirivit
     jäävät ennalleen (EDGE-4). Älä yritä kääntää niitä.
  6. Kaikki muut UI:n suomenkieliset tekstit `Loc.T`-pareiksi: `MainViewModel` (noin 60 kohtaa: BusyTitle-,
     ProgressMessage- ja StatusText-tekstit, kaikki dialogit, kolmivaiheisen puhdistuksen yhteenvedot ja
     vahvistukset, palautus, ohjelman poisto, lokirivit), `ModuleViewModel`, `CategoryViewModel`, `MainWindow.xaml.cs`
     (kansiovalitsimen virhe, treemap, `IDialogService`-toteutuksen napit "Peruuta"/"Sulje"), `IDialogService.cs`
     (oletusparametri `closeText = "Peruuta"` ei voi olla `Loc.T`-kutsu; käytä `string? closeText = null` ja ratkaise
     `null` → `Loc.T("Cancel", "Peruuta")` toteutuksessa), `UiLogger` (EN `ERROR`/`WARNING`, FI `VIRHE`/`VAROITUS`;
     `debug`/`info` ennallaan), `UiModuleFactory` (`new LocalizedText("File cleanup", "Tiedostosiivous")`, kuvaus
     `("Temporary files, caches, logs.", "Väliaikaistiedostot, välimuistit, lokit.")`, `("Browser caches",
     "Selainten välimuistit")`, `"Edge, Chrome, Firefox, Brave."`, lokivaroitus). Puhdistuksen yhteenveto
     `MainViewModel` noin rivit 266–352 rakentaa lauseen suomen sanajärjestyksellä (`char.ToUpper(verb[0])`);
     kirjoita englanniksi kokonaiset lauseet per moduulityyppi sen sijaan, että verbiä liitettäisiin.
     Vahvistusteksti mainitsee `%LOCALAPPDATA%\PcCleaner\quarantine` ja `pccleaner restore` molemmilla kielillä.

  Rajoitteet: moduulien tunnistus pysyy `Id`-arvon mukaisena (ei näyttönimen). Suomenkieliset tekstit merkki merkiltä
  samat kuin nyt. Ei uusia paketteja, ei `.resw`/`x:Uid`. Kommentit suomeksi. Älä koske muihin tiedostoihin.
  Sanasto (amerikanenglanti): Scan, Scan all, Clean, Cancel, Close, Done, dry run, quarantine, leftovers, backup,
  uninstaller, administrator, High risk / Medium risk / Low risk, `{n} items`, Startup programs, Disk usage,
  Uninstall programs.
  Verify rakentaa vain tämän projektin (`--no-restore -p:BuildProjectReferences=false`). Sovellusta ei voi käynnistää
  testiympäristössä ilman UAC-hyväksyntää; raportoi käsin tarkistettavat kohdat.

### T12: Englanninkieliset moduulitestit
- Status: pending
- Model: opus
- Requirements: REQ-27
- Files: tests/Cleaner.Tests/EnglishTextTests.cs
- Verify: `dotnet build tests/Cleaner.Tests/Cleaner.Tests.csproj --no-restore -p:BuildProjectReferences=false` → 0 warnings, 0 errors; `dotnet test tests/Cleaner.Tests --no-build --filter FullyQualifiedName~EnglishTextTests` → kaikki läpi
- Task:
  PcCleaner (C# .NET 8, xUnit 2.9.2) on saanut englannin kielen. Testiprojekti ajaa oletuksena suomeksi:
  `tests/Cleaner.Tests/TestLanguage.cs` asettaa moduulialustajassa `Loc.Current = Language.Fi` ja määrittelee
  kokoelman `[CollectionDefinition(TestLanguage.Collection, DisableParallelization = true)]`
  (`TestLanguage.Collection == "Language"`). Englantia testaavat luokat merkitään
  `[Collection(TestLanguage.Collection)]`, asettavat konstruktorissa `Loc.Current = Language.En` ja palauttavat
  `Language.Fi` `Dispose`-metodissa. Mallina `tests/Cleaner.Tests/LocalizationTests.cs`.

  Luo `tests/Cleaner.Tests/EnglishTextTests.cs`, joka todistaa, että jokainen moduuli tuottaa englantia, kun
  `Loc.Current = Language.En`. Jokaisesta moduulista vähintään `DisplayName` ja yksi moduulin tuottama teksti:
  - `FileCleanerModule`: säännöllä, jolla on `Name`/`NameFi`/`Description`/`DescriptionFi`, skannaus tilapäiskansiossa
    → kohteen `Category` on englanninkielinen `Name` ja `Description` englanninkielinen; säännöllä ilman `NameFi`
    → `Name` molemmilla kielillä (EDGE-2). Lisäksi oikeat `rules/files.json` ja `rules/browsers.json`
    (`RuleLoader`, polku `AppContext.BaseDirectory/rules`) → jokaisella säännöllä on `NameFi` ja `DescriptionFi`.
  - `RecycleBinModule`, `StartupModule`, `DiskUsageModule`, `RegistryModule`, `UninstallModule`, `ProcessesModule`:
    `DisplayName` = `Recycle Bin`, `Startup programs`, `Disk usage`, `Registry cleanup`, `Uninstall programs`,
    `Processes`; ja kunkin moduulin yksi tuotettu englanninkielinen teksti, esim. ohitussyy kohteelle, joka ei kuulu
    moduulille (`CleanAsync` vieraalla `ModuleId`-arvolla kuivana ajona → syy ei ole suomenkielinen nykyteksti
    `Kohde ei kuulu tälle moduulille`), `DiskUsageModule`-kategoriavakio tai `ProcessDescription.CategoryName`.
    Lue kunkin moduulin koodi ja olemassa olevat testit (`*ModuleTests.cs`), ja käytä samoja testiapuja
    (`TestLogger.cs`, tilapäiskansiot, `HKCU\Software\PcCleanerTests\<guid>`). Älä aja mitään, mikä muuttaa oikeaa
    järjestelmää (ei oikeaa roskakorin tyhjennystä, ei oikeiden prosessien lopetusta, ei oikeita Run-avaimia).
  - `known.json` (`ProcessRuleLoader`, `AppContext.BaseDirectory/rules/processes/known.json`) → jokaisella säännöllä
    on `DescriptionFi`, ja `DisplayDescription` on englanniksi eri kuin suomeksi.
  - `ByteFormatter` näkyy moduulin tekstissä englanniksi (esim. `DiskUsageModule`-kuvaus sisältää `B`/`KB`, ei `t`/`kt`).
  Tarkistukset vertaavat englanninkielistä tekstiä, jonka luet moduulin koodista (`Loc.T`-kutsun ensimmäinen
  argumentti), ei arvattua.

  Rajoitteet: älä muuta muita testejä tai tuotantokoodia. Jos jokin moduuli tuottaa vielä suomea englanniksi, älä
  korjaa tuotantokoodia: raportoi tiedosto ja rivi. Verify rakentaa vain testiprojektin
  (`--no-restore -p:BuildProjectReferences=false`), koska samanaikaiset tehtävät rakentavat CLI- ja UI-projektia.

## Final verification

- AC-1 [auto]: `dotnet build PcCleaner.sln` → 0 warnings, 0 errors (CON-5)
- AC-2 [auto]: `dotnet test` → kaikki läpi (218 vanhaa + T1:n ja T12:n uudet), 0 epäonnistunutta
- AC-3 [auto]: `dotnet run --project src/Cleaner.Cli --no-build -- modules` → englanninkieliset nimet;
  `dotnet run --project src/Cleaner.Cli --no-build -- --lang fi modules` → suomenkieliset nimet;
  `dotnet run --project src/Cleaner.Cli --no-build -- --lang xx modules` → paluukoodi 2;
  `dotnet run --project src/Cleaner.Cli --no-build -- scan --module diskusage --path tests --top 3` → englanninkielinen
  tuloste, tavuyksiköt `B/KB/MB`
- AC-4 [manual]: jäännössuomen haku. Aja
  `rg -n '"[^"]*[äöÄÖ][^"]*"' src --glob '*.cs' --glob '*.xaml' | rg -v 'Loc\.T\(|LocalizedText\(|^\S+:\s*///|//'`
  ja käy osumat läpi: sallittuja ovat vain `Loc.T`-kutsujen monirivisten suomenkielisten argumenttien rivit,
  CLI:n suomenkielinen ohjeteksti, `manifest.txt`-rivit (`Käsitellyt kohteet:` ym., CON-2) ja `--sort`-aliakset
  (`päivä`). Lisäksi pistokokeet englannin sanastosta (CON-6) ja siitä, ettei suomenkielisiä tekstejä ole
  muotoiltu uudelleen (`git diff main -- src | rg '^-.*[äö]'` -rivit löytyvät `+`-puolelta).
- AC-5 [manual]: käyttäjä käynnistää `PcCleaner.exe` (UAC): oletus englanti; valitsin `English`/`Suomi`
  paneelin alaosassa; vaihto päivittää heti kaikki näkyvät tekstit (napit, otsikot, moduulien nimet, tilarivit,
  järjestysvalikko); vaihto on estetty skannauksen aikana; dialogit vaihdon jälkeen uudella kielellä; uudelleen-
  käynnistyksen jälkeen valinta säilyy; skannaus englanniksi tuottaa englanninkieliset kategoriat ja kuvaukset.
- AC-6 [manual]: GitHubissa README.md näkyy englanniksi, linkit `README.fi.md` ja `LICENSE` toimivat, ja LICENSE
  tunnistetaan MIT-lisenssiksi (repon sivupalkki).

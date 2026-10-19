# Plan: Ohjelman poisto -moduuli (Revo Uninstaller -tyylinen jäänteiden siivous)
Branch: feature/uninstall-module
Created: 2026-10-19

## Background & scope

PC Cleaner (C# / .NET 8, WinUI 3 + CLI, repo `C:\Users\ville\PcCleaner`) saa uuden moduulin `uninstall`:
käyttäjä valitsee asennetun Win32-ohjelman, moduuli ajaa ohjelman oman poisto-ohjelman, odottaa sen
valmistumista ja etsii sitten jäänteet (kansiot, pikakuvakkeet, rekisteriavaimet ja -arvot) tavallista
poistoa syvemmin. Jäänteet siirretään karanteeniin ja rekisteri viedään `.reg`-varmuuskopioon, jotta
yksi palautuskomento tuo kaiken takaisin. Sama moduuli osaa etsiä jäänteet myös jo poistetulle ohjelmalle
("Etsi jäänteet" ilman poisto-ohjelman ajoa).

Käyttäjän päätökset (kysytty 19.10.2026):
- Vain Win32-ohjelmat (Uninstall-rekisteriavaimet HKLM, HKLM\Wow6432Node, HKCU). Store/MSIX-sovellukset eivät kuulu tähän.
- Jäännöskansiot ja -tiedostot siirretään karanteeniin (`%LOCALAPPDATA%\PcCleaner\quarantine\<aikaleima>\`),
  rekisteri `.reg`-varmuuskopioon samalla aikaleimalla. Palautus tuo molemmat takaisin.
- Laaja, Revo-tyylinen haku: nimiosumat kansioissa, julkaisijakansiot, pikakuvakkeet, rekisteriavaimet ja
  rekisteriarvot, jotka viittaavat asennuskansioon.

Ulkopuolella: Store/MSIX, asennuslokiin perustuva seuranta (Revo Pro), ajurit ja palvelut, Windows-komponentit,
muiden käyttäjien profiilit, UI:n treemap-tyyppiset lisänäkymät.

Olemassa oleva koodi, johon tämä nojaa (lue ennen toteutusta): `docs/ARCHITECTURE.md` (kerrokset, `ICleanerModule`-sopimus
luku 3.1, moduulin tunnisteiden konventio), `HANDOFF.md` (suunnittelupäätökset 1–20), `src/Cleaner.Modules.Registry`
(`RegistryBackup`, `RegistryGuard`, `RegistryPaths`, `CommandLineParser`), `src/Cleaner.Modules.DiskUsage/DiskUsageModule.cs`
(`Options`-ominaisuus, jonka UI asettaa ennen skannausta), `src/Cleaner.UI/ViewModels/MainViewModel.cs`
(moduulikohtaiset paneelit `IsDiskUsageSelected`, `IsRegistrySelected`, `RestoreRegistryBackupAsync`),
`src/Cleaner.Cli/Program.cs` ja `RestoreCommand.cs`.

## Requirements

- REQ-1: `InstalledProgramReader.List()` palauttaa Uninstall-juurten (`HKLM\SOFTWARE\...\Uninstall`,
  `HKLM\SOFTWARE\WOW6432Node\...\Uninstall`, `HKCU\Software\...\Uninstall`) aliavaimista `InstalledProgram`-tietueet
  nimen mukaan järjestettynä. Pois jäävät: `SystemComponent` = 1, `DisplayName` puuttuu tai on tyhjä, `ParentKeyName`-arvo on
  (päivitykset). Kaksi avainta samalla nimellä eri juurissa ovat kaksi eri tietuetta (`KeyPath` erottaa).
- REQ-2: `UninstallRunner.RunAsync(program, quiet, ct)` käynnistää ohjelman poisto-ohjelman: `QuietUninstallString`, jos
  `quiet` ja se on olemassa, muuten `UninstallString`. MSI-ohjelmalle (`WindowsInstaller` = 1 tai komennon exe on
  `msiexec.exe`) ajetaan `msiexec.exe /x <ProductCode>` ja `quiet`-tilassa lisäksi `/qb-`, missä `ProductCode` on avaimen nimi
  muodossa `{GUID}` tai komennosta poimittu `{GUID}`. Odottaa prosessin päättymistä, sen jälkeen enintään 60 s, että
  ohjelman Uninstall-avain katoaa (kysely 2 s välein). Tulos: `ExitCode`, `KeyRemoved`, `Elapsed`, `Error` (null kun
  käynnistys onnistui). Peruutus (`ct`) lopettaa odotuksen eikä tapa poisto-ohjelmaa.
- REQ-3: `LeftoverScanner.Scan(program, keyRemoved, options, ct)` tuottaa `CleanItem`-kohteet luvun **Matching rules**
  sääntöjen mukaan viidessä kategoriassa: "Asennuskansio", "Ohjelmakansiot", "Pikakuvakkeet", "Rekisteriavaimet",
  "Rekisteriarvot". Sama polku tai avain esiintyy enintään kerran (ensimmäinen osuva sääntö voittaa).
- REQ-4: Jokaisen jäännöskohteen `Description` kertoo osumasyyn: säännön nimi ja mihin nimeen, julkaisijaan tai polkuun
  osuma perustuu (esim. "Kansion nimi täsmää ohjelman nimeen 'Foo Bar'", "Arvo viittaa asennuskansioon C:\...").
- REQ-5: `CleanItem` saa valinnaisen kentän `bool? Preselected = null`. UI:n oletusvalinta on
  `item.Risk != High && (item.Preselected ?? module.SelectedByDefault)`. Nykyinen käytös säilyy kaikille moduuleille,
  jotka eivät aseta kenttää. Uninstall-moduuli asettaa `Preselected = true` vain **Matching rules** -luvussa
  esivalituiksi merkityille kohteille ja `false` muille.
- REQ-6: `UninstallModule.CleanAsync` oikeassa ajossa: kansio- ja tiedostokohteet siirretään karanteeniin
  `<QuarantineRoot>\<aikaleima>\NNN\<alkuperäinen nimi>` ja `manifest.txt` kirjaa `NNN → alkuperäinen polku`;
  rekisteriavaimet ja -arvot viedään `RegistryBackup`-luokalla samalla aikaleimalla ennen poistoa. Jos vienti tai siirto
  epäonnistuu, kohde päätyy `Skipped`-listalle syyn kanssa eikä sitä kosketa. Kuiva ajo ei siirrä, poista eikä luo
  kansioita, mutta täyttää `Removed`-listan.
- REQ-7: `BackupSessions.List()` listaa aikaleimat, joilla on rekisterivarmuuskopio ja/tai karanteeni, ja
  `BackupSessions.Restore(ts, log)` tuo molemmat takaisin: `.reg`-tiedostot `reg import` -komennolla ja karanteenin
  polut alkuperäisiin paikkoihinsa (puuttuvat yläkansiot luodaan). Jos alkuperäinen polku on jo olemassa, kohde ohitetaan
  ja syy raportoidaan. CLI `pccleaner restore [ts]` ja UI:n "Palauta varmuuskopio..." käyttävät `BackupSessions`-luokkaa.
- REQ-8: Ennen jokaista siirtoa tai poistoa tarkistetaan vartija: tiedostojärjestelmäkohteen on oltava jonkin
  **Matching rules** -luvun peruskansion alla tai ohjelman `InstallLocation`-kansion alla tai se itse, se ei saa olla
  peruskansio itse, eikä `PathGuard.IsSafeToDelete` saa hylätä sitä. Rekisterikohteen on oltava `HKCU\Software`-,
  `HKLM\SOFTWARE`- tai `HKLM\SOFTWARE\WOW6432Node`-juuren alla vähintään yhden segmentin syvyydessä juuresta, sen polussa ei
  saa olla `RegistryGuard.ForbiddenSegments`-segmenttejä, eikä se saa olla tai olla segmentin `Microsoft`, `Classes`,
  `Policies`, `WOW6432Node`, `Clients`, `RegisteredApplications` alla (poikkeus: `Microsoft\Windows\CurrentVersion\Uninstall\<avain>`
  ja `Microsoft\Windows\CurrentVersion\App Paths\<exe>` sallitaan, koska ne ovat ohjelman omia merkintöjä). Väärennetty
  tunniste, joka osoittaa muualle, päätyy `Skipped`-listalle.
- REQ-9: Karanteeniin siirretyn kansion yläkansio poistetaan, jos se on tyhjä siirron jälkeen eikä ole peruskansio
  (julkaisijakansio). Palautus luo puuttuvat yläkansiot uudelleen.
- REQ-10: Ennen poisto-ohjelman ajoa ja ennen oikeaa karanteenisiirtoa tarkistetaan, onko käynnissä prosessi, jonka
  exe on ohjelman `InstallLocation`-kansion tai karanteeniin siirrettävän kansion alla. Jos on, ajo/siirto ei ala ja
  virhe nimeää prosessin. Prosessit, joiden polkua ei voi lukea, ohitetaan.
- REQ-11: CLI-komento `pccleaner uninstall`: ilman valintoja listaa ohjelmat (nimi, versio, julkaisija, koko, avain).
  `--program <teksti>` valitsee ohjelman: tarkka nimiosuma tai yksikäsitteinen osamerkkijono (kirjainkoko ei merkitse);
  usealla osumalla komento listaa ehdokkaat ja palauttaa koodin 2. Valinnan kanssa komento ajaa poisto-ohjelman
  (`--quiet` käyttää hiljaista komentoa), odottaa, skannaa jäänteet ja tulostaa ne kuivana. `--leftovers-only` ohittaa
  poisto-ohjelman. `--apply` siirtää karanteeniin esivalitut kohteet (`Preselected == true`), tai `--item`-rajauksen
  kohteet, tai kaikki kun `--all`; kysyy vahvistuksen `[k/E]` ellei `--yes`. Tuloste kertoo karanteenikansion ja
  palautuskomennon.
- REQ-12: UI: moduulin "Ohjelman poisto" sivulla on hakukenttä, ohjelmalista (nimi, versio, julkaisija, koko),
  napit "Päivitä lista", "Poista ohjelma" ja "Etsi jäänteet". "Poista ohjelma" ajaa poisto-ohjelman, näyttää odotustilan
  ("Odotetaan poisto-ohjelmaa: <nimi>", Peruuta-nappi lopettaa odotuksen) ja skannaa jäänteet automaattisesti sen
  päätyttyä. "Etsi jäänteet" skannaa ilman poistoa. Jäänteet näkyvät olemassa olevassa kategoria- ja valintaruutunäkymässä,
  Puhdista-ketjun tekstit sanovat "siirretään karanteeniin", ja "Palauta varmuuskopio..." listaa `BackupSessions`-sessiot.
- REQ-13: Dokumentit päivitetty: `docs/ARCHITECTURE.md` luku 6.4 (moduulin kuvaus, säännöt, karanteeni), `README.md`
  (käyttö CLI:ssä ja UI:ssa), `HANDOFF.md` (projektitaulukko, päätökset 21–24, tunnisteet, tunnetut rajoitukset).

## Edge cases

- EDGE-1: Poisto-ohjelma päättyy heti ja jättää lapsiprosessin (NSIS, MSI) → `KeyRemoved`-kysely 60 s kattaa tämän;
  jos avain ei katoa määräajassa, `KeyRemoved = false`, jäännösskannaus jatkuu silti ja Uninstall-avain tarjotaan
  rekisterijäänteenä (High, ei esivalittu).
- EDGE-2: `InstallLocation` puuttuu, on tyhjä, osoittaa olemattomaan kansioon tai peruskansioon → asennuskansiosääntö
  ohitetaan, muut säännöt toimivat; rekisteriarvosääntö ohitetaan, koska vertailupolkua ei ole.
- EDGE-3: Ohjelman nimessä on vain yksi merkitsevä sana ja se on alle 5 merkkiä (esim. "Git") → vain täsmälleen samanniminen
  kansio osuu, ei osamerkkijono.
- EDGE-4: Normalisoitu ohjelman nimi on sama kuin jonkin peruskansion tai kieltolistan nimi (esim. "Windows") → ei kansio-osumia.
- EDGE-5: Karanteenisiirto eri asemalle (`Directory.Move` heittää `IOException`) → kopioidaan rekursiivisesti ja poistetaan
  alkuperäinen vasta onnistuneen kopion jälkeen.
- EDGE-6: Palautuksessa alkuperäinen polku on jo olemassa → kohde jätetään karanteeniin, viesti kertoo polun.
- EDGE-7: Peruutus rekisteriarvojen skannauksen aikana → `OperationCanceledException` etenee, osittaista tulosta ei palauteta.
- EDGE-8: Prosessi ei ole korotettu (CLI tavallisena käyttäjänä) → HKLM-kohteet ja Program Files -kansiot päätyvät
  `Skipped`-listalle syyllä "Tarvitsee järjestelmänvalvojan oikeudet" (kansio: `UnauthorizedAccessException` siirrossa).
- EDGE-9: Sama kansio osuu usealla säännöllä (asennuskansio on myös nimiosuma) → yksi kohde, ensimmäinen sääntö
  (asennuskansio) voittaa.
- EDGE-10: Poisto-ohjelmaa ei voi käynnistää (tiedosto puuttuu, `Win32Exception`) → `RunAsync` palauttaa `Error`-tekstin,
  ei poikkeusta; UI ja CLI tarjoavat silti jäännösskannauksen.

## Constraints

- CON-1: Ei NuGet-paketteja Core-, moduuli- eikä CLI-projekteihin (HANDOFF päätös 4). Testiprojekti käyttää xunitia.
- CON-2: Moduuli ei saa viitata toiseen moduuliin (ARCHITECTURE luku 2). Siksi `RegistryBackup`, `RegistryGuard` ja
  `RegistryPaths` siirretään `Cleaner.Core`-projektiin nimiavaruuteen `Cleaner.Core.Services` (T1) ja Registry-moduuli
  käyttää niitä sieltä.
- CON-3: `ICleanerModule`-rajapinta ei muutu. `CleanItem` saa vain valinnaisen `Preselected`-kentän positional-recordin
  loppuun, joten kaikki nykyiset kutsut kääntyvät muuttumatta.
- CON-4: Windows-only-koodi merkitään `[SupportedOSPlatform("windows")]`; `dotnet build PcCleaner.sln -warnaserror` on vihreä
  (tällä hetkellä 0 varoitusta, tämä on projektin lint).
- CON-5: Käyttäjälle näkyvät tekstit suomeksi, koodin nimet englanniksi, C# 12, nullable päällä, `sealed` oletuksena.
- CON-6: Kaikki tiedostojärjestelmän siirrot ja poistot kulkevat Core-palveluiden (`Quarantine`, `SafeFileDeleter`) kautta
  ja tarkistavat `PathGuard.IsSafeToDelete`-ehdon; rekisterin poistot kulkevat `RegistryBackup.TryExport`-kutsun kautta.
- CON-7: Uusi moduuli on `SelectedByDefault = false` ja ajetaan vain pyydettäessä (`--module uninstall`, UI:n sivu).
- CON-8: Testit, jotka koskevat rekisteriä, käyttävät vain `HKCU\Software\PcCleanerTests\<guid>`-avainta ja poistavat sen
  lopuksi (kuten `RegistryModuleTests`); tiedostotestit käyttävät `%TEMP%\pccleaner-tests\<guid>`-kansiota.

## Matching rules

Nämä säännöt ovat vaatimus (REQ-3, REQ-5, REQ-8 viittaavat tähän).

**Nimen normalisointi** (`NameMatcher.Normalize`): poista suluissa oleva sisältö, versiomaiset merkkijonot
(`\d+(\.\d+)+`, `v\d+`, `x64`, `x86`, `64-bit`, `32-bit`, `(64 bit)`), merkit `™ ® ©`; pienet kirjaimet; jaa
sanoiksi muilla kuin kirjain- ja numeromerkeillä. Merkitsevät sanat: pituus ≥ 3 ja ei kieltolistalla
`the, for, and, windows, version, edition, app, application, software, inc, llc, ltd, corp, corporation, gmbh, oy, ab`.

**Kansio-osuma** (`NameMatcher.Match(folderName, program)` → `MatchKind`):
- `Exact`: kansion normalisoidut sanat yhdistettynä == ohjelman normalisoidut sanat yhdistettynä.
- `Tokens`: kaikki ohjelman merkitsevät sanat esiintyvät kansion nimessä kokonaisina sanoina, ja ohjelmalla on ≥ 2
  merkitsevää sanaa tai sen ainoa merkitsevä sana on ≥ 5 merkkiä (EDGE-3).
- `Publisher`: kansion normalisoidut sanat == julkaisijan normalisoidut sanat; tällöin tutkitaan kansion lapset
  `Exact`/`Tokens`-säännöillä. Osuva lapsi on kohde; jos julkaisijakansiolla ei ole muita lapsia, kohde on julkaisijakansio.
- `None` muuten. Kansion nimi, joka on kieltolistalla `Microsoft, Windows, WindowsApps, Common Files, Microsoft Shared,
  Packages, Temp, Programs, Google, Intel, NVIDIA, AMD`, ei osu koskaan (EDGE-4).

**Peruskansiot** (`LeftoverScanOptions.BaseFolders`, oletukset `Environment.GetFolderPath`-kutsuilla): Program Files,
Program Files (x86), ProgramData (`CommonApplicationData`), `LocalApplicationData`, `LocalApplicationData\Programs`,
`ApplicationData` (Roaming), `LocalApplicationData\..\LocalLow`. Pikakuvakkeita varten lisäksi: Käynnistä-valikon
Programs (`Programs`, `CommonPrograms`), työpöytä (`Desktop`, `CommonDesktopDirectory`).

**Kategoriat, riski ja esivalinta:**

| Kategoria | Sääntö | Tunniste | Riski | Preselected |
|---|---|---|---|---|
| Asennuskansio | `InstallLocation` (lainausmerkit ja loppukeno pois), kansio on olemassa, ei peruskansio, ei peruskansion yläkansio, PathGuard sallii | `uninstall:dir:<polku>` | Medium | true |
| Asennuskansio | `DisplayIcon`- tai `UninstallString`-exen kansio, kun se on Program Files-, Program Files (x86)- tai LocalAppData\Programs-kansion alla, ei peruskansio, ja sen nimi osuu `Exact`/`Tokens`/`Publisher`-säännöllä | `uninstall:dir:<polku>` | Medium | true |
| Ohjelmakansiot | Peruskansion välitön lapsi, `Exact`-osuma Program Files-, Program Files (x86)-, ProgramData- tai LocalAppData\Programs-kansiossa | `uninstall:dir:<polku>` | Medium | true |
| Ohjelmakansiot | `Tokens`- tai `Publisher`-osuma samoissa kansioissa | `uninstall:dir:<polku>` | Medium | false |
| Ohjelmakansiot | Mikä tahansa osuma `LocalApplicationData`-, `ApplicationData`- tai `LocalLow`-kansiossa (voi sisältää käyttäjän dataa) | `uninstall:dir:<polku>` | High | false |
| Pikakuvakkeet | `.lnk` Käynnistä-valikon Programs-kansioissa (rekursiivisesti) tai työpöydillä, jonka kohde (`ShortcutResolver`-tyylinen WScript.Shell-luku, kopioi logiikka moduuliin) on asennuskansion tai osuneen ohjelmakansion alla; tai Käynnistä-valikon kansio, jonka nimi osuu `Exact`/`Tokens` | `uninstall:file:<polku>` / `uninstall:dir:<polku>` | Low | true |
| Rekisteriavaimet | `HKCU\Software\<Julkaisija>\<Nimi>`, `HKLM\SOFTWARE\<Julkaisija>\<Nimi>`, `HKLM\SOFTWARE\WOW6432Node\<Julkaisija>\<Nimi>`, `HKCU\Software\<Nimi>`, `HKLM\SOFTWARE\<Nimi>`, `HKLM\SOFTWARE\WOW6432Node\<Nimi>` kun avain on olemassa; aliavain- ja julkaisijanimen vertailu `Exact`-säännöllä juuren lapsiin (ei vain tarkka merkkijono). Julkaisijakansio kohteena, jos sillä ei ole muita lapsia | `uninstall:key:<avain>` | High | false |
| Rekisteriavaimet | Ohjelman Uninstall-avain, kun `keyRemoved == false` ja avain on yhä olemassa | `uninstall:key:<avain>` | High | false |
| Rekisteriavaimet | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\<x>.exe` (+ HKCU), jonka oletusarvo tai `Path` osoittaa asennuskansioon | `uninstall:key:<avain>` | High | false |
| Rekisteriavaimet | `HKCU\Software\Classes\<ProgId>` ja `HKLM\SOFTWARE\Classes\<ProgId>` (ei `.ext`, ei ForbiddenSegments), jonka `shell\open\command` osoittaa asennuskansioon | `uninstall:key:<avain>` | High | false |
| Rekisteriarvot | `HKCU\Software` ja `HKLM\SOFTWARE` (myös WOW6432Node) rekursiivisesti enintään 6 tason syvyydessä, ohittaen segmentit `Classes`, `Microsoft`, `Policies` ja ForbiddenSegments: `String`/`ExpandString`-arvo, jonka sisältö (laajennettuna, lainausmerkit pois) alkaa `InstallLocation`-polulla; lisäksi `...\CurrentVersion\Run`-arvot HKCU ja HKLM samalla ehdolla | `uninstall:value:<avain>\|<arvonimi>` | High | false |

Rekisteriarvojen skannaus kunnioittaa `ct`-peruutusta jokaisella avaimella (EDGE-7) ja ohittaa avaimet, joita ei voi avata.

## Assumptions

- Rekisteripalvelut siirretään Coreen (CON-2) sen sijaan, että Uninstall-moduuli viittaisi Registry-moduuliin.
  Nimiavaruus `Cleaner.Core.Services`, tiedostot kansioon `src/Cleaner.Core/Services/Registry/`. `RegistryBackup.DefaultRoot`
  ja `Quarantine.DefaultRoot` ovat molemmat `%LOCALAPPDATA%\PcCleaner\...`.
- `InstalledProgramReader` sijoitetaan Uninstall-moduuliin (ainoa käyttäjä). Registry-moduulin oma Uninstall-orpologiikka
  säilyy ennallaan; yhdistäminen on myöhempi refaktorointi.
- `Preselected` toteutetaan `CleanItem`-recordin valinnaisena kenttänä (REQ-5), ei uutena rajapintana, koska se on
  pienin muutos, joka säilyttää nykyisten moduulien käytöksen.
- Karanteeni ja rekisterivarmuuskopio jakavat aikaleiman (`yyyyMMdd-HHmmss`), jotta yksi `restore <ts>` kattaa molemmat.
  Aikaleima luodaan kerran `CleanAsync`-kutsun alussa ja annetaan molemmille palveluille.
- MSI-poistossa ei käytetä `/qn`-tilaa koskaan, vain `/qb-` (`quiet`), jotta käyttäjä näkee edistymisen.
- Poisto-ohjelma käynnistetään `UseShellExecute = true` -tilassa ilman `runas`-verbiä: UI on jo korotettu
  (HANDOFF päätös 7), CLI tavallisena käyttäjänä saa UAC-kehotteen poisto-ohjelmalta itseltään.
- Julkaisijakansion tyhjyys tarkistetaan vasta siirron jälkeen (REQ-9) `SafeFileDeleter.TryDeleteEmptyDirectory`-kutsulla;
  palautus luo kansion `Directory.CreateDirectory`-kutsulla ennen `Directory.Move`-kutsua.
- UI:n ohjelmalista näyttää enintään kaikki ohjelmat (tyypillisesti 50–300), ei virtualisointiongelmaa.
- Käynnissä olevan prosessin tarkistus (REQ-10) käyttää `Process.GetProcesses()` + `MainModule.FileName` try/catch-suojattuna;
  32-bittisten ja suojattujen prosessien polut jäävät lukematta ja ne ohitetaan. Tämä on hyväksytty rajoitus.
- Moduulin `RequiresAdmin = true` (HKLM-avaimet ja Program Files). `Risk = High`.
- `dotnet build PcCleaner.sln -warnaserror` toimii lint-tarkistuksena, koska projektissa ei ole erillistä linteriä.

## Disposition

- EDGE-4: toteutuu `NameMatcher`-kieltolistalla (T3) ja testataan siellä; ei erillistä työtä muissa tehtävissä.
- EDGE-9: toteutuu `LeftoverScanner`-luokan "ensimmäinen sääntö voittaa" -joukolla (T4).
- CON-1, CON-5, CON-7: läpileikkaavia; tarkistetaan diffeistä, ei omaa tehtävää. CON-7 toteutuu T6:n `SelectedByDefault => false`.

## Wave 1

Yksi tehtävä: T1 koskee jaettuja sopimuksia (Core, Registry-moduuli, CLI, UI, testit) ja kaiken muun on kääntyvä sitä vasten.

### T1: Jaetut sopimukset: rekisteripalvelut Coreen ja CleanItem.Preselected
- Status: pending
- Model: opus
- Requirements: REQ-5
- Constraints/edge: CON-2, CON-3, CON-4
- Files: src/Cleaner.Core/Services/Registry/RegistryPaths.cs, src/Cleaner.Core/Services/Registry/RegistryGuard.cs, src/Cleaner.Core/Services/Registry/RegistryBackup.cs, src/Cleaner.Modules.Registry/RegistryPaths.cs, src/Cleaner.Modules.Registry/RegistryGuard.cs, src/Cleaner.Modules.Registry/RegistryBackup.cs, src/Cleaner.Modules.Registry/RegistryModule.cs, src/Cleaner.Modules.Registry/RegistryOptions.cs, src/Cleaner.Cli/RestoreCommand.cs, src/Cleaner.UI/ViewModels/MainViewModel.cs, src/Cleaner.UI/ViewModels/ModuleViewModel.cs, src/Cleaner.Core/Models/CleanItem.cs, tests/Cleaner.Tests/RegistryModuleTests.cs, docs/ARCHITECTURE.md
- Verify: `dotnet build PcCleaner.sln -warnaserror` → exit 0, ja `dotnet test PcCleaner.sln --no-build` → "Passed!" 67 testiä (sama määrä kuin ennen)
- Task:
  Kaksi sopimusmuutosta, joihin kaikki myöhemmät tehtävät nojaavat.

  **A. Siirrä rekisteripalvelut Coreen.** Tiedostot `src/Cleaner.Modules.Registry/RegistryPaths.cs`, `RegistryGuard.cs` ja
  `RegistryBackup.cs` siirretään sellaisenaan kansioon `src/Cleaner.Core/Services/Registry/` ja niiden nimiavaruus vaihtuu
  `Cleaner.Modules.RegistryCleaner` → `Cleaner.Core.Services`. Julkinen API (luokkien ja jäsenten nimet, signatuurit,
  `[SupportedOSPlatform("windows")]`-merkinnät) ei muutu. Vanhat tiedostot poistetaan Registry-moduulista. Päivitä `using`-rivit
  ja viittaukset: `src/Cleaner.Modules.Registry/RegistryModule.cs`, `RegistryOptions.cs` (käyttää `RegistryBackup.DefaultRoot`),
  `src/Cleaner.Cli/RestoreCommand.cs`, `src/Cleaner.UI/ViewModels/MainViewModel.cs` (käyttää `RegistryBackup.List/Restore/DefaultRoot`
  ja `RegistryBackup.RestoreResult`), `tests/Cleaner.Tests/RegistryModuleTests.cs` (`RegistryGuardTests`, `RegistryPaths.KeyExists`,
  `RegistryBackup.List/Restore`). Core-projektilla ei ole NuGet-riippuvuuksia; `Microsoft.Win32.Registry` ja `System.Diagnostics.Process`
  ovat .NET 8:n mukana. Core ei saa viitata moduuleihin (ARCHITECTURE luku 2), joten mitään ei saa jäädä Registry-moduulista
  Coreen päin viitatuksi. Päivitä `docs/ARCHITECTURE.md` luvun 6.3 maininta "Projekti `src/Cleaner.Modules.Registry`" lisäämällä
  lause, että `RegistryBackup`, `RegistryGuard` ja `RegistryPaths` ovat Core-palveluita (`Cleaner.Core.Services`), jotta muutkin
  moduulit voivat käyttää niitä.

  **B. `CleanItem.Preselected`.** `src/Cleaner.Core/Models/CleanItem.cs` on positional record
  `CleanItem(string Id, string ModuleId, string Category, string Path, long SizeBytes, string Description, RiskLevel Risk)`.
  Lisää loppuun valinnainen kenttä `bool? Preselected = null` ja dokumentoi: null = moduulin oletus, true/false = kohteen oma
  esivalinta. Nykyiset kutsut (positional, 7 argumenttia) eivät muutu. Muuta `src/Cleaner.UI/ViewModels/ModuleViewModel.cs`
  metodin `LoadScan` oletusvalinta muotoon `item.Risk != RiskLevel.High && (item.Preselected ?? SelectedByDefault)` niin, että
  valinta lasketaan kohteittain (nyt `selectByDefault` lasketaan kategorian riskistä; vaihda kohteen omaan riskiin ja kenttään).
  Kategorian riski (`group.Max`) säilyy High-lohkon jaottelussa.

  Rajoitteet: ei muita toiminnallisia muutoksia; kaikki 67 nykyistä testiä pysyvät vihreinä; `-warnaserror` vihreä
  (CA1416 hoidetaan luokkatason `[SupportedOSPlatform("windows")]`-merkinnöillä, jotka siirtyvät tiedostojen mukana).

## Wave 2

Kaksi riippumatonta tehtävää: T2 kirjoittaa vain Core-projektin uusia tiedostoja ja omat testitiedostonsa, T3 luo uuden
moduuliprojektin tiedostoineen, sln-rivit ja testiprojektin viittauksen. Tiedostojoukot eivät leikkaa. Kummankin Verify
kääntää vain oman projektinsa (`--no-dependencies` käyttää Wave 1:n portissa käännettyä Core.dll:ää), koska koko solutionin
käännös lukisi sisaren keskeneräisiä tiedostoja. Testit ajetaan aallon lopussa foremanin toimesta: `dotnet test PcCleaner.sln`.

### T2: Core: Quarantine ja BackupSessions
- Status: pending
- Model: opus
- Requirements: REQ-7
- Constraints/edge: CON-6, EDGE-5, EDGE-6
- Files: src/Cleaner.Core/Services/Quarantine.cs, src/Cleaner.Core/Services/BackupSessions.cs, tests/Cleaner.Tests/QuarantineTests.cs, tests/Cleaner.Tests/BackupSessionsTests.cs
- Verify: `dotnet build src/Cleaner.Core/Cleaner.Core.csproj -warnaserror` → exit 0. Testit (`QuarantineTests`, `BackupSessionsTests`) ajaa foreman aallon lopussa komennolla `dotnet test PcCleaner.sln` → kaikki vihreitä.
- Task:
  Kaksi uutta Core-palvelua nimiavaruuteen `Cleaner.Core.Services`. Lue mallia `src/Cleaner.Core/Services/Registry/RegistryBackup.cs`
  (sama tyyli: aikaleimakansio, `manifest.txt`, `List`, `Restore`, `RestoreResult`) ja `SafeFileDeleter.cs` (`PathGuard`-tarkistus
  ennen jokaista operaatiota, `ICleanerLogger`-lokitus).

  **`Quarantine`** (sealed class, `[SupportedOSPlatform("windows")]` ei tarvita, puhdas tiedostojärjestelmä):
  ```csharp
  public sealed class Quarantine
  {
      public Quarantine(string root, string timestamp, ICleanerLogger log);   // kansio luodaan laiskasti ensimmäisessä siirrossa
      public static string DefaultRoot { get; }                                 // %LOCALAPPDATA%\PcCleaner\quarantine
      public string Timestamp { get; }
      public string Directory { get; }                                          // root\timestamp
      public bool HasEntries { get; }
      public bool TryMoveDirectory(string path, out string quarantinedPath, out string error);
      public bool TryMoveFile(string path, out string quarantinedPath, out string error);
      public void WriteManifest(IEnumerable<string> processedItems);
      public sealed record Entry(string Index, string OriginalPath, string QuarantinedPath, bool IsDirectory);
      public sealed record SessionInfo(string Timestamp, string Directory, int EntryCount);
      public sealed record RestoreResult(int Restored, int Skipped, IReadOnlyList<string> Messages);
      public static IReadOnlyList<SessionInfo> List(string? root = null);
      public static IReadOnlyList<Entry> ReadManifest(string sessionDirectory);
      public static RestoreResult Restore(string timestamp, ICleanerLogger log, string? root = null);
  }
  ```
  Siirto: kohde `root\<timestamp>\<NNN>\<alkuperäinen nimi>` (NNN juokseva 001…), `PathGuard.IsSafeToDelete(path)` tarkistetaan
  ensin ja hylkäys palauttaa false syyllä. `Directory.Move`; jos se heittää `IOException` (eri asema), kopioi rekursiivisesti
  (`Directory.CreateDirectory` + `File.Copy` säilyttäen aikaleimat ei vaadita) ja poista alkuperäinen vasta onnistuneen kopion
  jälkeen (EDGE-5). `manifest.txt` muoto: otsikkorivit (aikaleima, palautuskomento), sitten rivit `NNN<TAB>D|F<TAB>alkuperäinen polku<TAB>karanteenipolku`,
  sitten "Käsitellyt kohteet:" ja annetut rivit. `ReadManifest` lukee vain `NNN`-rivit. `Restore`: jokaiselle merkinnälle luo
  alkuperäisen polun yläkansio, jos puuttuu; jos alkuperäinen polku on jo olemassa → `Skipped` ja viesti (EDGE-6); muuten
  siirrä takaisin (sama eri asema -käsittely); poista karanteenin NNN-kansio, kun se on tyhjä. `List` palauttaa sessiot uusin ensin,
  vain ne joissa manifest on.

  **`BackupSessions`** (static, `[SupportedOSPlatform("windows")]`, koska käyttää `RegistryBackup`-luokkaa):
  ```csharp
  public static class BackupSessions
  {
      public sealed record Session(string Timestamp, int RegistryFiles, int QuarantineEntries, string Summary);
      public sealed record RestoreResult(int RegistryImported, int RegistryFailed, int QuarantineRestored, int QuarantineSkipped, IReadOnlyList<string> Messages);
      public static IReadOnlyList<Session> List(string? registryRoot = null, string? quarantineRoot = null);  // unioni aikaleimoista, uusin ensin
      public static RestoreResult Restore(string timestamp, ICleanerLogger log, string? registryRoot = null, string? quarantineRoot = null);
  }
  ```
  `Summary` esim. "3 .reg-tiedostoa, 2 karanteenikohdetta". `Restore` kutsuu `RegistryBackup.Restore`, kun sessiolla on
  rekisterikansio, ja `Quarantine.Restore`, kun sillä on karanteenikansio; puuttuva toinen puoli ei ole virhe.

  Testit (`QuarantineTests`): siirto ja palautus kansiolle, jossa alikansioita ja tiedostoja; tiedoston siirto; manifest-luku;
  palautus kun alkuperäinen on jo olemassa → Skipped; `PathGuard`-hylkäys (esim. käyttäjäprofiilin juuri) → false eikä mitään
  siirretty; yläkansion uudelleenluonti palautuksessa. `BackupSessionsTests`: väliaikaiset juuret, rekisteriosuus testiavaimella
  `HKCU\Software\PcCleanerTests\<guid>` (`RegistryBackup.TryExport`), yhdistetty listaus ja palautus; Windows-only-osa guardataan
  `if (!OperatingSystem.IsWindows()) return;` ja luokka merkitään `[SupportedOSPlatform("windows")]` (katso `RegistryModuleTests`).

### T3: Uninstall-projekti: InstalledProgramReader ja NameMatcher
- Status: pending
- Model: opus
- Requirements: REQ-1
- Constraints/edge: CON-1, CON-4, EDGE-3, EDGE-4
- Files: src/Cleaner.Modules.Uninstall/Cleaner.Modules.Uninstall.csproj, src/Cleaner.Modules.Uninstall/InstalledProgram.cs, src/Cleaner.Modules.Uninstall/InstalledProgramReader.cs, src/Cleaner.Modules.Uninstall/NameMatcher.cs, PcCleaner.sln, tests/Cleaner.Tests/Cleaner.Tests.csproj, tests/Cleaner.Tests/InstalledProgramReaderTests.cs, tests/Cleaner.Tests/NameMatcherTests.cs
- Verify: `dotnet build src/Cleaner.Modules.Uninstall/Cleaner.Modules.Uninstall.csproj --no-dependencies -warnaserror` → exit 0 (Core.dll on Wave 1:n portista). Testit ajaa foreman aallon lopussa: `dotnet test PcCleaner.sln`.
- Task:
  Luo uusi projekti `src/Cleaner.Modules.Uninstall` (nimiavaruus `Cleaner.Modules.Uninstall`) kopioimalla
  `src/Cleaner.Modules.DiskUsage/Cleaner.Modules.DiskUsage.csproj`-tiedoston asetukset (net8.0, nullable, ImplicitUsings,
  LangVersion latest, ProjectReference Core). Lisää projekti `PcCleaner.sln`-tiedostoon GUID:lla
  `{A1B2C3D4-0009-4000-8000-000000000009}` samalla tavalla kuin `Cleaner.Modules.Registry` (`{...0008}`): Project-rivi ja neljä
  Debug/Release|Any CPU -riviä. Lisää `ProjectReference` testiprojektiin `tests/Cleaner.Tests/Cleaner.Tests.csproj`.

  **`InstalledProgram`** (sealed record):
  ```csharp
  public sealed record InstalledProgram(
      string KeyPath,            // esim. HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Foo
      string KeyName,            // avaimen oma nimi (MSI: {GUID})
      string Name,               // DisplayName
      string? Version,           // DisplayVersion
      string? Publisher,
      string? InstallLocation,   // trimmattu: lainausmerkit ja loppukeno pois, tyhjä → null
      string? UninstallString,
      string? QuietUninstallString,
      string? DisplayIcon,
      long EstimatedSizeBytes,   // EstimatedSize (KB) * 1024, 0 jos puuttuu
      bool IsMsi);               // WindowsInstaller == 1 tai UninstallString-exe on msiexec.exe
  ```
  **`InstalledProgramReader`** (`[SupportedOSPlatform("windows")]`): `public static IReadOnlyList<InstalledProgram> List(IEnumerable<string>? uninstallRoots = null)`;
  oletusjuuret `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`, `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall`,
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall` (vakiot julkisina, nimet `UninstallHklm`, `UninstallHklm32`, `UninstallHkcu`).
  Avaimet avataan `Cleaner.Core.Services.RegistryPaths.OpenReadOnly(path)` -metodilla (Core, siirretty T1:ssä; API:
  `TryParse`, `Combine(root, child)`, `OpenReadOnly`, `KeyExists`). Suodatus REQ-1:n mukaan (`SystemComponent` DWORD 1 pois,
  tyhjä `DisplayName` pois, `ParentKeyName`-arvollinen pois). Järjestys `Name` kirjainkoosta riippumatta. Exe-poiminta
  `UninstallString`-arvosta: kopioi `src/Cleaner.Modules.Registry/CommandLineParser.ExtractExecutable`-logiikka moduuliin
  (`UninstallCommandLine.ExtractExecutable`), älä viittaa Registry-moduuliin (CON-2).

  **`NameMatcher`** (static, ei Windows-riippuvuutta) toteuttaa suunnitelman luvun "Matching rules" normalisoinnin ja osuman:
  ```csharp
  public enum MatchKind { None, Exact, Tokens, Publisher }
  public static class NameMatcher
  {
      public static IReadOnlyList<string> Tokens(string name);                 // normalisoidut sanat
      public static IReadOnlyList<string> SignificantTokens(string name);      // pituus ≥ 3, ei kieltolistalla
      public static string Normalize(string name);                             // sanat yhdistettynä ilman välejä
      public static MatchKind Match(string folderName, string programName, string? publisher);
      public static bool IsDeniedFolderName(string folderName);                // kieltolista
      public static readonly string[] StopWords;                               // the, for, and, windows, ...
      public static readonly string[] DeniedFolderNames;                       // Microsoft, Windows, WindowsApps, Common Files, ...
  }
  ```
  `Match` palauttaa `Publisher`, kun kansion nimi normalisoituna == julkaisija normalisoituna (ja julkaisija ei ole tyhjä);
  `Exact`, kun == ohjelman nimi; `Tokens` säännön mukaan (kaikki merkitsevät sanat kokonaisina sanoina kansion sanoissa; vaatii
  ≥ 2 merkitsevää sanaa tai yhden ≥ 5 merkin sanan, EDGE-3); kielletty kansionimi → `None` aina (EDGE-4). Versiomaisten
  merkkijonojen poisto: `\d+(\.\d+)+`, `^v\d+$`, `x64`, `x86`, `64-bit`, `32-bit` sanoina, sulkeiden sisältö kokonaan.

  Testit: `NameMatcherTests` (Theory): "Notepad++ (64-bit x64)" → tokens [notepad]; "Git" vs "GitHub Desktop" → None;
  "Git" vs "Git" → Exact; "7-Zip 23.01 (x64 edition)" vs "7-Zip" → Exact; "Mozilla Firefox" vs "Firefox" → None (Tokens vaatii
  kaikki merkitsevät sanat: mozilla puuttuu); "Firefox" vs "Mozilla Firefox" → Tokens; julkaisija "Mozilla" vs kansio "Mozilla" →
  Publisher; "Windows" → None; `IsDeniedFolderName("Common Files")` true. `InstalledProgramReaderTests`: testiavain
  `HKCU\Software\PcCleanerTests\<guid>\Uninstall` sisältää Normaali (kaikki kentät, EstimatedSize 2048 → 2 097 152), Msi
  (`WindowsInstaller` 1, avain `{GUID}`), Sys (`SystemComponent` 1), NoName (ei DisplayName), Update (`ParentKeyName`) → listassa
  vain Normaali ja Msi, järjestyksessä; `InstallLocation = "\"C:\\Foo\\\""` → `C:\Foo`. Luokka `[SupportedOSPlatform("windows")]`
  ja guard `if (!OperatingSystem.IsWindows()) return;` (malli `RegistryModuleTests`), avain poistetaan `Dispose`-metodissa (CON-8).

## Wave 3

Yksi tehtävä: T4 kirjoittaa Uninstall-projektiin, jonka T5 ja T6 myös muokkaavat; sama projekti ei käänny rinnakkaisten
keskeneräisten tiedostojen kanssa, joten aallot ovat peräkkäisiä.

### T4: LeftoverScanner
- Status: pending
- Model: fable
- Requirements: REQ-3, REQ-4
- Constraints/edge: CON-4, CON-8, EDGE-2, EDGE-7, EDGE-9
- Files: src/Cleaner.Modules.Uninstall/LeftoverScanOptions.cs, src/Cleaner.Modules.Uninstall/LeftoverScanner.cs, src/Cleaner.Modules.Uninstall/LeftoverItemId.cs, src/Cleaner.Modules.Uninstall/ShortcutTarget.cs, tests/Cleaner.Tests/LeftoverScannerTests.cs
- Verify: `dotnet build src/Cleaner.Modules.Uninstall/Cleaner.Modules.Uninstall.csproj --no-dependencies -warnaserror` → exit 0, ja `dotnet test PcCleaner.sln --filter "FullyQualifiedName~LeftoverScannerTests"` → kaikki vihreitä
- Task:
  Toteuta jäänteiden haku suunnitelman luvun "Matching rules" taulukon mukaan (lue se kokonaan; se on vaatimus).
  Projekti `src/Cleaner.Modules.Uninstall` on olemassa (T3) ja sisältää:
  ```csharp
  public sealed record InstalledProgram(string KeyPath, string KeyName, string Name, string? Version, string? Publisher,
      string? InstallLocation, string? UninstallString, string? QuietUninstallString, string? DisplayIcon,
      long EstimatedSizeBytes, bool IsMsi);
  public enum MatchKind { None, Exact, Tokens, Publisher }
  public static class NameMatcher { Tokens, SignificantTokens, Normalize, Match(folderName, programName, publisher), IsDeniedFolderName, StopWords, DeniedFolderNames }
  public static class UninstallCommandLine { public static string? ExtractExecutable(string? command); }
  ```
  Core tarjoaa (`Cleaner.Core.Services`): `RegistryPaths` (`TryParse`, `Combine`, `OpenReadOnly`, `KeyExists`, `Label`),
  `RegistryGuard.ForbiddenSegments`, `PathGuard.IsSafeToDelete(path, out reason)`. `CleanItem` on
  `CleanItem(Id, ModuleId, Category, Path, SizeBytes, Description, Risk, bool? Preselected = null)`.

  **`LeftoverScanOptions`** (sealed record): `BaseFolders` (nimetty tietue `BaseFolder(string Path, BaseFolderKind Kind)`,
  `enum BaseFolderKind { ProgramFiles, ProgramData, LocalPrograms, UserData }`; oletukset luvun "Matching rules" peruskansioista),
  `ShortcutFolders` (Käynnistä-valikon Programs user+common, työpöydät user+public), `RegistryRoots`
  (`HKCU\Software`, `HKLM\SOFTWARE`, `HKLM\SOFTWARE\WOW6432Node`), `AppPathsRoots`, `RunKeys`, `ClassesRoots`, `MaxRegistryDepth = 6`.
  Testit antavat omat kansiot ja testiavaimet.

  **`LeftoverItemId`** (static): `MakeDir(path)` → `uninstall:dir:<polku>`, `MakeFile(path)` → `uninstall:file:<polku>`,
  `MakeKey(keyPath)` → `uninstall:key:<avain>`, `MakeValue(keyPath, valueName)` → `uninstall:value:<avain>|<arvonimi>`,
  `TryParse(id, out LeftoverKind kind, out string path, out string? valueName)`, `enum LeftoverKind { Dir, File, Key, Value }`.
  Erotin `|` kuten `RegistryModule.MakeItemId`-metodissa.

  **`ShortcutTarget`**: `public static string? Resolve(string lnkPath)` WScript.Shell-COM:lla `dynamic`-tyypillä, virhe → null
  (kopioi `src/Cleaner.Modules.Startup/ShortcutResolver.cs`-logiikka, älä viittaa Startup-moduuliin).

  **`LeftoverScanner`** (`[SupportedOSPlatform("windows")]`):
  ```csharp
  public sealed class LeftoverScanner
  {
      public LeftoverScanner(LeftoverScanOptions options);
      public const string InstallFolderCategory = "Asennuskansio";
      public const string ProgramFoldersCategory = "Ohjelmakansiot";
      public const string ShortcutsCategory = "Pikakuvakkeet";
      public const string RegistryKeysCategory = "Rekisteriavaimet";
      public const string RegistryValuesCategory = "Rekisteriarvot";
      public IReadOnlyList<CleanItem> Scan(InstalledProgram program, bool keyRemoved, string moduleId,
          IProgress<(int Percent, string Message)>? progress, CancellationToken ct);
  }
  ```
  Kohteen `SizeBytes`: kansioille rekursiivinen koko (`EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = ReparsePoint }`,
  virheet ohitetaan), tiedostoille pituus, rekisterille 0. `Description` REQ-4:n mukaan. Päällekkäisyys: pidä `HashSet`
  polkuja/avaimia (OrdinalIgnoreCase) ja lisää kohde vain, jos sitä ei ole (EDGE-9); myös alikansio jo lisätyn kansion alla
  ohitetaan. `InstallLocation`-sääntö: EDGE-2:n ehdot; peruskansion yläkansion tunnistus: jokin peruskansio alkaa
  `path + "\"`. Rekisteriarvojen skannaus: rekursio `MaxRegistryDepth`, `ct.ThrowIfCancellationRequested()` joka avaimella
  (EDGE-7), avaamisvirheet ohitetaan, `RegistryGuard.ForbiddenSegments` ja segmentit `Classes`, `Microsoft`, `Policies`
  ohitetaan rekursiossa (mutta `RunKeys` ja `AppPathsRoots` tutkitaan erikseen). Arvon vertailu: `Environment.ExpandEnvironmentVariables`,
  lainausmerkit pois, `StartsWith(installLocation, OrdinalIgnoreCase)` ja seuraava merkki on `\` tai merkkijonon loppu.
  `progress` raportoi vaiheittain (kansiot, pikakuvakkeet, avaimet, arvot).

  Testit (`LeftoverScannerTests`, `[SupportedOSPlatform("windows")]`, temp-kansiot + testiavain, CON-8): rakenna peruskansiot
  temp-kansioon (`ProgramFiles`, `ProgramData`, `LocalAppData`, `LocalAppData\Programs`, `Roaming`, Start Menu) ja ohjelma
  "Foo Bar" julkaisija "Acme": asennuskansio `ProgramFiles\Foo Bar` (Exact, Medium, Preselected true); `ProgramData\Acme\Foo Bar`
  (Publisher → lapsi); `Roaming\Foo Bar` (High, false); `ProgramFiles\Foo Bar Tools` (Tokens, Medium, false); `ProgramFiles\Foobar`
  (ei osu: tokens [foobar] ≠ [foo, bar]); pikakuvake `.lnk` Start Menussa, jonka kohde asennuskansiossa (luo oikea .lnk WScript.Shell:llä);
  testiavaimet `Software\Acme\Foo Bar` ja `Software\Foo Bar` → Rekisteriavaimet; arvo `Software\Other\Thing` `Path = <asennuskansio>\x.exe`
  → Rekisteriarvot; `InstallLocation` olematon → ei asennuskansiota; peruutettu `ct` → `OperationCanceledException`;
  sama kansio kahdella säännöllä → yksi kohde; jokaisella kohteella ei-tyhjä `Description`.

## Wave 4

Yksi tehtävä: T5 muokkaa samaa projektia kuin T4 ja T6.

### T5: UninstallRunner ja RunningProcessCheck
- Status: pending
- Model: opus
- Requirements: REQ-2, REQ-10
- Constraints/edge: CON-4, EDGE-1, EDGE-10
- Files: src/Cleaner.Modules.Uninstall/UninstallRunner.cs, src/Cleaner.Modules.Uninstall/RunningProcessCheck.cs, tests/Cleaner.Tests/UninstallRunnerTests.cs
- Verify: `dotnet build src/Cleaner.Modules.Uninstall/Cleaner.Modules.Uninstall.csproj --no-dependencies -warnaserror` → exit 0, ja `dotnet test PcCleaner.sln --filter "FullyQualifiedName~UninstallRunnerTests"` → kaikki vihreitä
- Task:
  **`UninstallRunner`** (`[SupportedOSPlatform("windows")]`):
  ```csharp
  public sealed record UninstallRunResult(int? ExitCode, bool KeyRemoved, TimeSpan Elapsed, string? Error);
  public sealed class UninstallRunner
  {
      public UninstallRunner(ICleanerLogger log, TimeSpan? keyRemovalTimeout = null, TimeSpan? pollInterval = null); // oletus 60 s / 2 s
      public static (string FileName, string Arguments)? BuildCommand(InstalledProgram program, bool quiet);          // null jos ei komentoa
      public Task<UninstallRunResult> RunAsync(InstalledProgram program, bool quiet, CancellationToken ct);
  }
  ```
  `BuildCommand`: MSI (`program.IsMsi`): `ProductCode` = `KeyName`, jos se on muotoa `{GUID}`, muuten komennosta regexillä
  `\{[0-9A-Fa-f-]{36}\}`; tulos `("msiexec.exe", "/x {GUID}" + (quiet ? " /qb-" : ""))`. Muut: `quiet && QuietUninstallString` →
  se, muuten `UninstallString`; jaa exe ja argumentit `UninstallCommandLine.ExtractExecutable`-metodilla (T3; exe-osan jälkeinen
  loppu on argumentit, lainausmerkit säilytetään). `RunAsync`: `ProcessStartInfo { UseShellExecute = true }` (ei `runas`-verbiä,
  katso Assumptions), `Win32Exception`/`FileNotFoundException` → `Error`, ei poikkeusta (EDGE-10). Odota `WaitForExitAsync(ct)`;
  sitten kysely `RegistryPaths.KeyExists(program.KeyPath)` `pollInterval`-välein `keyRemovalTimeout`-ajan tai kunnes avain katoaa
  (EDGE-1). Peruutus: `OperationCanceledException` etenee kutsujalle; prosessia ei tapeta. `Elapsed` kattaa koko ajan.
  Lokita käynnistetty komento, poistumiskoodi ja `KeyRemoved`.

  **`RunningProcessCheck`** (static, `[SupportedOSPlatform("windows")]`): `public static string? FindRunningUnder(string directory)`
  → ensimmäisen prosessin nimi (`ProcessName`), jonka `MainModule.FileName` alkaa `directory + "\"` (OrdinalIgnoreCase), tai null;
  `Process.GetProcesses()`, jokainen `MainModule`-luku try/catch (`Win32Exception`, `InvalidOperationException`) → ohita, ja `Dispose`
  jokaiselle (malli `src/Cleaner.Core/Services/ProcessChecker.cs`).

  Testit (`UninstallRunnerTests`, CON-8): `BuildCommand`-teoria (MSI avaimesta, MSI komennosta, quiet-string, tavallinen quoted exe + argumentit,
  ei komentoa → null). `RunAsync` oikealla prosessilla: ohjelma, jonka `UninstallString` on `cmd.exe /c reg delete "HKCU\Software\PcCleanerTests\<guid>\Uninstall\Foo" /f`
  (luo avain ensin) → `KeyRemoved == true`, `ExitCode == 0`, `Error == null`; `UninstallString` = `cmd.exe /c exit 3` → `ExitCode 3`,
  `KeyRemoved false` kun timeout on 1 s ja poll 200 ms; olematon exe → `Error` ei null. `RunningProcessCheck.FindRunningUnder(Environment.SystemDirectory)`
  ei heitä (tulos voi olla null tai prosessin nimi); `FindRunningUnder(temp-kansio)` → null.

## Wave 5

Yksi tehtävä: T6 kokoaa moduulin samaan projektiin.

### T6: UninstallModule (ICleanerModule-liima, karanteeni, rekisterivarmuuskopio)
- Status: pending
- Model: opus
- Requirements: REQ-6, REQ-8, REQ-9
- Constraints/edge: CON-3, CON-6, CON-7, CON-8, EDGE-8
- Files: src/Cleaner.Modules.Uninstall/UninstallModule.cs, src/Cleaner.Modules.Uninstall/LeftoverGuard.cs, tests/Cleaner.Tests/UninstallModuleTests.cs
- Verify: `dotnet build src/Cleaner.Modules.Uninstall/Cleaner.Modules.Uninstall.csproj --no-dependencies -warnaserror` → exit 0, ja `dotnet test PcCleaner.sln --filter "FullyQualifiedName~UninstallModuleTests"` → kaikki vihreitä
- Task:
  Toteuta `UninstallModule : ICleanerModule` (`Cleaner.Core.Abstractions`; sopimus `docs/ARCHITECTURE.md` luku 3.1:
  `ScanAsync` ei muuta mitään, `CleanAsync` käsittelee vain `item.ModuleId == Id`, kuiva ajo täyttää `Removed`-listan muuttamatta
  mitään, virheet `Skipped`-listalle, levy-I/O `Task.Run`-kutsussa). Malli: `src/Cleaner.Modules.Registry/RegistryModule.cs`
  (Clean-silmukka, varmuuskopio ennen poistoa, `LastBackupDirectory`) ja `src/Cleaner.Modules.DiskUsage/DiskUsageModule.cs`
  (`Options`-ominaisuus, jonka UI asettaa ennen skannausta).
  ```csharp
  [SupportedOSPlatform("windows")]
  public sealed class UninstallModule : ICleanerModule
  {
      public string Id => "uninstall"; public string DisplayName => "Ohjelman poisto";
      public string Description => "Ajaa ohjelman oman poisto-ohjelman ja siirtää jäänteet karanteeniin.";
      public RiskLevel Risk => RiskLevel.High; public bool RequiresAdmin => true; public bool SelectedByDefault => false;
      public UninstallModule(); public UninstallModule(LeftoverScanOptions scanOptions, string? quarantineRoot, string? registryBackupRoot);
      public InstalledProgram? Target { get; set; }                 // ScanAsync skannaa tämän jäänteet; null → ScanResult.Errors
      public bool TargetKeyRemoved { get; set; } = true;            // UninstallRunner-tulos; false → Uninstall-avain tarjotaan jäänteenä
      public IReadOnlyList<InstalledProgram> ListPrograms();        // InstalledProgramReader.List(scanOptions.UninstallRoots?) – lisää UninstallRoots LeftoverScanOptions-tietueeseen, jos sitä ei ole
      public Task<UninstallRunResult> RunUninstallerAsync(InstalledProgram program, bool quiet, CleanerContext context, CancellationToken ct); // REQ-10-tarkistus ensin
      public string? LastQuarantineDirectory { get; }
      public string? LastBackupTimestamp { get; }
  }
  ```
  Käytettävissä: T2 `Quarantine` (`TryMoveDirectory`, `TryMoveFile`, `WriteManifest`, `HasEntries`, `Directory`, `Timestamp`),
  Core `RegistryBackup(root, log)` (huom: sen aikaleima luodaan konstruktorissa `DateTime.Now`-arvosta; lisää Coreen
  **ei** muutoksia — anna karanteenille `RegistryBackup`-olion `Timestamp`, jotta aikaleimat ovat samat), `RegistryGuard.IsAllowed`,
  `RegistryPaths.OpenWritable`, `SafeFileDeleter.TryDeleteEmptyDirectory`, `PathGuard`. T4 `LeftoverScanner`, `LeftoverItemId`,
  `LeftoverScanOptions`; T5 `UninstallRunner`, `RunningProcessCheck`.

  `CleanAsync`-silmukka per kohde: moduulitarkistus → `LeftoverItemId.TryParse` → **`LeftoverGuard`** (uusi static-luokka, REQ-8:
  tiedostojärjestelmä: polku on jonkin `BaseFolders`/`ShortcutFolders`-kansion alla tai `Target.InstallLocation` tai sen alla,
  ei peruskansio itse, `PathGuard.IsSafeToDelete`; rekisteri: `RegistryGuard.IsAllowed(path, RegistryRoots + AppPathsRoots + RunKeys + ClassesRoots, allowExactRoot: false)`
  ja lisäksi polun segmentti ei saa olla `Microsoft`, `Classes`, `Policies`, `WOW6432Node`, `Clients`, `RegisteredApplications`
  paitsi kun avain on `...\CurrentVersion\Uninstall\<x>`- tai `...\CurrentVersion\App Paths\<x>`-muotoa tai `Classes\<ProgId>`
  ClassesRoots-juuren suora lapsi) → olemassaolo (`Directory.Exists`/`File.Exists`/`KeyExists`/arvo) → REQ-10 kansioille
  (`RunningProcessCheck.FindRunningUnder`) → kuiva ajo: `Removed` → oikea ajo: kansio/tiedosto `Quarantine.TryMove*`,
  avain `RegistryBackup.TryExport` + `DeleteSubKeyTree`, arvo `TryExport(avain)` + `DeleteValue`. Epäonnistuminen →
  `Skipped` syyllä; `UnauthorizedAccessException` → "Ei oikeuksia" / HKLM ilman korotusta → "Tarvitsee järjestelmänvalvojan oikeudet"
  (EDGE-8, kuten `RegistryModule`). REQ-9: siirron jälkeen, jos yläkansio on tyhjä eikä ole peruskansio, `SafeFileDeleter.TryDeleteEmptyDirectory`.
  Lopuksi `Quarantine.WriteManifest` ja `RegistryBackup.WriteManifest` (jos merkintöjä), lokiin karanteenikansio ja
  "palautus: pccleaner restore <ts>". `ScanAsync`: `Target == null` → `Errors.Add("Valitse ohjelma ensin.")`; muuten
  `LeftoverScanner.Scan(Target, TargetKeyRemoved, Id, context.Progress, ct)` `Task.Run`-kutsussa.

  Testit (`UninstallModuleTests`, CON-8): temp-peruskansiot ja testiavaimet kuten `LeftoverScannerTests`, karanteeni- ja
  rekisterijuuret temp-kansioissa: skannaus → kuiva ajo ei muuta mitään eikä luo kansioita; oikea ajo siirtää asennuskansion
  karanteeniin ja poistaa testiavaimen, manifestit syntyvät, `LastQuarantineDirectory` ja `LastBackupTimestamp` täytetyt ja
  aikaleimat samat; tyhjä julkaisijakansio poistuu (REQ-9); `BackupSessions.Restore(ts)` (T2) tuo kansion ja avaimen takaisin;
  väärennetyt tunnisteet (polku peruskansion ulkopuolella, peruskansio itse, `HKLM\SOFTWARE\Microsoft\Windows`, ForbiddenSegments,
  vieras moduuli) → kaikki `Skipped`, mitään ei siirretty; `Target == null` → virhe ilman poikkeusta.

## Wave 6

Kolme riippumatonta tehtävää: T7 muokkaa vain CLI-projektia, T8 vain UI-projektia, T9 vain dokumentteja. Tiedostojoukot
eivät leikkaa. T7:n ja T8:n Verify kääntää vain oman projektinsa `--no-dependencies`-valinnalla (Uninstall.dll on Wave 5:n portista).

### T7: CLI: uninstall-komento ja restore BackupSessions-luokalla
- Status: pending
- Model: opus
- Requirements: REQ-11
- Constraints/edge: CON-5, CON-7
- Files: src/Cleaner.Cli/Cleaner.Cli.csproj, src/Cleaner.Cli/ModuleFactory.cs, src/Cleaner.Cli/Program.cs, src/Cleaner.Cli/RestoreCommand.cs, src/Cleaner.Cli/UninstallCommand.cs
- Verify: `dotnet build src/Cleaner.Cli/Cleaner.Cli.csproj --no-dependencies -warnaserror` → exit 0, ja `dotnet run --project src/Cleaner.Cli --no-build -- uninstall` → tulostaa ohjelmalistan (vähintään otsikkorivi ja yksi ohjelma), poistumiskoodi 0
  Manual (human): `dotnet run --project src/Cleaner.Cli --no-build -- uninstall --program "<asennettu ohjelma>" --leftovers-only` listaa jäänteet kuivana ilman virheitä.
- Task:
  Lisää `ProjectReference` `..\Cleaner.Modules.Uninstall\Cleaner.Modules.Uninstall.csproj` CLI-projektiin ja rekisteröi
  `new UninstallModule()` `ModuleFactory.CreateAll`-metodin Windows-lohkoon (`src/Cleaner.Cli/ModuleFactory.cs`).
  Moduulin API:
  ```csharp
  UninstallModule { Target, TargetKeyRemoved, ListPrograms(), RunUninstallerAsync(program, quiet, context, ct), ScanAsync, CleanAsync, LastQuarantineDirectory, LastBackupTimestamp }
  InstalledProgram(KeyPath, KeyName, Name, Version, Publisher, InstallLocation, UninstallString, QuietUninstallString, DisplayIcon, EstimatedSizeBytes, IsMsi)
  UninstallRunResult(ExitCode, KeyRemoved, Elapsed, Error)
  CleanItem(..., RiskLevel Risk, bool? Preselected)
  BackupSessions.List() / Restore(ts, log) → RestoreResult(RegistryImported, RegistryFailed, QuarantineRestored, QuarantineSkipped, Messages)  // Cleaner.Core.Services
  ```
  Uusi `UninstallCommand` (static, `[SupportedOSPlatform("windows")]`, malli `RestoreCommand.cs`):
  `public static Task<int> RunAsync(string[] args, bool apply, bool yes, bool verbose, CleanerContext context, UninstallModule module)`.
  `Program.cs`: lisää `case "uninstall"` (Windows-guard kuten `restore`), `Values("--program")`, `Flag("--leftovers-only")`,
  `Flag("--quiet")`, `Flag("--all")`; `--item` on jo olemassa (`itemFilters`), välitä se. Ohjelmalistan tulostus: nimi,
  versio, julkaisija, koko (`ByteFormatter.Format`), avain. REQ-11:n valintasäännöt (tarkka nimi tai yksikäsitteinen
  osamerkkijono; usea → listaa ehdokkaat, koodi 2; ei yhtään → virhe, koodi 2). Poisto-ohjelman ajo tulostaa "Käynnistetään
  poisto-ohjelma..." ja tuloksen (`ExitCode`, `KeyRemoved`, `Error`), sitten jäänteet kuivana samassa muodossa kuin `scan`
  tulostaa vain pyydettäessä ajettavien moduulien kohteet (`Program.RunScan`: tunniste + koko + kuvaus); merkitse esivalitut
  kohteet `*`-etuliitteellä. `--apply`: kohteet = `--item`-rajaus, tai `--all` → kaikki, tai `Preselected == true`; vahvistus
  `[k/E]` ellei `yes` ("Siirretäänkö N kohdetta karanteeniin ja poistetaanko M rekisterikohdetta?"); aja `module.CleanAsync`
  kontekstilla `DryRun = false`; tulosta karanteenikansio ja "Palautus: pccleaner restore <ts>".
  `RestoreCommand`: vaihda `RegistryBackup.List/Restore` → `BackupSessions.List/Restore`, listaus näyttää `Summary`-tekstin,
  tulos kertoo molemmat määrät. Päivitä `PrintHelp`-teksti (`uninstall`-rivit ja `restore`-kuvaus "rekisteri + karanteeni").

### T8: UI: Ohjelman poisto -sivu
- Status: pending
- Model: opus
- Requirements: REQ-12
- Constraints/edge: CON-5, CON-7
- Files: src/Cleaner.UI/Cleaner.UI.csproj, src/Cleaner.UI/Services/UiModuleFactory.cs, src/Cleaner.UI/ViewModels/MainViewModel.cs, src/Cleaner.UI/ViewModels/ModuleViewModel.cs, src/Cleaner.UI/ViewModels/InstalledProgramViewModel.cs, src/Cleaner.UI/MainWindow.xaml, src/Cleaner.UI/MainWindow.xaml.cs
- Verify: `dotnet build src/Cleaner.UI/Cleaner.UI.csproj --no-dependencies -warnaserror` → exit 0
  Manual (human, korotettu sovellus): avaa "Ohjelman poisto", hae ohjelma, "Etsi jäänteet" näyttää kategoriat; "Poista ohjelma" pienelle testiohjelmalle näyttää odotustilan ja skannaa jäänteet automaattisesti; "Palauta varmuuskopio..." listaa session.
- Task:
  Lisää `ProjectReference` Uninstall-moduuliin ja `new UninstallModule()` `UiModuleFactory.CreateAll`-metodiin. Lue
  `src/Cleaner.UI/ViewModels/MainViewModel.cs` kokonaan: moduulikohtaiset paneelit (`IsDiskUsageSelected`, `IsRegistrySelected`,
  `OnSelectedModuleChanged`), `ScanModulesAsync` (asettaa `DiskUsageModule.Options` ennen skannausta — tee sama `UninstallModule.Target`-
  ja `TargetKeyRemoved`-ominaisuuksille), `CleanAsync`-ketjun tekstit (`isStartup`-haara; lisää `isUninstall`-haara: verbi
  "siirretään karanteeniin", vahvistusteksti kertoo karanteenin ja palautuksen), `RestoreRegistryBackupAsync` (vaihda
  `RegistryBackup.List/Restore` → `BackupSessions.List/Restore`, nimeä `RestoreBackupAsync`, nappi näkyy kun
  `IsRegistrySelected || IsUninstallSelected`). Moduulin API on T7:n tehtävätekstin mukainen (`Target`, `TargetKeyRemoved`,
  `ListPrograms()`, `RunUninstallerAsync(program, quiet, context, ct)` → `UninstallRunResult(ExitCode, KeyRemoved, Elapsed, Error)`,
  `InstalledProgram(KeyPath, KeyName, Name, Version, Publisher, InstallLocation, ..., EstimatedSizeBytes, IsMsi)`).

  `MainViewModel`: `IsUninstallSelected`, `Programs` (`ObservableCollection<InstalledProgramViewModel>`), `ProgramFilter` (string,
  suodattaa nimen ja julkaisijan osamerkkijonolla, `UpdateSourceTrigger=PropertyChanged` XAML:ssa), `SelectedProgram`,
  komennot `RefreshProgramsCommand`, `UninstallProgramCommand` (CanExecute: ei busy ja ohjelma valittu), `ScanLeftoversCommand`.
  `UninstallProgramCommand`: `BeginBusy("Odotetaan poisto-ohjelmaa: <nimi>")`, `RunUninstallerAsync`; `Error` → `InfoAsync`;
  peruutus → `InfoAsync("Odotus lopetettu", ...)`; onnistuessa aseta `Target`, `TargetKeyRemoved` ja kutsu `ScanModulesAsync` moduulille,
  sitten `RefreshPrograms`. `InstalledProgramViewModel`: `Name`, `Version`, `Publisher`, `SizeText` (`ByteFormatter`), `Program`.
  `MainWindow.xaml`: paneeli `Visibility="{x:Bind ViewModel.IsUninstallSelected, Mode=OneWay}"` DiskUsage-paneelin viereen:
  hakukenttä, `ListView` (MaxHeight 260, `SelectedItem` TwoWay `SelectedProgram`, rivillä nimi/versio/julkaisija/koko),
  napit "Päivitä lista", "Poista ohjelma" (AccentButtonStyle), "Etsi jäänteet". `ModuleViewModel`: ei muutoksia oletusvalintaan
  (T1 teki `Preselected`-säännön), mutta `Glyph`-switchiin `"uninstall" => "\uE74D"`-tyylinen oma merkki (käytä `"\uECAA"`).
  Käyttäjälle näkyvät tekstit suomeksi. `MainViewModel.CleanAsync`-tulosdialogi näyttää karanteenikansion ja palautusohjeen,
  kun moduuli on `uninstall` (`LastQuarantineDirectory`, `LastBackupTimestamp`).

### T9: Dokumentit
- Status: pending
- Model: opus
- Requirements: REQ-13
- Files: docs/ARCHITECTURE.md, README.md, HANDOFF.md
- Verify: `grep -c "uninstall" docs/ARCHITECTURE.md README.md HANDOFF.md` → jokaisessa ≥ 1; `dotnet build PcCleaner.sln -warnaserror` ei koske dokumentteja (ei ajeta)
- Task:
  Päivitä dokumentit tämän suunnitelman mukaan (lue suunnitelma `docs/plans/2026-10-19-uninstall-module.md` kokonaan; se on
  tämän tehtävän ainoa lähde yhdessä koodin kanssa). `docs/ARCHITECTURE.md`: uusi luku 6.4 "Ohjelman poisto (`uninstall`)"
  samassa muodossa kuin 6.3 (taulukko kategorioista, säännöt, tunnisteet, karanteeni ja `BackupSessions`, vartija, mitä ei
  kosketa), luvun 2 kerroskaavion moduulilistaan `Uninstall`, luvun 8 testaukseen uudet testiluokat, luvun 3.2 `CleanItem`-
  riville `Preselected`. `README.md`: käyttöesimerkit `uninstall`, `uninstall --program ... --leftovers-only`, `--apply`,
  `restore` (rekisteri + karanteeni), turvallisuusperiaate 7 "Jäänteet karanteeniin, ei poistoon". `HANDOFF.md`:
  projektitaulukkoon `src/Cleaner.Modules.Uninstall` ja Core-palveluiden siirto, testimäärä, päätökset 21 (Win32 vain), 22
  (karanteeni + yhteinen aikaleima), 23 (laaja haku, säännöt ARCHITECTURE 6.4), 24 (`CleanItem.Preselected`), tunnisteet
  `uninstall:dir|file|key|value`, tunnetut rajoitukset (Store-sovellukset, 32-bittisten prosessien polut, julkaisijakansio
  vain kun ei muita lapsia), "Seuraavat vaiheet" päivitettynä. Kirjoita suomeksi, ilman ajatusviivoja, tiiviisti.

## Final verification
- AC-1 [auto]: `dotnet build PcCleaner.sln -warnaserror` → exit 0, 0 varoitusta
- AC-2 [auto]: `dotnet test PcCleaner.sln --no-build` → "Passed!", 0 epäonnistunutta, testejä ≥ 67 + uudet (NameMatcher, InstalledProgramReader, Quarantine, BackupSessions, LeftoverScanner, UninstallRunner, UninstallModule)
- AC-3 [auto]: `dotnet run --project src/Cleaner.Cli --no-build -- uninstall` → ohjelmalista, koodi 0; `dotnet run --project src/Cleaner.Cli --no-build -- restore` → listaus tai "Ei varmuuskopioita", koodi 0
- AC-4 [auto]: `dotnet publish src/Cleaner.UI -c Release -o publish/PcCleaner` → exit 0 ja `publish/PcCleaner/Cleaner.Modules.Uninstall.dll` on olemassa
- AC-5 [manual]: korotetussa UI:ssa "Etsi jäänteet" jollekin asennetulle ohjelmalle näyttää kategoriat, kuvaukset kertovat osumasyyn, mikään High-kohde ei ole valittuna, asennuskansio on valittuna
- AC-6 [manual, blocking]: käyttäjä poistaa UI:sta yhden pienen, vapaaehtoisesti valitun testiohjelman: poisto-ohjelma käynnistyy, odotustila näkyy, jäänteet skannataan automaattisesti, Puhdista siirtää esivalitut karanteeniin, "Palauta varmuuskopio..." tuo ne takaisin ja `restore <ts>` CLI:ssä listaa saman session

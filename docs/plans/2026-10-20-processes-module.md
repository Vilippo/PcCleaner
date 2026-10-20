# Plan: Prosessit-moduuli (processes)
Branch: feature/processes-module
Created: 2026-10-20

## Background & scope

Uusi moduuli `processes` ("Prosessit") projektissa `src/Cleaner.Modules.Processes`. Skannaus listaa käyttäjän
istunnon prosessit, jotka eivät ole Windows-kansiossa, luokittelee ne kuuteen kategoriaan ja perustelee jokaisen
löydöksen kuvauksessa. Puhdistus lopettaa valitut prosessit (ensin ikkunan sulkupyyntö, sitten Kill). Mitään ei
poisteta levyltä eikä rekisteristä. Moduuli toimii samalla sopimuksella kuin muut (`ICleanerModule`, kuiva ajo,
`SelectedByDefault = false`), ja näkyy CLI:ssä (`--module processes`) ja WinUI-sovelluksessa ilman uutta sivua.

Käyttäjän päätökset (kysytty 20.10.2026):

1. Järjestelmäprosessit (Windows-kansion exe, muiden istuntojen prosessit, palvelut) piilotetaan kokonaan. Niitä ei
   listata eikä niitä voi valita.
2. Oletuksena rastittuina ovat vain prosessit, joiden ohjelmatiedosto puuttuu levyltä, sekä orvot apuprosessit,
   jotka eivät käyttäneet suoritinta mittausjakson aikana. Kaikki muu valitaan käsin.
3. Saman exen lapsiprosessit (Edge, Spotify, Discord) sulautetaan elossa olevan vanhemman riviin: yksi rivi per
   ohjelma, muisti summataan, lopetus lopettaa koko joukon.

Jo aiemmin sovittu (keskustelu 20.10.2026): ikkunalliset ohjelmat listataan High-riskinä eikä niitä esivalita
koskaan; Windows-palvelut eivät kuulu moduuliin; sääntötiedosto tunnetuille taustaprosesseille kuuluu mukaan.

Ulkopuolella: palveluiden pysäytys tai poisto käytöstä; linkitys käynnistysohjelmiin ("käynnistyy Run-avaimesta X",
nappi käynnistysohjelmat-sivulle) on vaihe 2; muistin yhteissumman näyttö kategoriaotsikossa ja tilarivillä
(UI näyttää summat vain `SelectedByDefault`-moduuleille, ks. Assumptions); prosessien uudelleenkäynnistys.

Olemassa oleva koodi, johon tämä nojaa (lue ennen toteutusta): `docs/ARCHITECTURE.md` luku 3 (sopimus) ja luku 6.1
(Startup-moduuli mallina), `src/Cleaner.Modules.Startup/StartupModule.cs` (Task.Run-kääre, DryRun, Skipped-syyt,
Id-jäsennys), `src/Cleaner.Modules.Uninstall/RunningProcessCheck.cs` (prosessien läpikäynti ja poikkeusten
käsittely), `src/Cleaner.Core/Rules/RuleLoader.cs` (JSON-asetukset), `src/Cleaner.Modules.Files/RecycleBinModule.cs`
(P/Invoke-käytäntö: `[DllImport]`, private, `#region P/Invoke`).

## Requirements

Skannaus ja luokittelu

- REQ-1: `ProcessesModule` toteuttaa `ICleanerModule`: `Id = "processes"`, `DisplayName = "Prosessit"`,
  `Risk = RiskLevel.High` (korkein kohdetaso, ks. Assumptions), `RequiresAdmin = false`, `SelectedByDefault = false`.
  `ScanAsync` ja `CleanAsync` ajavat työn `Task.Run`-kutsussa ja raportoivat edistymisen `context.Report`-kutsuilla
  (vähintään alussa, mittausjakson alussa ja lopussa `Report(100, "Valmis")`).
- REQ-2: Skannaus ottaa kaksi näytettä prosessien suoritinajasta (`TotalProcessorTime`) mittausvälin päässä
  toisistaan (oletus 3 s, konstruktorissa säädettävä). Prosessi on joutilas (`IsIdle = true`) vain, jos molemmat
  näytteet onnistuivat ja arvo ei muuttunut. Lukuvirhe tarkoittaa `IsIdle = false`.
- REQ-3: Skannaus ei koskaan heitä prosessikohtaista virhettä ulos: prosessi, jonka tieto ei ole luettavissa
  (`Win32Exception`, `InvalidOperationException`, kadonnut prosessi), saa puuttuvan arvon (`ExePath = null`,
  `StartFileTime = 0`, `CpuTime = null`) ja käsitellään suojattuna (REQ-4). Lisäksi inventaario koettaa avata
  prosessin `PROCESS_TERMINATE`-oikeudella (0x0001) ja tallentaa tuloksen kenttään `CanTerminate`; korotetut ja
  suojatut prosessit saavat `false`, kun ohjelma ajetaan tavallisena käyttäjänä.
- REQ-4: `ProcessGuard.Protects(info, snapshot)` palauttaa syyn (ei null) prosessille, joka on jokin seuraavista:
  oma prosessi tai oman prosessin vanhempi; PID 0 tai 4; `SessionId` eri kuin oma istunto; `ExePath` null tai
  `StartFileTime` 0; `ExePath` on `Environment.GetFolderPath(SpecialFolder.Windows)`-kansion alla (kirjainkoko ei
  merkitse); `IsCritical = true`; `CanTerminate = false`. Suojattuja prosesseja ei listata skannauksessa, ja `CleanAsync`
  tarkistaa saman ennen lopetusta (REQ-15).
- REQ-5: Sulautus. Prosessi, jonka vanhempi on elossa, ei ole suojattu, jolla on sama `ExePath` (kirjainkoko ei
  merkitse) ja jonka vanhemman `StartFileTime` on pienempi tai yhtä suuri kuin oma, sulautetaan vanhempaansa.
  Ketju seurataan ylimpään saman exen esivanhempaan (juureen). Vain juuri tuottaa `CleanItem`-kohteen. Juuren
  `SizeBytes` on juuren ja kaikkien sulautettujen lasten `PrivateBytes`-summa, ja kuvaus kertoo lasten määrän
  ("N aliprosessia") kun N > 0.
- REQ-6: Ryhmän (juuri + sulautetut lapset) ominaisuudet: `HasVisibleWindow` on tosi, jos jollakin ryhmän jäsenellä
  on näkyvä ikkuna; `IsIdle` on tosi vain, jos kaikki jäsenet ovat joutilaita.
- REQ-7: Orpo. Juuri on orpo, jos sen `ParentPid` ei ole koko näytteen (myös suojattujen) elossa olevien PID-lukujen
  joukossa, tai jos vanhempi löytyy mutta vanhemman `StartFileTime` on suurempi kuin juuren oma (PID on kierrätetty).
- REQ-8: Jokainen juuri saa täsmälleen yhden kategorian, ensimmäinen osuva sääntö voittaa, tässä järjestyksessä:
  1. Ryhmällä näkyvä ikkuna → "Avoimet ohjelmat", High, `Preselected = false`. Ikkunallinen ryhmä ei koskaan päädy
     muuhun kategoriaan, vaikka exe puuttuisi tai nimi osuisi sääntöön; nämä tiedot näkyvät vain kuvauksessa.
  2. `ExeMissing` → "Ohjelmatiedosto puuttuu levyltä", Medium, `Preselected = true`.
  3. Orpo (REQ-7) → "Orvot apuprosessit", Medium, `Preselected = ryhmän IsIdle`.
  4. Sääntötiedoston osuma (REQ-10) → "Tunnetut taustaprosessit", säännön `Risk`, `Preselected = false`.
  5. Ryhmän `IsIdle` → "Taustaprosessit, ei käyttöä", Medium, `Preselected = false`.
  6. Muuten → "Taustaprosessit, aktiivisia", Medium, `Preselected = false`.
- REQ-9: `CleanItem`: `Id = processes:<pid>:<startFileTime>` (`ProcessItemId.Make` / `TryParse`, molemmat luvut
  kymmenjärjestelmän kokonaislukuina), `ModuleId = "processes"`, `Category` REQ-8:n mukaan, `Path = ExePath`,
  `SizeBytes` REQ-5:n mukaan, `Description` koostuu osista erotettuna merkillä " · " tässä järjestyksessä, tyhjät
  osat pois: syy (esim. "Käynnistäjä (PID 1234) on päättynyt", "Ohjelmatiedostoa ei löydy", säännön `Name`),
  "ei suorittimen käyttöä N s aikana" tai "käyttää suoritinta", "käynnistetty <ikä>" (muodossa "N min", "N h M min" tai
  "N d H h"), "N aliprosessia", "käynnistäjä <nimi> (PID n)" kun vanhempi on elossa. `Risk` ja `Preselected` REQ-8:n mukaan.
- REQ-10: Sääntötiedosto `rules/processes/known.json` (projektissa `src/Cleaner.Modules.Processes/rules/processes/known.json`,
  kopioidaan tulosteeseen `None`-itemillä kuten Files-moduulissa). Skeema: `schemaVersion` (1), `rules[]` joilla
  `id` (pakollinen, yksilöllinen), `name` (pakollinen), `match[]` (pakollinen, vähintään yksi prosessinimen
  jokerikuvio: `*` ja `?`, kirjainkoko ei merkitse, pääte `.exe` saa puuttua kuviosta tai nimestä), `description`,
  `risk` (Low | Medium | High, oletus Medium). `ProcessRuleLoader.LoadFromFile`/`LoadFromJson` käyttää samoja
  JSON-asetuksia kuin `RuleLoader` (kommentit, loppupilkut, kirjainkoko, enum-merkkijonot) ja hylkää
  `InvalidDataException`-poikkeuksella tyhjän tai toistuvan `id`:n ja tyhjän `match`-listan.
- REQ-11: Toimitettu `known.json` sisältää vähintään nämä säännöt (nimi ja kuvaus suomeksi, Risk Medium ellei toisin):
  `google-crash-handler` (match `GoogleCrashHandler.exe`, `GoogleCrashHandler64.exe`), `adobe-ipc-broker`
  (`AdobeIPCBroker.exe`), `adobe-ccx-process` (`CCXProcess.exe`), `adobe-collab-sync` (`AdobeCollabSync.exe`),
  `steam-webhelper` (`steamwebhelper.exe`, Low), `discord-helper` (`Discord*.exe`, Low; kun Discordin pääikkuna on auki,
  ryhmä on REQ-8:n kohdan 1 mukaan "Avoimet ohjelmat" ja sääntö näkyy vain kuvauksessa, ks. EDGE-6).
- REQ-12: Jos sääntötiedostoa ei ole, skannaus toimii ilman sääntöjä ja kirjaa yhden `Logger.Warning`-rivin
  ("Sääntötiedostoa ei löydy: <polku>"). Virheellinen tiedosto kirjataan `Logger.Warning`-rivillä
  "Sääntötiedosto on virheellinen: <polku>: <viesti>" ja sama teksti lisätään `ScanResult.Errors`-listaan.

Lopetus

- REQ-13: `CleanAsync` ohittaa kohteen syyllä "Kohde ei kuulu tälle moduulille", jos `item.ModuleId != "processes"`,
  ja syyllä "Virheellinen tunniste", jos `ProcessItemId.TryParse` epäonnistuu.
- REQ-14: Kun `context.DryRun == true`, `CleanAsync` ei lopeta mitään, mutta lisää kohteen `Removed`-listaan, jos
  prosessi on elossa ja sen `StartFileTime` vastaa tunnistetta, ja kirjaa `Logger.Debug`-rivin "[KUIVA] Lopetettaisiin ...".
- REQ-15: Ennen lopetusta `CleanAsync` hakee prosessin PID:llä; jos prosessia ei ole, syy on "Prosessi on jo
  päättynyt"; jos `StartTime.ToFileTime()` ei vastaa tunnistetta, syy on "PID on käytetty uudelleen"; jos
  `ProcessGuard.Protects` palauttaa syyn tuoreelle tiedolle, kohde ohitetaan sillä syyllä. Kaikki nämä menevät
  `Skipped`-listaan ja `Logger.Warning`-riville.
- REQ-16: Lopetus kahdessa vaiheessa (`ProcessTerminator.TryTerminate(pid, startFileTime, out reason)`): jos
  prosessilla on pääikkuna, kutsutaan `CloseMainWindow()` ja odotetaan enintään 5 s; jos prosessi on yhä elossa,
  kutsutaan `Kill()` ja odotetaan enintään 5 s. Onnistuminen = prosessi on päättynyt. `Win32Exception` → syy
  "Ei oikeuksia", muu poikkeus → poikkeuksen viesti. Prosessia, jonka `StartTime` ei vastaa annettua, ei koskaan lopeteta.
- REQ-17: Kun juuri lopetetaan oikeasti, `CleanAsync` lopettaa lisäksi tuoreesta näytteestä juuren saman exen
  jälkeläiset transitiivisesti (`ProcessClassifier.CollectSameExeDescendants`, sama ehto kuin REQ-5:n sulautuksessa:
  sama `ExePath`, `StartFileTime` vähintään vanhemman), syvyysjärjestyksessä lapsista lapsenlapsiin. Jälkeläisen
  epäonnistunut lopetus kirjataan `Logger.Warning`-riville eikä muuta juuren tulosta. Jälkeläisiä ei haeta kuivassa ajossa.
- REQ-18: `CleanAsync` kirjaa jokaisesta oikeasta lopetuksesta `Logger.Info`-rivin, joka sisältää prosessin nimen,
  PID:n ja `ExePath`-polun, jotta käyttäjä voi käynnistää ohjelman uudelleen lokin perusteella.

Kytkentä

- REQ-19: CLI: `ModuleFactory.CreateAll` lisää `new ProcessesModule(Path.Combine(rulesDirectory, "processes", "known.json"))`
  Windows-lohkoon; `Cleaner.Cli.csproj` viittaa projektiin; `Program.cs`: ohjetekstin moduulilista mainitsee
  `'processes'`, oma selitysrivi "'processes' listaa käyttäjän istunnon taustaprosessit ja lopettaa valitut; mitään
  ei poisteta levyltä.", ja `--apply`-vahvistuksen teksti laajenee muotoon "... käynnistysohjelmat poistetaan
  käytöstä, prosessit lopetetaan."
- REQ-20: UI: `UiModuleFactory.CreateAll` lisää moduulin samalla polulla kuin REQ-19; `Cleaner.UI.csproj` viittaa
  projektiin; `ModuleViewModel.Glyph` saa haaran `"processes"` (ei-tyhjä Segoe Fluent Icons -merkki, eri kuin muilla
  moduuleilla); `MainViewModel.CleanAsync`: verbi "lopetetaan", toinen vahvistus
  "Lopetetaanko {n} prosessia{ (koko)}?\n\nTallentamattomat tiedot katoavat, jos ohjelma on auki. Mitään ei poisteta
  levyltä. Ohjelman voi käynnistää uudelleen tavallisesti.", painike "Lopeta prosessit", ja tulosteksti käyttää
  prosesseille muotoa ", vapautettu muistia X" tavallisen ", vapautettu X" sijaan.
- REQ-21: `PcCleaner.sln` sisältää projektin `Cleaner.Modules.Processes` (GUID `{A1B2C3D4-0010-4000-8000-000000000010}`)
  neljällä Any CPU -konfiguraatiorivillä, ja `tests/Cleaner.Tests/Cleaner.Tests.csproj` viittaa projektiin.
- REQ-22: Dokumentit: `docs/ARCHITECTURE.md` luvun 1 taulukkoon rivi 9 (`processes`, High, luku 6.5) ja uusi luku 6.5,
  joka kuvaa kategoriat (REQ-8), suojaukset (REQ-4), sulautuksen (REQ-5), tunnisteen (REQ-9) ja lopetuksen
  (REQ-15..17); `README.md` rakennepuuhun projekti, CLI-esimerkkeihin `scan --module processes` ja
  `clean --module processes --item "processes:<pid>:<käynnistysaika>" --apply` ja varoitus, että pelkkä etuliite
  `"processes:"` tai `--apply` ilman `--item` lopettaa kaikki listatut prosessit, myös avoimet ohjelmat (CLI ei
  suodata `Preselected`- tai `Risk`-tiedon mukaan, `Program.cs` RunClean), sekä kappale moduulista; `HANDOFF.md` taulukkoon rivi.

## Edge cases

- EDGE-1: Prosessi päättyy näytteiden välissä → toinen näyte epäonnistuu → `IsIdle = false`, prosessi listataan
  vain, jos se on yhä elossa toisen näytteen hetkellä (muuten pois).
- EDGE-2: Prosessi, jonka vanhempi on suojattu (explorer.exe, svchost.exe) → ei orpo, ei sulautusta (eri exe);
  kuvauksessa "käynnistäjä explorer (PID n)".
- EDGE-3: Prosessin PID-tunniste osoittaa prosessiin, joka on kuollut ja PID käytetty uudelleen eri ohjelmalle →
  `StartFileTime` ei vastaa → Skipped "PID on käytetty uudelleen", mitään ei lopeteta (REQ-15).
- EDGE-4: Käyttäjän valitsema juuri on itse sulautettu lapsi (ei mahdollista UI:sta, mahdollista CLI:n `--item`-osumalla
  vanhaan tunnisteeseen) → käsitellään tavallisena kohteena, lapset haetaan REQ-17:n mukaan sen alta.
- EDGE-5: CLI tavallisena käyttäjänä: korotetun prosessin polku on luettavissa (`PROCESS_QUERY_LIMITED_INFORMATION` ei
  kuulu no-read-up-maskiin), mutta `OpenProcess(PROCESS_TERMINATE)` epäonnistuu → `CanTerminate = false` → suojattu →
  ei listata. Skannaus ei tuota `Errors`-riviä tästä, vain `Logger.Debug`-rivin lukumäärästä.
- EDGE-6: Ikkunallinen ohjelma, jonka nimi osuu sääntöön tai jonka exe puuttuu (esim. Discord.exe pääikkunalla,
  päivityksen vaihtama Chrome) → kategoria "Avoimet ohjelmat", High, ei esivalittu (REQ-8 kohta 1). Säännön nimi ja
  "Ohjelmatiedostoa ei löydy" lisätään kuvaukseen. Kategoriaa ei nosteta High-tasolle yksittäisen kohteen takia, koska UI
  sijoittaa koko kategorian korkean riskin lohkoon kategorian maksimiriskin mukaan (`ModuleViewModel.LoadScan`).
- EDGE-7: `CloseMainWindow()` palauttaa false (ei ikkunaa tai viestiä ei voitu lähettää) → siirrytään suoraan `Kill()`-vaiheeseen.
- EDGE-8: Sulautetun lapsen vanhempi on elossa mutta eri exe (Steam → steamwebhelper.exe) → ei sulautusta, lapsi on
  oma juuri, kuvauksessa "käynnistäjä steam (PID n)".
- EDGE-9: Prosessilla ei ole `ExePath`-tietoa, mutta Toolhelp antaa nimen → suojattu (REQ-4), ei listata.
- EDGE-10: Kaksi juurta samalla exe-polulla ilman vanhempi-lapsi-suhdetta (kaksi erillistä Notepad-ikkunaa) → kaksi riviä,
  eri PID, ei sulautusta.

## Constraints

- CON-1: Moduuli viittaa vain `Cleaner.Core`-projektiin. Ei viittauksia muihin moduuleihin eikä UI:hin. Core ei muutu.
- CON-2: Projekti on `net8.0` (ei `net8.0-windows`), `Nullable` ja `ImplicitUsings` päällä, `LangVersion latest`.
  Windows-kohtaiset luokat merkitään `[SupportedOSPlatform("windows")]`, jotta CA1416 ei varoita.
  `dotnet build PcCleaner.sln -warnaserror` on lint-tarkistus (repo ei käytä dotnet formatia eikä .editorconfigia).
- CON-3: P/Invoke: `[DllImport]`, `private static extern`, `CharSet.Unicode`, luokan sisällä `#region P/Invoke`
  (RecycleBinModule-käytäntö). Ei `LibraryImport`-generaattoria, ei NuGet-paketteja.
- CON-4: Sääntötiedosto ei saa päätyä tulosteen `rules\`-kansion juureen: testi `Shipped_rule_files_are_valid`
  (`tests/Cleaner.Tests/RuleLoaderTests.cs`) lataa jokaisen `rules\*.json`-tiedoston `RuleLoader`-luokalla, joka vaatii
  tiedostosääntöjen kentät. Siksi polku on `rules\processes\known.json` (alikansio, `GetFiles` ei ole rekursiivinen).
- CON-5: Prosessin tunnistetieto on kernelin luontiaika FILETIME-muodossa (`Process.StartTime.ToFileTime()`), ei PID yksin.
- CON-6: Skannaus ei muuta mitään (ei lopeta, ei kirjoita). Kaikki lopetukset kirjataan lokiin.

## Assumptions

- Moduulin `Risk = High`, koska sopimus (`ICleanerModule.Risk`: "moduulin korkein riskitaso") ja "Avoimet ohjelmat"
  -kategoria on High. Oletuskohteet (taustaprosessit) ovat Medium ja esivalinta koskee vain Medium-kohteita.
- Näkyvä ikkuna todetaan `Process.MainWindowHandle != IntPtr.Zero` -tarkistuksella (näkyvä, omistajaton
  ylätason ikkuna). Tray-sovellus, jonka ikkuna on piilotettu, on "taustaprosessi"; sen lopetus ei hävitä tallentamatonta työtä.
- UI näyttää opt-in-moduuleille kategorian ja tilarivin summat vain `SelectedByDefault`-moduuleille
  (`ModuleViewModel.LoadScan`, `CategoryViewModel.showTotal`). Prosessien muistisumma näkyy siis vain rivikohtaisesti
  ja valintatekstissä "Valittu N kohdetta, X". Muutos UI:n yleiseen logiikkaan ei kuulu tähän suunnitelmaan.
- Prosessien PPID ja nimi luetaan Toolhelp32-snapshotilla (`CreateToolhelp32Snapshot`, `Process32FirstW/NextW`);
  exe-polku `QueryFullProcessImageNameW`-kutsulla kahvalla `PROCESS_QUERY_LIMITED_INFORMATION` (0x1000), joka toimii
  32-bittisille ja useimmille suojatuille prosesseille; `IsProcessCritical` samalla kahvalla; erillinen
  `OpenProcess(PROCESS_TERMINATE)` -koe kertoo, voiko prosessin lopettaa nykyisillä oikeuksilla. Muut kentät
  (`SessionId`, `PrivateMemorySize64`, `StartTime`, `TotalProcessorTime`, `MainWindowHandle`) `System.Diagnostics.Process`-luokasta.
- Mittausväli 3 s on riittävä: Windowsin ajastinraja on 15,6 ms, joten prosessi, joka käyttää suoritinta edes
  satunnaisesti, näkyy aktiivisena. Lyhyemmän välin saa konstruktorista (testit käyttävät 200 ms).
- Prosessin uudelleenkäynnistys ei kuulu moduuliin; loki (REQ-18) riittää palautusohjeeksi.
- `IProcessInventory.Capture(TimeSpan.Zero, ct)` palauttaa näytteen ilman suoritinmittausta (`IsIdle = false` kaikille);
  `CleanAsync` käyttää sitä lasten hakuun (REQ-17).
- Integraatiotesti käynnistää kopion `cmd.exe`-tiedostosta tilapäiskansiosta (`%TEMP%\pccleaner-tests\<guid>\pcc-test-cmd.exe`
  argumentilla `/c ping -n 60 127.0.0.1 >nul`, `CreateNoWindow`), koska Windows-kansion alkuperäinen olisi suojattu (REQ-4).
  Testi lopettaa prosessin ja poistaa kansion `Dispose`-metodissa.

## Disposition

- CON-1: toteutuu rakenteella (csproj viittaa vain Coreen), tarkistetaan T1:n Verify-rivillä (grep ProjectReference).
- CON-6: toteutuu puuttumalla: `ProcessesModule.Scan` kutsuu vain `IProcessInventory.Capture`, `ProcessClassifier.Classify`
  ja `ProcessRuleLoader`; mikään niistä ei avaa kirjoittavaa kahvaa. AC-4 tarkistaa, että kuiva ajo jättää prosessin eloon.
- EDGE-4, EDGE-10: sallivat käytöksen, joka seuraa REQ-5:n ja REQ-17:n sanamuodosta; ei rakennettavaa.

## Wave 1

Yksi tehtävä: T1 luo projektin, mallit ja puhtaan logiikan, joihin kaikki muut tehtävät nojaavat.

### T1: Projekti, mallit, säännöt, suojaus ja luokittelija
- Status: done 5904363
- Model: fable
- Requirements: REQ-4, REQ-5, REQ-6, REQ-7, REQ-8, REQ-9 (vain `ProcessItemId` ja kuvauksen koostaminen), REQ-10, REQ-11, REQ-21
- Constraints/edge: CON-1, CON-2, CON-4, EDGE-2, EDGE-6, EDGE-8, EDGE-9, EDGE-10
- Files: PcCleaner.sln, tests/Cleaner.Tests/Cleaner.Tests.csproj,
  src/Cleaner.Modules.Processes/Cleaner.Modules.Processes.csproj,
  src/Cleaner.Modules.Processes/ProcessInfo.cs,
  src/Cleaner.Modules.Processes/ProcessSnapshot.cs,
  src/Cleaner.Modules.Processes/IProcessInventory.cs,
  src/Cleaner.Modules.Processes/IProcessTerminator.cs,
  src/Cleaner.Modules.Processes/ProcessCategory.cs,
  src/Cleaner.Modules.Processes/ClassifiedProcess.cs,
  src/Cleaner.Modules.Processes/ProcessRule.cs,
  src/Cleaner.Modules.Processes/ProcessRuleLoader.cs,
  src/Cleaner.Modules.Processes/ProcessItemId.cs,
  src/Cleaner.Modules.Processes/ProcessGuard.cs,
  src/Cleaner.Modules.Processes/ProcessClassifier.cs,
  src/Cleaner.Modules.Processes/ProcessDescription.cs,
  src/Cleaner.Modules.Processes/rules/processes/known.json,
  tests/Cleaner.Tests/ProcessClassifierTests.cs,
  tests/Cleaner.Tests/ProcessRuleLoaderTests.cs
- Verify: `dotnet build PcCleaner.sln -warnaserror` → "0 Warning(s)", "0 Error(s)";
  `dotnet test PcCleaner.sln --no-build --filter "FullyQualifiedName~ProcessClassifierTests|FullyQualifiedName~ProcessRuleLoaderTests|FullyQualifiedName~RuleLoaderTests"` → "Passed!", 0 failed
  (RuleLoaderTests mukana, koska `Shipped_rule_files_are_valid` ei saa alkaa lukemaan known.json-tiedostoa);
  `grep -c ProjectReference src/Cleaner.Modules.Processes/Cleaner.Modules.Processes.csproj` → 1 (vain Core)
- Task:
  Luo uusi kirjastoprojekti `src/Cleaner.Modules.Processes` (nimiavaruus `Cleaner.Modules.Processes`) PC Cleaner
  -sovellukseen. Se on tuleva `ICleanerModule`-moduuli, joka listaa käyttäjän istunnon prosessit ja lopettaa valitut.
  Tässä tehtävässä syntyvät projekti, tietomallit, rajapinnat, sääntötiedosto ja puhdas logiikka (suojaus, sulautus,
  luokittelu, kuvaus). Win32-inventaario, lopetus ja moduuliluokka tulevat myöhemmissä tehtävissä, jotka nojaavat
  tässä määriteltyihin tyyppeihin, joten nimet ja muodot alla ovat sitovia.

  Projekti: kopioi `src/Cleaner.Modules.Startup/Cleaner.Modules.Startup.csproj` (net8.0, Nullable, ImplicitUsings,
  LangVersion latest, vain `..\Cleaner.Core\Cleaner.Core.csproj`-viittaus) ja lisää
  `<None Include="rules\**\*.json" CopyToOutputDirectory="PreserveNewest" />` kuten `src/Cleaner.Modules.Files/Cleaner.Modules.Files.csproj`.
  Lisää projekti `PcCleaner.sln`-tiedostoon GUID:lla `{A1B2C3D4-0010-4000-8000-000000000010}` (Project-lohko Uninstall-lohkon
  jälkeen, neljä `Debug|Any CPU` / `Release|Any CPU` -riviä `ProjectConfigurationPlatforms`-osioon Uninstall-rivien jälkeen,
  malli: Uninstall-rivit GUID:lla `{A1B2C3D4-0009-...}`). Lisää `ProjectReference` tiedostoon `tests/Cleaner.Tests/Cleaner.Tests.csproj`
  muiden moduulien viereen (`..\..\src\Cleaner.Modules.Processes\Cleaner.Modules.Processes.csproj`).

  Sääntötiedosto sijoitetaan polkuun `rules/processes/known.json` (alikansio!), koska testi `Shipped_rule_files_are_valid`
  tiedostossa `tests/Cleaner.Tests/RuleLoaderTests.cs` lataa jokaisen tulosteen `rules\*.json`-tiedoston
  tiedostosääntöjen `RuleLoader`-luokalla ja kaatuisi eri skeemaan. `Directory.GetFiles(dir, "*.json")` ei ole
  rekursiivinen, joten alikansio on turvassa. Älä muuta sitä testiä.

  Tyypit (kaikki `public`, `[SupportedOSPlatform("windows")]` vain luokille, jotka kutsuvat Windows-rajapintoja; näistä mikään ei kutsu):

  ```csharp
  public sealed record ProcessInfo(
      int Pid, int ParentPid, string Name, string? ExePath, long StartFileTime, int SessionId,
      long PrivateBytes, TimeSpan? CpuTime, bool HasVisibleWindow, bool IsCritical, bool CanTerminate, bool ExeMissing, bool IsIdle);

  public sealed record ProcessSnapshot(
      IReadOnlyList<ProcessInfo> Processes, int CurrentSessionId, int OwnPid, int OwnParentPid,
      DateTimeOffset TakenAt, TimeSpan SampleInterval);

  public interface IProcessInventory
  {
      /// Ottaa näytteen kaikista prosesseista. sampleInterval > 0: kaksi suoritinaikanäytettä, IsIdle täytetään.
      /// sampleInterval == 0: yksi näyte, IsIdle = false kaikilla. Ei heitä prosessikohtaisia virheitä.
      ProcessSnapshot Capture(TimeSpan sampleInterval, CancellationToken ct);
  }

  public interface IProcessTerminator
  {
      /// Lopettaa prosessin vain, jos sen StartTime.ToFileTime() == startFileTime. Palauttaa true kun prosessi on päättynyt.
      bool TryTerminate(int pid, long startFileTime, out string reason);
  }

  public enum ProcessCategory { MissingExe, Orphan, Known, Windowed, IdleBackground, ActiveBackground }

  public sealed record ClassifiedProcess(
      ProcessInfo Root, IReadOnlyList<ProcessInfo> Children, ProcessCategory Category,
      RiskLevel Risk, bool Preselected, ProcessRule? Rule, ProcessInfo? Parent)
  {
      public long TotalPrivateBytes => Root.PrivateBytes + Children.Sum(c => c.PrivateBytes);
      public bool HasVisibleWindow => Root.HasVisibleWindow || Children.Any(c => c.HasVisibleWindow);
      public bool IsIdle => Root.IsIdle && Children.All(c => c.IsIdle);
  }

  public sealed class ProcessRule
  {
      public required string Id { get; init; }
      public required string Name { get; init; }
      public required List<string> Match { get; init; }
      public string Description { get; init; } = "";
      public RiskLevel Risk { get; init; } = RiskLevel.Medium;
      public bool Matches(string processName);   // jokerit * ja ?, kirjainkoko ei merkitse, ".exe" saa puuttua kummalta puolelta tahansa
  }

  public sealed class ProcessRuleSet { public int SchemaVersion { get; init; } = 1; public List<ProcessRule> Rules { get; init; } = new(); }

  public static class ProcessRuleLoader
  {
      public static ProcessRuleSet LoadFromFile(string path);
      public static ProcessRuleSet LoadFromJson(string json, string sourceName = "<json>");  // InvalidDataException virheistä
  }

  public static class ProcessItemId
  {
      public const string Prefix = "processes";
      public static string Make(int pid, long startFileTime) => $"{Prefix}:{pid}:{startFileTime}";
      public static bool TryParse(string id, out int pid, out long startFileTime);
  }

  public static class ProcessGuard
  {
      /// Syy suomeksi tai null, kun prosessia saa listata ja lopettaa.
      public static string? Protects(ProcessInfo info, ProcessSnapshot snapshot);
  }

  public static class ProcessClassifier
  {
      public static IReadOnlyList<ClassifiedProcess> Classify(ProcessSnapshot snapshot, IReadOnlyList<ProcessRule> rules);
      /// Pid:n saman exen jälkeläiset transitiivisesti (sama ExePath OrdinalIgnoreCase, StartFileTime >= vanhemman),
      /// syvyysjärjestyksessä lapsista lapsenlapsiin, suojattuja ei suodateta. Tyhjä, jos pid ei ole näytteessä.
      public static IReadOnlyList<ProcessInfo> CollectSameExeDescendants(ProcessSnapshot snapshot, int pid);
  }

  public static class ProcessDescription
  {
      public static string CategoryName(ProcessCategory category);
      public static string Build(ClassifiedProcess item, ProcessSnapshot snapshot);   // osat " · " -erotettuna
  }
  ```

  `ProcessRuleLoader` käyttää samoja `JsonSerializerOptions`-asetuksia kuin `src/Cleaner.Core/Rules/RuleLoader.cs`
  (PropertyNameCaseInsensitive, ReadCommentHandling.Skip, AllowTrailingCommas, JsonStringEnumConverter). Validointi:
  `Id` ei saa olla tyhjä eikä toistua, `Name` ei tyhjä, `Match` vähintään yksi ei-tyhjä kuvio. Virhe →
  `InvalidDataException`, jonka viesti nimeää lähteen ja säännön.

  `ProcessGuard.Protects` palauttaa syyn, jos jokin pätee: `info.Pid == snapshot.OwnPid` tai `== snapshot.OwnParentPid`
  ("Oma prosessi"); `Pid <= 4` ("Järjestelmäprosessi"); `SessionId != snapshot.CurrentSessionId` ("Toinen istunto tai palvelu");
  `ExePath` null tai `StartFileTime == 0` ("Tietoja ei voitu lukea"); `ExePath` alkaa polulla
  `Environment.GetFolderPath(Environment.SpecialFolder.Windows)` + kansioerotin, kirjainkoko ei merkitse ("Windows-kansion prosessi");
  `IsCritical` ("Kriittinen prosessi"); `!CanTerminate` ("Ei oikeuksia lopettaa"). Muuten null. Windows-kansion polku luetaan kerran staattiseen kenttään; testattavuuden
  vuoksi lisää sisäinen ylikirjoitus `internal static string WindowsDirectory { get; set; }` ja `InternalsVisibleTo("Cleaner.Tests")`
  csproj-tiedostoon.

  `ProcessClassifier.Classify`:
  1. Elossa olevien PID-lukujen joukko kootaan koko näytteestä, myös suojatuista (orpotarkistus tarvitsee ne).
  2. Suojatut (`ProcessGuard.Protects != null`) pudotetaan pois ehdokkaista.
  3. Sulautus: ehdokas X sulautuu vanhempaansa P, jos P on ehdokas (ei suojattu, elossa), `P.ExePath` on sama kuin
     `X.ExePath` (OrdinalIgnoreCase) ja `P.StartFileTime <= X.StartFileTime`. Seuraa ketjua ylimpään tällaiseen esivanhempaan;
     se on juuri. Suojaa syklit (PID-kierrätys) käyntimerkinnällä. Juuren `Children` = kaikki siihen sulautuneet, PID-järjestyksessä.
  4. Orpo: juuren `ParentPid` ei ole elossa olevien joukossa, tai vanhempi on elossa mutta `parent.StartFileTime > root.StartFileTime`.
     `Parent` kentäksi elossa oleva vanhempi (myös suojattu), kun se on oikea vanhempi; muuten null.
  5. Kategoria, ensimmäinen osuva voittaa: ryhmällä näkyvä ikkuna → Windowed (High, Preselected false; `Rule` täytetään
     silti sääntöosumalla kuvausta varten); `Root.ExeMissing` → MissingExe (Medium, true); orpo → Orphan (Medium,
     Preselected = ryhmän IsIdle); sääntöosuma `rules.FirstOrDefault(r => r.Matches(Root.Name))` → Known (säännön Risk,
     false); ryhmän IsIdle → IdleBackground (Medium, false); muuten ActiveBackground (Medium, false).
  Sulautus (vaihe 3) toteutetaan samalla ehdolla kuin `CollectSameExeDescendants`, joka on julkinen apufunktio
  `CleanAsync`-toteutusta varten: se palauttaa pid:n saman exen jälkeläiset transitiivisesti syvyysjärjestyksessä
  (ensin lapset, sitten lapsenlapset) suodattamatta suojattuja.
  6. Tulos järjestetään kategorian (enum-järjestys) ja sitten `TotalPrivateBytes` laskevasti.

  `ProcessDescription.CategoryName`: MissingExe "Ohjelmatiedosto puuttuu levyltä", Orphan "Orvot apuprosessit",
  Known "Tunnetut taustaprosessit", Windowed "Avoimet ohjelmat", IdleBackground "Taustaprosessit, ei käyttöä",
  ActiveBackground "Taustaprosessit, aktiivisia".
  `ProcessDescription.Build` kokoaa osat järjestyksessä, tyhjät pois, erotin " · ": (a) syy: MissingExe
  "Ohjelmatiedostoa ei löydy"; Orphan "Käynnistäjä (PID <ParentPid>) on päättynyt"; Known säännön `Name` ja, jos
  `Description` ei ole tyhjä, ": <Description>"; Windowed "Näkyvä ikkuna" ja perään, jos `Root.ExeMissing`,
  "ohjelmatiedostoa ei löydy", ja jos `Rule` ei ole null, säännön `Name`; muut tyhjä. (b) jos `snapshot.SampleInterval > 0`:
  ryhmän IsIdle → "ei suorittimen käyttöä <N> s aikana" (N = kokonaissekunnit, pyöristys ylös), muuten "käyttää suoritinta".
  (c) "käynnistetty <ikä>" kun `Root.StartFileTime > 0`: ikä = `snapshot.TakenAt - DateTime.FromFileTime(StartFileTime)`,
  muoto "N min" (< 60 min), "N h M min" (< 24 h), "N d H h". (d) "N aliprosessia" kun Children.Count > 0.
  (e) "käynnistäjä <Parent.Name> (PID n)" kun `Parent` ei ole null.

  `known.json` (kommentti alkuun suomeksi, mihin tiedosto on ja ettei se poista mitään), säännöt: `google-crash-handler`
  ("Google Crash Handler", match `GoogleCrashHandler.exe`, `GoogleCrashHandler64.exe`, kuvaus: Googlen päivittäjän
  kaatumisraportoija, pyörii Chromen ja Driven sulkemisen jälkeen); `adobe-ipc-broker` ("Adobe IPC Broker", `AdobeIPCBroker.exe`);
  `adobe-ccx-process` ("Adobe Creative Cloud Experience", `CCXProcess.exe`); `adobe-collab-sync` ("Adobe Collab Sync",
  `AdobeCollabSync.exe`); `steam-webhelper` ("Steam Web Helper", `steamwebhelper.exe`, Low); `discord-helper`
  ("Discord", `Discord*.exe`, Low). Risk Medium ellei mainittu.

  Testit (`namespace Cleaner.Tests`, xunit, snake_case-nimet kuten `tests/Cleaner.Tests/StartupModuleTests.cs`):
  `ProcessRuleLoaderTests`: toimitettu tiedosto latautuu polusta `Path.Combine(AppContext.BaseDirectory, "rules", "processes", "known.json")`
  ja sisältää vähintään kuusi sääntöä; toistuva id → InvalidDataException; tyhjä match → InvalidDataException;
  `Matches` jokerit ja .exe-päätteen puuttuminen (Theory). `ProcessClassifierTests` rakentaa `ProcessSnapshot`-olioita käsin
  (apufunktio `Info(pid, ppid, name, exe, start, ...)`), asettaa `ProcessGuard.WindowsDirectory = @"C:\Windows"` ja testaa:
  suojaus kaikille seitsemälle syylle (myös `CanTerminate = false`); sulautus kolmen tason ketjussa (summa, lasten määrä,
  yksi rivi) ja `CollectSameExeDescendants` samalle ketjulle (kaksi jälkeläistä, lapsi ennen lapsenlasta); eri exe ei sulaudu
  (EDGE-8); sama exe ilman vanhempi-lapsi-suhdetta on kaksi juurta (EDGE-10); orpo kuolleella vanhemmalla; orpo kierrätetyllä
  PID:llä (vanhemman StartFileTime suurempi); kategorioiden etusija (ikkuna voittaa puuttuvan exen, orvon ja säännön ja
  antaa High ilman esivalintaa; ExeMissing voittaa säännön; orpo voittaa säännön; sääntö voittaa joutilaan); Preselected
  vain MissingExe-ryhmälle ja joutilaalle orvolle; ikkunallisen ryhmän kuvaus sisältää säännön nimen; ProcessItemId round trip
  ja virheelliset syötteet; ProcessDescription.Build-osien järjestys yhdessä esimerkissä.

## Wave 2

Yksi tehtävä: T2 lisää Win32-inventaarion ja lopetuksen samaan projektiin, jota T1 loi.

### T2: Inventaario ja lopetus (Win32)
- Status: done cf807cc
- Model: opus
- Requirements: REQ-2, REQ-3, REQ-16
- Constraints/edge: CON-2, CON-3, CON-5, EDGE-1, EDGE-7
- Files: src/Cleaner.Modules.Processes/ProcessInventory.cs,
  src/Cleaner.Modules.Processes/ProcessTerminator.cs,
  tests/Cleaner.Tests/ProcessInventoryTests.cs
- Verify: `dotnet build PcCleaner.sln -warnaserror` → "0 Warning(s)", "0 Error(s)";
  `dotnet test PcCleaner.sln --no-build --filter "FullyQualifiedName~ProcessInventoryTests"` → "Passed!", 0 failed
- Task:
  Projektissa `src/Cleaner.Modules.Processes` on rajapinnat `IProcessInventory` ja `IProcessTerminator` sekä recordit
  `ProcessInfo` ja `ProcessSnapshot` (lue ne tiedostoista ennen aloitusta; niiden muotoa ei muuteta). Toteuta
  `[SupportedOSPlatform("windows")] public sealed class ProcessInventory : IProcessInventory` ja
  `[SupportedOSPlatform("windows")] public sealed class ProcessTerminator : IProcessTerminator`.

  `ProcessInventory.Capture(TimeSpan sampleInterval, CancellationToken ct)`:
  1. Toolhelp32-snapshot (`CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS = 0x2, 0)`, `Process32FirstW`/`Process32NextW`,
     `PROCESSENTRY32W` jossa `dwSize` asetetaan `Marshal.SizeOf`-arvoon ja `szExeFile` on `ByValTStr SizeConst = 260`) antaa
     jokaiselle prosessille PID, PPID ja nimen (ilman `.exe`-päätettä, kuten `Process.ProcessName`). Kahva suljetaan `CloseHandle`-kutsulla.
  2. Jokaiselle PID:lle luetaan `Process.GetProcessById`-oliolla `SessionId`, `PrivateMemorySize64`, `StartTime.ToFileTime()`,
     `TotalProcessorTime`, `MainWindowHandle != IntPtr.Zero`. Jokainen luku on omassa try-lohkossaan: `Win32Exception`,
     `InvalidOperationException` ja `ArgumentException` (prosessi kadonnut) antavat puuttuvan arvon (`StartFileTime = 0`,
     `CpuTime = null`, `HasVisibleWindow = false`, `PrivateBytes = 0`). Jos prosessi on kadonnut jo `GetProcessById`-kutsussa,
     se jätetään pois.
  3. Exe-polku: `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, false, pid)` ja `QueryFullProcessImageNameW(h, 0, sb, ref size)`
     (puskuri 1024 merkkiä); epäonnistuminen → `ExePath = null`. Samalla kahvalla `IsProcessCritical(h, out critical)`; epäonnistuminen → false.
     Kahva suljetaan aina. `ExeMissing = ExePath != null && !File.Exists(ExePath)`. `CanTerminate`: erillinen
     `OpenProcess(PROCESS_TERMINATE = 0x0001, false, pid)` onnistuu (kahva ei ole `IntPtr.Zero`); kahva suljetaan heti.
  4. Jos `sampleInterval > TimeSpan.Zero`: `Thread.Sleep` tai `ct.WaitHandle.WaitOne(sampleInterval)` (peruutus heittää
     `OperationCanceledException`), sitten jokaiselle prosessille, jonka ensimmäinen `CpuTime` ei ole null, luetaan
     `TotalProcessorTime` uudelleen; `IsIdle = (toinen onnistui && toinen == ensimmäinen)`. Prosessi, joka on kadonnut toisen
     näytteen hetkellä, pudotetaan pois. `sampleInterval == 0` → `IsIdle = false` kaikilla, ei odotusta.
  5. Palauta `ProcessSnapshot(list, Process.GetCurrentProcess().SessionId, Environment.ProcessId, ownParentPid, DateTimeOffset.Now, sampleInterval)`,
     jossa `ownParentPid` on Toolhelp-listasta oman PID:n PPID (0 jos ei löydy).
  P/Invoke-määrittelyt luokan sisään `#region P/Invoke`, `[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]`,
  `private static extern`, kuten `src/Cleaner.Modules.Files/RecycleBinModule.cs`. Ei NuGet-paketteja, ei `LibraryImport`.

  `ProcessTerminator.TryTerminate(int pid, long startFileTime, out string reason)`:
  `Process.GetProcessById(pid)`; `ArgumentException` → reason "Prosessi on jo päättynyt", return false.
  `StartTime.ToFileTime() != startFileTime` → "PID on käytetty uudelleen", false (lukuvirhe `Win32Exception` → "Ei oikeuksia", false).
  Jos `MainWindowHandle != IntPtr.Zero` ja `CloseMainWindow()` palauttaa true → `WaitForExit(5000)`. Jos prosessi on yhä elossa
  (`HasExited == false`) → `Kill()` ja `WaitForExit(5000)`. Lopuksi `HasExited` → true ja reason ""; muuten reason
  "Prosessi ei päättynyt 10 s kuluessa", false. `Win32Exception` → "Ei oikeuksia"; `InvalidOperationException` (päättyi kesken) →
  true, koska prosessi on poissa; muu poikkeus → `ex.Message`, false. `Process`-olio hävitetään aina (`using`).

  Testit `tests/Cleaner.Tests/ProcessInventoryTests.cs` (`if (!OperatingSystem.IsWindows()) return;` alkuun):
  `Capture_includes_current_process_with_exe_path_and_session`: `Capture(TimeSpan.Zero, default)` sisältää oman PID:n,
  jolla `ExePath` on `Environment.ProcessPath` (OrdinalIgnoreCase), `SessionId == CurrentSessionId`, `StartFileTime > 0`,
  `CanTerminate == true`, `IsIdle == false`, ja `OwnPid == Environment.ProcessId`; PID 4 (System) löytyy ja sillä `CanTerminate == false`. `Capture_with_interval_marks_sleeping_process_idle`: käynnistä
  `Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })`,
  odota 500 ms, `Capture(TimeSpan.FromMilliseconds(300))`, prosessin rivi löytyy, `ParentPid == Environment.ProcessId`,
  `CpuTime` ei null; lopuksi `Kill()` finally-lohkossa. (Joutilaisuutta ei väitetä varmaksi, koska ping voi ajoittaa
  CPU:ta; väitä vain, että rivi ja CPU-aika löytyvät.) `TryTerminate_rejects_wrong_start_time`: käynnistä sama cmd, kutsu
  `TryTerminate(pid, startFileTime + 1, out reason)` → false, reason "PID on käytetty uudelleen", prosessi yhä elossa; sitten
  `TryTerminate(pid, oikea, out _)` → true ja `HasExited`. `TryTerminate_reports_already_exited`: käynnistä `cmd.exe /c exit`,
  odota päättymistä, `TryTerminate(pid, 1, out reason)` → false, reason "Prosessi on jo päättynyt".

## Wave 3

Yksi tehtävä: T3 kokoaa moduulin T1:n ja T2:n päälle.

### T3: ProcessesModule ja integraatiotestit
- Status: done efa99a9
- Model: opus
- Requirements: REQ-1, REQ-9, REQ-12, REQ-13, REQ-14, REQ-15, REQ-17, REQ-18
- Constraints/edge: CON-2, CON-6, EDGE-3, EDGE-4, EDGE-5
- Files: src/Cleaner.Modules.Processes/ProcessesModule.cs,
  tests/Cleaner.Tests/ProcessesModuleTests.cs
- Verify: `dotnet build PcCleaner.sln -warnaserror` → "0 Warning(s)", "0 Error(s)";
  `dotnet test PcCleaner.sln --no-build --filter "FullyQualifiedName~ProcessesModuleTests"` → "Passed!", 0 failed
- Task:
  Projektissa `src/Cleaner.Modules.Processes` ovat valmiina: `IProcessInventory` + `ProcessInventory`, `IProcessTerminator`
  + `ProcessTerminator`, `ProcessSnapshot`, `ProcessInfo`, `ProcessClassifier.Classify(snapshot, rules)`,
  `ProcessDescription.CategoryName/Build`, `ProcessGuard.Protects(info, snapshot)`, `ProcessRuleLoader`, `ProcessItemId`.
  Lue ne ennen aloitusta. Toteuta `[SupportedOSPlatform("windows")] public sealed class ProcessesModule : ICleanerModule`
  (`Cleaner.Core.Abstractions`, mallit `Cleaner.Core.Models`, loki `Cleaner.Core.Logging.ICleanerLogger`). Malli rakenteelle:
  `src/Cleaner.Modules.Startup/StartupModule.cs` (Task.Run-kääreet, Report-kutsut, Skipped-syyt, DryRun-haara, loppuyhteenveto).

  Metatiedot: `Id => "processes"`, `DisplayName => "Prosessit"`, `Description => "Listaa käyttäjän istunnon taustaprosessit ja lopettaa valitut. Ei poista mitään levyltä."`,
  `Risk => RiskLevel.High`, `RequiresAdmin => false`, `SelectedByDefault => false`.
  Konstruktorit: `public ProcessesModule(string? rulesFile = null, IProcessInventory? inventory = null, IProcessTerminator? terminator = null, TimeSpan? sampleInterval = null)`;
  oletukset: `rulesFile` → `Path.Combine(AppContext.BaseDirectory, "rules", "processes", "known.json")`, `new ProcessInventory()`,
  `new ProcessTerminator()`, 3 s. Julkinen `TimeSpan SampleInterval { get; }`.

  `ScanAsync` → `Task.Run(() => Scan(context, ct), ct)`. `Scan`: `Report(0, "Luetaan prosessit")`; säännöt ladataan
  `ProcessRuleLoader.LoadFromFile`-kutsulla, jos tiedosto on; puuttuva tiedosto → `Logger.Warning($"Sääntötiedostoa ei löydy: {polku}")`
  ja tyhjä lista; `InvalidDataException`/`JsonException`/`IOException` → `Logger.Warning($"Sääntötiedosto on virheellinen: {polku}: {ex.Message}")`,
  `result.Errors.Add(sama teksti)` ja tyhjä lista.
  `Report(10, $"Mitataan suorittimen käyttöä ({SampleInterval.TotalSeconds:0} s)")`; `snapshot = inventory.Capture(SampleInterval, ct)`;
  `Report(80, "Luokitellaan")`; `ProcessClassifier.Classify`; jokaisesta `ClassifiedProcess c` yksi
  `new CleanItem(Id: ProcessItemId.Make(c.Root.Pid, c.Root.StartFileTime), ModuleId: Id, Category: ProcessDescription.CategoryName(c.Category), Path: c.Root.ExePath!, SizeBytes: c.TotalPrivateBytes, Description: ProcessDescription.Build(c, snapshot), Risk: c.Risk, Preselected: c.Preselected)`.
  `Logger.Debug` suojattujen määrästä (`snapshot.Processes.Count - listattujen ryhmien jäsenmäärä`); `Report(100, "Valmis")`;
  `Logger.Info($"{DisplayName}: löytyi {result.Count} prosessiryhmää")`.

  `CleanAsync` → `Task.Run(() => Clean(items, context, ct), ct)`. `Clean` käy kohteet läpi (`ct.ThrowIfCancellationRequested()`,
  `Report(prosentti, item.Path)`):
  1. `item.ModuleId != Id` (OrdinalIgnoreCase) → Skipped "Kohde ei kuulu tälle moduulille".
  2. `!ProcessItemId.TryParse(item.Id, out pid, out start)` → Skipped "Virheellinen tunniste".
  3. Tuore tieto: `basic = inventory.Capture(TimeSpan.Zero, ct)` otetaan kerran koko `Clean`-kutsulle ja siitä
     `info = basic.Processes.FirstOrDefault(p => p.Pid == pid)`. `info == null` → Skipped "Prosessi on jo päättynyt".
     `info.StartFileTime != start` → Skipped "PID on käytetty uudelleen". `ProcessGuard.Protects(info, basic)` → Skipped sillä syyllä.
  4. `context.DryRun` → `Logger.Debug($"[KUIVA] Lopetettaisiin {info.Name} (PID {pid}) {info.ExePath}")`, `Removed.Add(item)`, continue.
  5. `terminator.TryTerminate(pid, start, out reason)`: true → `Removed.Add(item)` ja
     `Logger.Info($"Lopetettiin {info.Name} (PID {pid}): {info.ExePath}")`; false → Skipped reason ja `Logger.Warning($"Ohitettu {item.Path}: {reason}")`.
  6. Onnistuneen lopetuksen jälkeen jälkeläiset: `ProcessClassifier.CollectSameExeDescendants(basic, pid)` (transitiivinen,
     sama exe, lapset ennen lapsenlapsia); jokaiselle `TryTerminate(child.Pid, child.StartFileTime, out r)`; onnistuminen →
     `Logger.Info("Lopetettiin aliprosessi ...")`, epäonnistuminen (myös "Prosessi on jo päättynyt", kun vanhemman kuolema vei
     lapsen mukanaan) → `Logger.Warning` ilman vaikutusta juuren tulokseen. Jälkeläisiä ei käsitellä kuivassa ajossa.
  Jokainen Skipped-syy kirjataan `Logger.Warning`-rivillä. Lopuksi `Report(100, "Valmis")` ja Info-yhteenveto
  (`context.DryRun ? "lopetettaisiin" : "lopetettiin"`, määrä, ohitettujen määrä).

  Testit `tests/Cleaner.Tests/ProcessesModuleTests.cs`, `[SupportedOSPlatform("windows")] public sealed class ProcessesModuleTests : IDisposable`,
  jokainen testi alkaa `if (!OperatingSystem.IsWindows()) return;`. Apuprosessi: konstruktori kopioi
  `Path.Combine(Environment.SystemDirectory, "cmd.exe")` kansioon `Path.Combine(Path.GetTempPath(), "pccleaner-tests", Guid.NewGuid().ToString("N"))`
  nimellä `pcc-test-cmd.exe` ja käynnistää sen argumentilla `/c ping -n 60 127.0.0.1 >nul` (`CreateNoWindow = true`, `UseShellExecute = false`);
  kopio on tarpeen, koska Windows-kansion prosessit ovat suojattuja eikä niitä listata. `Dispose` tappaa prosessin, jos se on
  elossa, ja poistaa kansion (yritä uudelleen 3 kertaa 200 ms välein, koska exe voi olla hetken lukittu). Moduuli luodaan
  `new ProcessesModule(rulesFile: Path.Combine(tempDir, "missing.json"), sampleInterval: TimeSpan.FromMilliseconds(200))`;
  polun on oltava ei-null ja olematon, koska null-arvo osoittaisi testitulosteen `rules\processes\known.json`-tiedostoon,
  joka on olemassa.
  Testit: `Scan_lists_test_process_as_background` (odota 300 ms käynnistyksen jälkeen; `ScanAsync` sisältää kohteen, jonka Id on
  `ProcessItemId.Make(pid, startFileTime)` ja `Path` on kopion polku (OrdinalIgnoreCase), kategoria on jokin "Taustaprosessit, ..."
  -kategorioista, `Preselected == false`, kuvaus sisältää "käynnistäjä"); `Scan_is_idempotent_for_test_process` (kaksi
  peräkkäistä `ScanAsync`-kutsua antavat testiprosessille saman `Id`:n ja `Path`:n, ja prosessi on elossa molempien jälkeen;
  ARCHITECTURE luku 8 vaatii tämän testin jokaiselta moduulilta); `Scan_does_not_list_windows_directory_processes`
  (mikään `Path` ei ala `Environment.GetFolderPath(SpecialFolder.Windows)`); `Scan_warns_when_rules_file_missing` (TestLogger sisältää Warning-rivin,
  `Errors` tyhjä); `DryRun_keeps_process_alive_but_reports_removed` (DryRun true → Removed 1, prosessi elossa);
  `Apply_terminates_process` (DryRun false → Removed 1, `WaitForExit(5000)` → `HasExited`, TestLogger sisältää Info-rivin "Lopetettiin");
  `Apply_skips_reused_pid` (Id väärällä startFileTime → Skipped syy "PID on käytetty uudelleen", prosessi elossa);
  `Clean_skips_foreign_module_and_bad_id` (kaksi kohdetta, syyt "Kohde ei kuulu tälle moduulille" ja "Virheellinen tunniste");
  `Clean_skips_protected_process` (kohde oman testiprosessin PID:llä `Environment.ProcessId` ja oikealla startFileTime → Skipped "Oma prosessi").

## Wave 4

T4 ja T5 ovat riippumattomia (eri tiedostot; T5 ei käännä mitään) ja ajetaan rinnakkain.

### T4: CLI- ja UI-kytkentä
- Status: pending
- Model: opus
- Requirements: REQ-19, REQ-20
- Constraints/edge: CON-1
- Files: src/Cleaner.Cli/ModuleFactory.cs, src/Cleaner.Cli/Cleaner.Cli.csproj, src/Cleaner.Cli/Program.cs,
  src/Cleaner.UI/Services/UiModuleFactory.cs, src/Cleaner.UI/Cleaner.UI.csproj,
  src/Cleaner.UI/ViewModels/ModuleViewModel.cs, src/Cleaner.UI/ViewModels/MainViewModel.cs
- Verify: `dotnet build PcCleaner.sln -warnaserror` → "0 Warning(s)", "0 Error(s)";
  `dotnet run --project src/Cleaner.Cli -- modules` → rivi, jossa "processes" ja "vain --module";
  `dotnet run --project src/Cleaner.Cli -- scan --module processes` → "== Prosessit ==" ja vähintään yksi kategoria, poistumiskoodi 0
  Manual (human): UI:ssa "Prosessit" näkyy navigoinnissa omalla kuvakkeella; Puhdista kysyy "Lopetetaanko N prosessia" ja painike on "Lopeta prosessit".
- Task:
  Kytke uusi moduuli `Cleaner.Modules.Processes.ProcessesModule` (konstruktori `ProcessesModule(string? rulesFile = null, ...)`,
  `Id = "processes"`, `SelectedByDefault = false`, lopettaa prosesseja, ei poista tiedostoja) komentoriviin ja WinUI-sovellukseen.
  Moduuli on jo olemassa ja testattu; tämä tehtävä ei muuta sitä.

  CLI: `src/Cleaner.Cli/Cleaner.Cli.csproj` saa `ProjectReference`-rivin `..\Cleaner.Modules.Processes\Cleaner.Modules.Processes.csproj`
  muiden moduulien viereen. `src/Cleaner.Cli/ModuleFactory.cs`: `using Cleaner.Modules.Processes;` ja Windows-lohkoon
  (`RuntimeInformation.IsOSPlatform(OSPlatform.Windows)`, jossa Startup, Registry ja Uninstall lisätään)
  `modules.Add(new ProcessesModule(Path.Combine(rulesDirectory, "processes", "known.json")));`. `src/Cleaner.Cli/Program.cs`:
  ohjetekstissä rivi "Moduulit 'startup', 'diskusage', 'registry' ja 'uninstall' ajetaan vain, kun ne annetaan --module-valinnalla."
  → lisää 'processes' listaan; lisää selitysrivi "'processes' listaa käyttäjän istunnon taustaprosessit ja lopettaa valitut; mitään ei poisteta levyltä."
  muiden moduuliselitysten jälkeen; `--apply`-vahvistuksen teksti `"... Tiedostot poistetaan, käynnistysohjelmat poistetaan käytöstä. [k/E] "`
  → `"... Tiedostot poistetaan, käynnistysohjelmat poistetaan käytöstä, prosessit lopetetaan. [k/E] "`. Lisää `--item`-esimerkin
  alle toinen esimerkkirivi `--module processes --item "processes:<pid>:<käynnistysaika>"` (tarkka tunniste skannauksen
  tulosteesta; pelkkä etuliite osuu kaikkiin).

  UI: `src/Cleaner.UI/Cleaner.UI.csproj` saa saman `ProjectReference`-rivin (polku `..\Cleaner.Modules.Processes\...`).
  `src/Cleaner.UI/Services/UiModuleFactory.cs`: `using Cleaner.Modules.Processes;` ja `modules.Add(new ProcessesModule(Path.Combine(rulesDirectory, "processes", "known.json")));`
  Uninstall-rivin jälkeen (lista on navigointijärjestys). `src/Cleaner.UI/ViewModels/ModuleViewModel.cs`: `Glyph`-switchiin haara
  `"processes" => "\uE9D9"` (Segoe Fluent Icons; mikä tahansa muu kuin muilla moduuleilla käytetty, ei-tyhjä merkki käy).
  `src/Cleaner.UI/ViewModels/MainViewModel.cs`, metodi `CleanAsync`: lisää `var isProcesses = module.Id == "processes";`;
  `verb` prosesseille "lopetetaan"; toinen vahvistus (`confirmText`) prosesseille
  `$"Lopetetaanko {dryRemoved} prosessia" + (dryBytes > 0 ? $" ({ByteFormatter.Format(dryBytes)})" : "") + "?\n\nTallentamattomat tiedot katoavat, jos ohjelma on auki. Mitään ei poisteta levyltä. Ohjelman voi käynnistää uudelleen tavallisesti."`,
  `confirmButton` "Lopeta prosessit"; tulosteksti (`resultText`) prosesseille `", vapautettu muistia X"` muodon `", vapautettu X"` sijaan.
  Muut moduulit säilyttävät nykyiset tekstinsä täsmälleen.

### T5: Dokumentit
- Status: pending
- Model: opus
- Requirements: REQ-22
- Files: docs/ARCHITECTURE.md, README.md, HANDOFF.md
- Verify: `grep -c "processes" docs/ARCHITECTURE.md README.md HANDOFF.md` → jokaisessa vähintään 1;
  `grep -n "### 6.5" docs/ARCHITECTURE.md` → yksi rivi
- Task:
  Dokumentoi uusi moduuli `processes` ("Prosessit", projekti `src/Cleaner.Modules.Processes`) kolmeen tiedostoon samalla
  tyylillä kuin olemassa olevat moduulit. Tyyli: suomi, ei ajatusviivoja, konkreettiset nimet. Älä muuta muiden moduulien tekstiä.

  Mitä moduuli tekee: skannaus listaa käyttäjän istunnon prosessit, jotka eivät ole Windows-kansiossa; suojatut
  (oma prosessi ja sen vanhempi, PID 0 ja 4, toinen istunto tai palvelu, lukukelvoton polku tai käynnistysaika,
  Windows-kansion exe, kriittinen prosessi, prosessi jota ei saa avata lopetusoikeudella) jätetään kokonaan pois. Saman exen
  lapsiprosessit sulautetaan elossa olevan vanhemman riviin (muisti summataan, kuvaus kertoo "N aliprosessia"). Kategoriat
  etusijajärjestyksessä: "Avoimet ohjelmat" (ryhmällä näkyvä ikkuna, High, ei koskaan esivalittu; puuttuva exe tai
  sääntöosuma näkyy vain kuvauksessa), "Ohjelmatiedosto puuttuu levyltä" (Medium, esivalittu), "Orvot apuprosessit"
  (vanhempi kuollut tai PID kierrätetty; Medium, esivalittu vain jos joutilas), "Tunnetut taustaprosessit" (sääntötiedosto
  `rules/processes/known.json`: id, name, match-jokerit, description, risk; säännön riski, oletus Medium),
  "Taustaprosessit, ei käyttöä" (suoritinaika ei muuttunut 3 s näytteiden välillä), "Taustaprosessit, aktiivisia".
  `CleanItem.Id = processes:<pid>:<startFileTime>`, `Path` on exe-polku, `SizeBytes` on yksityinen muisti, `Description`
  kertoo syyn, suorittimen käytön, iän, aliprosessien määrän ja käynnistäjän. Lopetus: tunnisteen käynnistysaika
  tarkistetaan (PID-kierrätys → ohitus "PID on käytetty uudelleen"), suojaus tarkistetaan uudelleen, ikkunalliselle
  `CloseMainWindow` ja 5 s odotus, sitten `Kill` ja 5 s; saman exen jälkeläiset lopetetaan perään. Kuiva ajo ei lopeta mitään.
  Jokainen lopetus kirjataan lokiin nimellä, PID:llä ja polulla. `SelectedByDefault = false`, `Risk = High`
  (korkein kohdetaso), `RequiresAdmin = false`. Palvelut ja linkitys käynnistysohjelmiin eivät kuulu moduuliin (vaihe 2).
  Sääntötiedosto on alikansiossa, koska `Shipped_rule_files_are_valid` lukee `rules\*.json`-juuren tiedostosääntöinä.

  `docs/ARCHITECTURE.md`: luvun 1 taulukkoon rivi `| 9 | Prosessit | processes | valmis | High (kohteet Medium, avoimet ohjelmat High) | 6.5 |`;
  luvun 2 kaavion moduulilistaan "Processes"; uusi luku "### 6.5 Prosessit (`processes`) – toteutettu" luvun 6.4 jälkeen
  samalla rakenteella kuin 6.1 (projekti, kategoriataulukko sarakkeilla Kategoria | Ehto | Riski | Esivalittu, suojaukset,
  sulautus, tunniste, lopetus, turvamekanismit). Päivitä otsikon versio ja päiväys (0.3 · 20.10.2026).
  `README.md`: rakennepuuhun rivi `Cleaner.Modules.Processes/  Prosessit: käyttäjän istunnon taustaprosessien luokittelu ja lopetus (rules/processes/known.json)`;
  CLI-esimerkkeihin `dotnet run -- scan --module processes` ("käyttäjän istunnon taustaprosessit, luokiteltuina") ja
  `dotnet run -- clean --module processes --item "processes:<pid>:<käynnistysaika>" --apply` ("lopeta yksi prosessi, kysyy
  vahvistuksen"); oma kappale moduulista levytilan analyysin kappaleen jälkeen (3-6 riviä), jossa varoitus: CLI:n `clean` ei
  suodata esivalinnan tai riskin mukaan, joten `--apply` ilman tarkkaa `--item`-tunnistetta (tai pelkällä etuliitteellä
  `"processes:"`) lopettaa kaikki listatut prosessit, myös avoimet ohjelmat; turvallisuusperiaatteisiin ei lisäyksiä.
  `HANDOFF.md`: taulukkoon rivi `src/Cleaner.Modules.Processes` luokkaluettelolla (`ProcessesModule`, `ProcessInventory`,
  `ProcessClassifier`, `ProcessGuard`, `ProcessTerminator`, `ProcessRuleLoader`, `ProcessItemId`, `ProcessDescription`) ja
  tilalla "valmis, yksikkö- ja integraatiotestit"; päivitä päivämäärä otsikon alla.

## Final verification
- AC-1 [auto]: `dotnet build PcCleaner.sln -warnaserror` → "0 Warning(s)", "0 Error(s)".
- AC-2 [auto]: `dotnet test PcCleaner.sln --no-build` → "Passed!", 0 failed (159 testiä ennen tätä haaraa plus uudet Process*-testit).
- AC-3 [auto]: `dotnet run --project src/Cleaner.Cli -- scan --module processes` → tulosteessa "== Prosessit ==" ja vähintään
  yksi `processes:`-tunniste, poistumiskoodi 0. (Windows-kansion suojaus varmistetaan testillä
  `Scan_does_not_list_windows_directory_processes`, koska CLI ei tulosta polkua.)
- AC-4 [auto]: `dotnet run --project src/Cleaner.Cli -- clean --module processes --item "processes:"` (kuiva ajo, ei --apply) →
  tulosteessa "KUIVA AJO" ja "Vapautuisi:", poistumiskoodi 0, ja uusin tiedosto kansiossa `%LOCALAPPDATA%\PcCleaner\logs\`
  ei sisällä riviä "Lopetettiin".
- AC-5 [manual]: WinUI-sovellus: "Prosessit" näkyy navigoinnissa, skannaus näyttää edistymisen "Mitataan suorittimen käyttöä",
  kategoriat ovat suomeksi, "Avoimet ohjelmat" on korkean riskin lohkossa suljettuna, mikään ikkunallinen ohjelma ei ole rastittuna.
- AC-6 [manual]: WinUI-sovellus: käynnistä Muistio (notepad.exe on Windows-kansiossa, joten se ei näy; käytä sen sijaan
  esim. Brave tai mikä tahansa Program Files -ohjelma), valitse sen rivi "Avoimet ohjelmat" -lohkosta, Puhdista → kuivan ajon
  yhteenveto "Kuivassa ajossa lopetetaan 1 kohdetta", vahvistus "Lopetetaanko 1 prosessia", painike "Lopeta prosessit" → ohjelma sulkeutuu,
  loki `%LOCALAPPDATA%\PcCleaner\logs\` sisältää rivin "Lopetettiin <nimi> (PID n): <polku>".

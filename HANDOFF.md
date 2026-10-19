# Session handoff – PC Cleaner

Päivitetty 19.10.2026 (Windows-ajon jälkeen). Liitä tämä uuden keskustelun alkuun yhdessä PcCleaner.zip:n kanssa.

## Projekti yhdellä lauseella

Oma modulaarinen Windows 11 -siivousohjelma (vrt. CCleaner), C# / .NET 8, omaan käyttöön.
Käyttöliittymä on WinUI 3 (ensimmäinen versio valmis), lisäksi CLI. Käyttäjä hallitsee C#:n perusteet.

## Tila: mikä on valmista

Solution `PcCleaner.sln` kääntyy 0 virhettä / 0 varoitusta. Käyttäjän koneella SDK 10.0.402, target net8.0.

| Projekti | Sisältö | Tila |
|---|---|---|
| `src/Cleaner.Core` | `ICleanerModule`, mallit (`CleanItem`, `ScanResult`, `CleanResult`, `CleanerContext`, `RiskLevel`), `CleanRule` + `RuleLoader` (JSON), `CleanerEngine`, palvelut `PathGuard`, `SafeFileDeleter`, `PathExpander`, `ProcessChecker`, `ElevationHelper`, `ByteFormatter`, lokitus (`ICleanerLogger`, `FileLogger`, `CompositeLogger`) | valmis |
| `src/Cleaner.Modules.Files` | `FileCleanerModule` (sääntöpohjainen), `RecycleBinModule` (shell32 P/Invoke), `rules/files.json` (9 sääntöä), `rules/browsers.json` (Edge, Chrome, Firefox, Brave) | valmis |
| `src/Cleaner.Modules.Startup` | `StartupModule` (Run-avaimet HKCU/HKLM/Wow6432Node, käynnistyskansiot, Task Schedulerin juurikansion kirjautumistehtävät), `StartupSource`, `StartupApproved` (12 tavun arvomuoto), `TaskSchedulerSource` ja `ShortcutResolver` (COM myöhäissidonnaisesti `dynamic`-tyypillä) | valmis, skannattu oikealla koneella 19.10.2026 |
| `src/Cleaner.Modules.DiskUsage` | `DiskUsageModule` (kansiopuu `DirectoryNode`, ylätason kansiot rinnakkain, top-N kansiot ja tiedostot, puu jää `LastTrees`-ominaisuuteen treemapia varten), `DiskUsageOptions` (juuret, top-N, rinnakkaisuus) | valmis, skannattu oikealla levyllä 19.10.2026 |
| `src/Cleaner.Modules.Registry` (nimiavaruus `Cleaner.Modules.RegistryCleaner`) | `RegistryModule` (Uninstall-orvot, .ext/ProgId-orvot, MUICache-orvot, MRU-listat), `RegistryGuard` (sallitut juuret + kielletyt avaimet), `CommandLineParser` (exe komentorivistä, varovainen puuttumistarkistus), `RegistryBackup` (reg export/import, listaus, palautus), `RegistryScanOptions`, `RegistryPaths` | valmis, skannattu oikealla koneella 19.10.2026 |
| `src/Cleaner.Cli` | `pccleaner modules | scan | clean [--apply] [--yes] [--module id] [--item id] [--path dir] [--top n] [--rules dir] [--include-disabled] [-v]`, `pccleaner restore [aikaleima]` (`RestoreCommand`), `ModuleFactory`, `ConsoleLogger` | valmis, ajettu Windowsissa 19.10.2026 |
| `src/Cleaner.UI` | WinUI 3 (Windows App SDK 1.8.260921001, self-contained, pakkaamaton, x64), CommunityToolkit.Mvvm 8.4.2. `MainWindow` (NavigationView + kategoriat Expandereina + alapalkki), `MainViewModel` (skannaus, kolmivaiheinen puhdistus, peruutus, admin-uudelleenkäynnistys), `ModuleViewModel`, `CategoryViewModel`, `ItemViewModel`, `UiModuleFactory`, `UiLogger`, `Elevation`, `IDialogService` | valmis, käynnistetty ja testattu UI Automationilla 19.10.2026 |
| `tests/Cleaner.Tests` | 67 xUnit-testiä (lisäksi FileLogger-siivous, Treemap): PathGuard, RuleLoader, FileCleanerModule, StartupApproved, StartupModule, DiskUsageModule, CommandLineParser, RegistryGuard, RegistryModule (oikea rekisteri avaimessa `HKCU\Software\PcCleanerTests\<guid>`, oikea reg export/import -kierros) | 59/59 läpi Windowsissa |
| `docs/ARCHITECTURE.md` | tekninen määrittely, 9 lukua | valmis |
| `README.md` | käännös- ja käyttöohje | valmis |

### Windows-ajo 19.10.2026 (tavallisena käyttäjänä)

Build 0/0, testit 14/14. `scan` löysi 727 Mt: pikkukuvat 387 Mt, Windows Update -välimuisti 265 Mt,
%TEMP% 62 Mt (14 356 tiedostoa), Windows-lokit 13 Mt. Brave oli auki, joten selainsääntö ohitettiin oikein.

Korjattu samalla:

- `RecycleBinModule`: `SHQUERYRBINFO` oli `Pack = 4` (20 tavua), mutta 64-bittinen shell32 odottaa 24 tavua
  (shellapi.h pakkaa 1 tavuun vain 32-bittisenä). Tulos oli E_INVALIDARG 0x80070057. Pack-määre poistettu, kysely toimii.
- `FileCleanerModule.Clean`: tyhjien kansioiden siivous käsitteli vain poistettujen tiedostojen suorat yläkansiot,
  joten `sub/deep` poistui mutta `sub` jäi. Nyt yläkansiot kävellään säännön juureen asti. Testi
  `Apply_deletes_files_and_empty_subdirs_but_not_root` kaatui tähän ja menee nyt läpi.
- CLI kunnioittaa `enabledByDefault`-kenttää. `--include-disabled` ottaa Prefetchin ja Windows.oldin mukaan.
  `FileCleanerModule.FromRuleFile` sai valinnaisen `filter`-parametrin, oletus suodattaa `enabledByDefault: false` pois.

Ei ajettu adminina (UAC-kehotetta ei voi hyväksyä Claude-sessiosta). `%WINDIR%\Temp`, Prefetch ja
SoftwareDistribution-poisto ovat testaamatta. `--apply` ei ole ajettu kertaakaan oikealla datalla.
Git alustettu 19.10.2026: haara `main`, ensimmäinen commit `cba91ea` (75 tiedostoa, ei bin/obj).
Etärepo: https://github.com/Vilippo/PcCleaner (yksityinen, `origin`, luotu `gh repo create` 19.10.2026).
Tekninen määrittely on `docs/ARCHITECTURE.md`; juuren vanha kopio poistettiin commitissa `5cdfa52`.

### Startup-moduuli 19.10.2026

`scan --module startup` löysi käyttäjän koneelta 11 käytössä olevaa kohdetta: 3 HKCU Run, 4 HKLM Run,
1 käynnistyskansio (.lnk-kohde selvitetty), 3 ajoitettua tehtävää. Kuiva `clean --module startup --item "startup:hkcu-run:"`
rajasi kolmeen ja raportoi oikein. `--apply` ei ole ajettu startup-moduulille oikealla datalla; testit kattavat
StartupApproved-kirjoituksen omassa testiavaimessa.

## Tehdyt suunnittelupäätökset (älä avaa uudelleen ilman syytä)

1. **Kuiva ajo on oletus** – `CleanerContext.DryRun = true`. CLI vaatii `--apply`.
2. **Selaimet eivät ole oma koodimoduuli**, vaan sama `FileCleanerModule` eri sääntötiedostolla. Uusi tiedostopohjainen kohde = uusi JSON-lohko, ei koodia.
3. **Kaikki poistot `SafeFileDeleter`-luokan kautta**, joka kutsuu `PathGuard.IsSafeToDelete`. Moduulit eivät kutsu `File.Delete` suoraan.
4. **Ei ulkoisia NuGet-paketteja Core/Files/Cli-projekteissa.** Vain testiprojekti käyttää xunitia. WinUI tuo omansa (Windows App SDK, CommunityToolkit.Mvvm).
5. **Rekisterin puhdistus tehdään viimeisenä ja rajattuna** (vain Uninstall-orvot, tiedostotyyppikytkökset, MUICache, MRU-listat). Pakollinen `.reg`-varmuuskopio + palautus ennen `--apply`-tukea. Käyttäjä halusi ominaisuuden; Claude suositteli varovaisuutta ja sovittiin rajatusta versiosta.
6. **Käynnistysohjelmat poistetaan käytöstä, ei poisteta** (`StartupApproved`-avain kuten Tehtävienhallinta).
7. ~~Admin-oikeuksia ei pakoteta~~ **Muutettu 19.10.2026 käyttäjän pyynnöstä: UI-exe käynnistyy aina järjestelmänvalvojana** (`app.manifest`: `requestedExecutionLevel requireAdministrator`, UAC-kehote joka käynnistyksellä). CLI (`pccleaner.exe`) toimii edelleen tavallisena käyttäjänä. InfoBar ja "Käynnistä uudelleen järjestelmänvalvojana" jäivät koodiin, mutta eivät näy, koska prosessi on jo korotettu. Seuraukset: Windowsin kansiovalitsin ("Selaa...") ei toimi korotetussa prosessissa, polku kirjoitetaan käsin (virhe näytetään dialogissa); UI Automation -savutestit eivät toimi korottamattomasta PowerShellistä (UIPI), testaus vaatii korotetun PowerShellin tai Debug-buildin ajamisen `dotnet run` -komennolla (sekin pyytää UAC:n).
8. Käyttäjälle näkyvät tekstit suomeksi, koodin nimet englanniksi. C# 12, nullable päällä, `sealed` oletuksena.
9. **`ICleanerModule.SelectedByDefault`** (oletusjäsen, oletus `true`). Startup palauttaa `false`: ilman `--module startup` CLI ei koske käynnistysohjelmiin, koska ne ovat käyttäjän omia valintoja eikä `clean --apply` saa poistaa niitä kaikkia käytöstä. UI:n "skannaa kaikki" noudattaa samaa.
10. **Task Scheduler vain juurikansiosta (`\`)**, ei piilotettuja. Windowsin omat tehtävät ovat `\Microsoft\`-alikansiossa eikä niitä poisteta käytöstä siivouksen nimissä. COM käytetään `dynamic`-tyypillä (`Schedule.Service`, `WScript.Shell`), ei interop-kokoonpanoja eikä NuGetia.
11. **CLI `--item <tunniste>`** rajaa puhdistuksen yksittäisiin kohteisiin (osamerkkijono, kirjainkoko ei merkitse). Tarkoitettu startup- ja diskusage-moduuleille, toimii kaikille.
12. **DiskUsage poistaa vain tiedostoja, ei koskaan kansioita.** Luku 6.2 sanoi "CleanAsync ei tee mitään" mutta myös "käyttäjä voi poistaa suoraan käyttöliittymästä"; ratkaisu: tiedostokohteet menevät `SafeFileDeleter`-luokan kautta, kansiokohteet palautuvat aina `Skipped`-listalle. Suurimman kansion rekursiivinen poisto on juuri se vahinko, jota ei saa tehdä. Kaikki kohteet `Risk = High`, `SelectedByDefault = false`.
13. **DiskUsage ei seuraa reparse pointteja** (junction, symlink): sama data ei tule laskettua kahdesti eikä puu kierrä. Piilotetut ja järjestelmätiedostot lasketaan mukaan.
14. **Windows App SDK 1.8-linja, ei 2.x.** 2.x julkaisuja tulee tiheään (2.0 → 2.5 vuodessa) ja niiden muutoksia ei tunneta; 1.8 on huollettu (viimeisin 1.8.260921001, syyskuu 2026). Päivitys 2.x:ään on oma tehtävänsä.
15. **UI näyttää enintään 500 riviä per kategoria** (`CategoryViewModel.MaxVisibleItems`). Kategoriat ovat Expandereita ScrollViewerissa eivätkä virtualisoi; 14 000 riviä jäädyttäisi ikkunan. Kategorian valintaruutu koskee kaikkia kohteita, myös piilotettuja, ja tämä sanotaan listan alla.
16. **UI lataa kaikki säännöt, myös `enabledByDefault: false`** (Prefetch, Windows.old). Ne päätyvät High-riskin lohkoon, joka on oletuksena kiinni ja valitsematta. CLI sen sijaan ohittaa ne ilman `--include-disabled`.
17. **Oletusvalinta UI:ssa:** kohde on valittu, kun moduulin `SelectedByDefault` on tosi ja riski alle High. Startup-, diskusage- ja registry-kohteet ovat aina aluksi valitsematta.
18. **Rekisteri: varmuuskopio on ehto poistolle.** `RegistryModule.Clean` vie avaimen `reg export` -komennolla ennen muutosta; jos vienti epäonnistuu, kohde ohitetaan. Yksi kansio per ajo: `%LOCALAPPDATA%\PcCleaner\registry-backups\<aikaleima>\NNN-<avain>.reg` + `manifest.txt`. Palautus `pccleaner restore <aikaleima>` tai UI:n nappi "Palauta varmuuskopio..." tuo kansion kaikki tiedostot (`reg import`).
19. **Rekisterivartija on tyyppikohtainen ja vaatii suoran lapsen.** `uninstall`-kohde saa osua vain Uninstall-juurten suoriin lapsiin, `ext`/`progid` vain Classes-juurten suoriin lapsiin, `muicache` ja `mru` vain täsmälleen omaan avaimeensa. Polun segmentit CLSID, Interface, TypeLib, AppID, Services, Drivers, SharedDLLs, Fonts estävät aina. Ensimmäinen versio salli "minkä tahansa sallitun juuren alla" ja testi paljasti, että uninstall-tunnisteella olisi voinut poistaa Classes-avaimen; korjattu.
20. **Orpotulkinta on varovainen.** Uninstall: vain kun `UninstallString` on, sen exe on absoluuttinen ja puuttuu, exe ei ole msiexec, `SystemComponent` ei ole 1 ja `InstallLocation` ei ole olemassa. `.ext`: oletusarvo ilman välilyöntejä (selkonimi "Microsoft Email Message" ei ole ProgId), ProgId puuttuu kaikista Classes-juurista, avaimella ei aliavaimia (OpenWithProgids, PersistentHandler menetettäisiin). ProgId: `shell\open\command` ilman `DelegateExecute`-arvoa, exe absoluuttinen ja puuttuu; pääte-ettömälle komennolle (`perfmon /sys`) kokeillaan `.exe`. Puuttumistarkistus palauttaa "ei puutu", jos polku on suhteellinen, verkkopolku, asemaa ei ole tai polku on `WindowsApps`-kansiossa (ei lukuoikeutta). MRU: ei orpotarkistusta, avaimen sisältö tyhjennetään ja avain säilyy.

## Rajapintasopimus (kopio, jotta uusi moduuli voidaan kirjoittaa ilman zipin avaamista)

```csharp
public interface ICleanerModule
{
    string Id { get; }            // pieniä kirjaimia, ei muutu koskaan
    string DisplayName { get; }
    string Description { get; }
    RiskLevel Risk { get; }       // Low | Medium | High
    bool RequiresAdmin { get; }
    bool SelectedByDefault => true;  // false = mukaan vain kun käyttäjä valitsee moduulin erikseen
    Task<ScanResult>  ScanAsync(CleanerContext ctx, CancellationToken ct = default);   // EI muuta mitään
    Task<CleanResult> CleanAsync(IReadOnlyList<CleanItem> items, CleanerContext ctx, CancellationToken ct = default);
}
public sealed record CleanItem(string Id, string ModuleId, string Category, string Path, long SizeBytes, string Description, RiskLevel Risk);
```

Säännöt: `CleanAsync` käsittelee vain `item.ModuleId == Id`; DryRun-tilassa ei kirjoittavia operaatioita mutta `Removed` täytetään; yksittäisen kohteen virhe → `Skipped`, ei poikkeus; levy-I/O `Task.Run`-kutsussa.

Uuden moduulin rekisteröinti: `src/Cleaner.Cli/ModuleFactory.cs` → `CreateAll`.

## Startup-moduulin tunnisteet (UI tarvitsee)

`CleanItem.Id` = `startup:<lähde>:<nimi>`, lähteet `hkcu-run`, `hklm-run`, `hklm-run32`, `folder-user`, `folder-common`, `task`.
`Path` on rekisteripolku (`HKCU\Software\...\Run\Nimi`), tiedostopolku tai tehtävän polku (`\Nimi`); `Description` on komento;
`SizeBytes = 0`. Skannaus palauttaa vain käytössä olevat kohteet. Käytöstäpoisto kirjoittaa `StartupApproved`-avaimeen
samassa hivessä (`Run`, `Run32`, `StartupFolder`), tehtäville `Enabled = false`. HKLM-lähteet ohitetaan syyllä
"Tarvitsee järjestelmänvalvojan oikeudet", jos prosessi ei ole korotettu. Palautus toistaiseksi Tehtävienhallinnasta;
`StartupApproved.BuildEnabledValue()` on valmiina, jos UI:hin halutaan "ota takaisin käyttöön".

## DiskUsage-moduulin tunnisteet (UI tarvitsee)

`CleanItem.Id` = `diskusage:dir:<polku>` tai `diskusage:file:<polku>`; kategoriat `Suurimmat kansiot` ja
`Suurimmat tiedostot`; `SizeBytes` = alipuun tai tiedoston koko; `Description` = tiedostomäärä ja osuus juuresta
(kansio) tai muokkauspäivä (tiedosto). Kansiokohteet ovat sisäkkäisiä (AppData, AppData\Local, ...), joten niiden
summa ei tarkoita mitään; CLI ei näytä sitä. Koko puu on `DiskUsageModule.LastTrees` (yksi `DirectoryNode` per juuri)
treemapia varten. Mittaus 19.10.2026 tavallisena käyttäjänä: `C:\Users\ville` 2,14 milj. tiedostoa, 474 Gt, 65 s kylmänä;
koko `C:\` 2,83 milj. tiedostoa, 1,52 Tt, 44 s heti perään (välimuisti lämmin). Kuiva `clean --item "diskusage:file:"`
toimi; `--apply` ei ajettu oikealla datalla.

## WinUI 3 -sovellus 19.10.2026

Käännös `dotnet build src\Cleaner.UI` toimii ilman Visual Studiota (XAML-kääntäjä tulee NuGet-paketista), 0 varoitusta.
Exe: `src\Cleaner.UI\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\PcCleaner.exe`, sääntötiedostot kopioituvat viereen
`rules\`-kansioon Files-projektin kautta. Testattu UI Automationilla (PowerShell, `System.Windows.Automation`):
Tiedostosiivous-skannaus löysi 14 487 kohdetta, kategorian valintaruutu pudotti valinnan 14 457:ään, Puhdista avasi
kuivan ajon dialogin ("poistetaan 14 457 kohdetta (340 Mt)"), joka peruttiin. Käynnistysohjelmat-sivu näytti 11 kohdetta
valitsematta. Oikeaa poistoa (dialogin toinen vahvistus) **ei ole ajettu** UI:sta.

Puhdistusketju `MainViewModel.CleanAsync`: kuiva ajo → ContentDialog "Kuivan ajon yhteenveto" (Jatka/Peruuta) →
ContentDialog "Vahvista" (Poista pysyvästi / Poista käytöstä) → oikea ajo → tulosdialogi → moduuli skannataan uudelleen.
Peruuta-nappi peruuttaa `CancellationTokenSource`-olion; keskeytetty oikea ajo skannaa moduulin uudelleen.

Rajoitukset (tilanne illalla 19.10.): treemap, polku/top-N-valinta ja kuvake lisätty (ks. "UI:n jatko"),
ei asetuksia, ei MSIX-pakettia.
Admin-kohteita ei merkitä harmaiksi rivikohtaisesti, koska `CleanItem` ei kanna admin-lippua; moduulitason vihje ja
InfoBar-nappi "Käynnistä uudelleen järjestelmänvalvojana" (`runas`) ovat.

## Registry-moduuli 19.10.2026

`scan --module registry` löysi käyttäjän koneelta 346 kohdetta: 21 tiedostotyyppiorpoa (VS 2010:n ProgId:t, poistettujen
ohjelmien HKCU-ProgId:t, `.gitignore`→`txtfile`, jota Windows 11 ei enää rekisteröi), 322 MUICache-arvoa poistettuihin
Downloads-kansioihin, 3 MRU-listaa (OpenSavePidlMRU 301, LastVisitedPidlMRU 17, TypedPaths 26). 0 Uninstall-orpoa.
Ensimmäinen versio antoi 47 tiedostotyyppiorpoa, joista 26 oli vääriä (`perfmon` ilman päätettä, `.eml`/`.pdb` joilla
PersistentHandler-aliavain, selkonimet oletusarvossa); säännöt tiukennettiin (päätös 20). Kuiva `clean --item "registry:mru:"`
toimi eikä luonut varmuuskopiokansiota. Testit kattavat export → poisto → import -kierroksen testiavaimessa.
**Käyttäjä ajoi oikean rekisteripuhdistuksen ja palautuksen UI:sta 19.10.2026 klo 17:38** (MUICache-kategoria, 322 arvoa):
varmuuskopio `registry-backups\20261019-173834\001-HKCU_..._MuiCache.reg` + manifest syntyi, palautus toimi.
Käyttäjän arvio: "vaikutti toimivan".

Tunnisteet: `registry:uninstall:<avain>`, `registry:ext:<avain>`, `registry:progid:<avain>`, `registry:muicache:<avain>|<arvo>`,
`registry:mru:<avain>`. Avainpolut muodossa `HKCU\...` / `HKLM\...` (ei HKCR). HKLM-kohteet ohitetaan ilman adminia.

## UI:n jatko ja julkaisu 19.10.2026 (ilta)

- `FileLogger` siivoaa käynnistyessä vanhat lokit: säilyttää 50 uusinta ja poistaa yli 30 päivää vanhat
  (`PruneOldLogs`, testattu). Lokikansiossa oli ennen siivousta kymmeniä tiedostoja päivän ajoista.
- Levytilan analyysin sivulla on kansio-kenttä (+ "Selaa..." FolderPicker, sidottu ikkunaan `InitializeWithWindow`),
  Top-N NumberBox ja **treemap**: `Treemap.Squarify` (squarified-algoritmi, DiskUsage-projektissa, testattu) ja
  piirto `MainWindow.xaml.cs`:ssä Canvasille (Border-lohkot, nimi + koko, ToolTip, napsautus avaa alikansion, "Ylös").
  Useampi asema yhdistetään `DirectoryNode.Combine`-solmuksi. `DiskUsageModule.Options` on nyt vaihdettava ominaisuus.
- `TextBox.Text`-sidonta käyttää `UpdateSourceTrigger=PropertyChanged`; ilman sitä Skannaa heti kirjoituksen jälkeen
  skannasi vanhan polun (x:Bind päivittää oletuksena vasta fokuksen vaihtuessa).
- Vain pyydettäessä ajettavien moduulien kategoria- ja tilarivit eivät enää näytä yhteiskokoa (sisäkkäiset kansiot
  summautuivat 4 Tt:ksi).
- **Julkaisu:** `dotnet publish src\Cleaner.UI -c Release -o publish\PcCleaner` tuottaa 249 tiedostoa, 138 Mt,
  ei vaadi Windows App SDK:n asennusta. Kaksi sudenkuoppaa korjattu csproj:ssa: (1) ilman `Platform=x64`-oletusta
  publish ajoi AnyCPU-alustalla eikä XAML/PRI-vaiheita ajettu → exe kaatui 0xC000027B combase.dll:ssä;
  (2) publish ei kopioi `PcCleaner.pri`- ja `.xbf`-tiedostoja, lisätty `CopyPriAndXbfToPublishDir`-target.
  Julkaistu exe käynnistetty ja skannattu. `publish/` on .gitignoressa.
- Illan skannaus löysi Tiedostosiivouksesta enää 34 kohdetta (aamulla 14 487): käyttäjä ajoi puhdistuksen itse
  UI:sta päivän aikana. Uusi kategoria "Muistivedokset" (2 kpl) tuli julkaistun exen kaatumisista (`%LOCALAPPDATA%\CrashDumps`).

## Asennus käyttäjän koneelle 19.10.2026

Julkaistu exe: `C:\Users\ville\PcCleaner\publish\PcCleaner\PcCleaner.exe` (ei versionhallinnassa, `publish/` on
.gitignoressa; generoi uudelleen `dotnet publish src\Cleaner.UI -c Release -o publish\PcCleaner`). Työpöydällä
pikakuvake `PC Cleaner.lnk` (kohde publish-exe, kuvake exestä). Käynnistetty pikakuvakkeesta, otsikko
"PC Cleaner (järjestelmänvalvoja)". Ennen uutta publishia ohjelma pitää sulkea, muuten exe on lukittu.

## Seuraavat vaiheet (järjestys sovittu)

1. ~~`dotnet build` + `dotnet test` Windowsissa~~ tehty 19.10.2026, kaksi bugia korjattu (ks. yllä).
2. ~~`scan` ja kuiva `clean`~~ tehty tavallisena käyttäjänä. Jäljellä: käyttäjä ajaa `scan` admin-PowerShellistä ja tarkistaa `%WINDIR%\Temp`-löydökset, sekä ensimmäinen `clean --apply --module recyclebin` tai vastaava pieni oikea poisto.
3. ~~Avoin kysymys WinUI vs Startup~~ käyttäjä valitsi Startup-moduulin ensin; tehty 19.10.2026.
4. ~~DiskUsage (luku 6.2)~~ tehty 19.10.2026.
5. ~~WinUI 3 (luku 7)~~ ensimmäinen versio tehty 19.10.2026. Jäljellä UI:ssa: treemap, DiskUsage-polun valinta, kuvake, oikean poiston kokeilu.
6. ~~Käyttäjä kokeilee UI:ta~~ kokeiltu 19.10.2026, "toimii tähän asti". ~~Registry (6.3)~~ tehty 19.10.2026.
   **Kaikki ARCHITECTURE.md:n suunnitellut moduulit ja UI:n ensimmäinen versio ovat valmiit.** Seuraavaksi ehdolla:
   a) ~~git init + ensimmäinen commit~~ tehty (`cba91ea`), ~~etärepo~~ github.com/Vilippo/PcCleaner (yksityinen), b) ~~ensimmäinen oikea rekisteripoisto
   ja palautuksen kokeilu~~ käyttäjä teki 19.10.2026 (MUICache), c) ~~treemap, DiskUsage-polun valinta, dotnet publish~~
   tehty; ~~sovelluskuvake~~ tehty 19.10.2026 (`tools\make-icon.ps1` generoi `src\Cleaner.UI\Assets\PcCleaner.ico`
   9 koossa + `.png`; `<ApplicationIcon>` UI- ja CLI-exeen, ikkunaan `AppWindow.SetIcon` tiedostosta, koska pakkaamaton
   WinUI 3 ei käytä exe-resurssia otsikkopalkissa). Jäljellä mahdollinen MSIX/asennusohjelma,
   d) ~~vanhojen lokien siivous~~ tehty.
7. WinUI 3 alkuperäinen kuvaus: projekti `src/Cleaner.UI`, `net8.0-windows10.0.19041.0`, MVVM (CommunityToolkit.Mvvm), NavigationView moduuleille, löydökset kategorioittain valintaruuduin, Skannaa/Puhdista-napit, kaksoisvahvistus, High-riski oletuksena kiinni, `Progress<T>` → ProgressBar, `CancellationToken` → Peruuta. Tarkempi kuvaus ARCHITECTURE.md luku 7.
8. ~~Startup-moduuli (luku 6.1)~~ tehty. ~~DiskUsage (6.2)~~ tehty. ~~Registry (6.3)~~ tehty.

## Tunnetut puutteet / huomiot

- Pikkukuvavälimuisti (387 Mt) on todennäköisesti Explorerin lukitsema. `--apply` ohittaa lukitut tiedostot IOException-syyllä, ei testattu.
- `PathExpander` tukee vain yhtä `*`-jokeria kansiotasolla. Riittää nykyisille säännöille.
- `RecycleBinModule` on merkitty `[SupportedOSPlatform("windows")]` ja rekisteröidään vain Windowsissa; ei testattu oikealla roskakorilla.
- `FileLogger` kirjoittaa `%LOCALAPPDATA%\PcCleaner\logs\cleaner-<aika>-<pid>.log` ja siivoaa käynnistyessä vanhat (50 uusinta säilyy, yli 30 pv vanhat pois).
- Startup ei näe pakattujen sovellusten (Store/MSIX) `StartupTask`-kohteita, jotka Tehtävienhallinta näyttää. Ne ovat avaimessa `HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\<paketti>\<tehtävä>\State`. Lisää lähteenä myöhemmin, jos tarvitaan.
- Startup: `StartupApproved\Run32`-avaimen käyttö Wow6432Node-kohteille on Tehtävienhallinnan käytäntö, mutta sitä ei ole todennettu tällä koneella (ei 32-bittisiä Run-kohteita).
- Startup: ajoitetun tehtävän `Enabled = false` ilman adminia onnistuu vain käyttäjän omille tehtäville; muut päätyvät `Skipped`-listalle syyllä "Ei oikeuksia".
- DiskUsage: top-N-kansiot ovat sisäkkäisiä ketjuja (du-tyyli). Jos halutaan "missä tiedostot oikeasti ovat", lisää vaihtoehtoinen järjestys `OwnBytes`-kentän mukaan; `DirectoryNode` sisältää sen jo.
- DiskUsage: skannauksessa `IgnoreInaccessible = true`, joten tavallisena käyttäjänä osa `C:\Windows`- ja muiden käyttäjien kansioista jää laskematta. `HadErrors`-lippu näkyy kuvauksessa "osa alikansioista ei luettavissa".
- Registry: MUICache-avain viedään kokonaan (yksi .reg, voi olla satoja kilotavuja) ennen ensimmäistä arvon poistoa; sama avain vain kerran per ajo. Palautus tuo koko avaimen takaisin, myös sen jälkeen lisätyt arvot säilyvät (reg import ei poista).
- Registry: `reg export`/`reg import` -tulosteet ovat lokalisoituja; virheviesti välitetään sellaisenaan.
- Registry: ei vielä Uninstall-orpoja MSI-tuotteille (msiexec ohitetaan aina) eikä HKCR-reflektoituja 32-bittisiä ProgId-avaimia (`HKLM\SOFTWARE\WOW6432Node\Classes`).
- `Prefetch` ja `Windows.old` ovat `enabledByDefault: false`. CLI ohittaa ne ilman `--include-disabled`. UI:n pitää näyttää ne valintaruutuina, oletuksena pois päältä.

## Ympäristöhuomio Claudelle

Claude-ympäristöön saa .NET 8 SDK:n komennolla `apt-get install -y dotnet-sdk-8.0`. NuGet (`api.nuget.org`) **ei** ole saavutettavissa, joten restore pitää tehdä tyhjällä lähdelistalla:
`dotnet restore <proj> --configfile offline.nuget.config` (`<packageSources><clear/></packageSources>`) ja `dotnet build --no-restore`. Testiprojektia ei voi kääntää siellä.

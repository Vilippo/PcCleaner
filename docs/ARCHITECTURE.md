# PC Cleaner – tekninen määrittely

Versio 0.1 · 19.10.2026

## 1. Tavoite

Oma siivousohjelma Windows 11:lle (vrt. CCleaner), jossa jokainen ominaisuus on itsenäinen
moduuli. Ydin ei tiedä moduuleista mitään etukäteen, joten uuden ominaisuuden lisääminen
ei vaadi muutoksia olemassa olevaan koodiin.

Ominaisuudet kehitysjärjestyksessä:

| # | Moduuli | Id | Tila | Riski |
|---|---------|----|------|-------|
| 1 | Tiedostosiivous | `files` | valmis (CLI) | Low–High |
| 2 | Selainten välimuistit | `browsers` | valmis (CLI) | Medium |
| 3 | Roskakori | `recyclebin` | valmis (CLI) | Low |
| 4 | WinUI 3 -käyttöliittymä | – | seuraava | – |
| 5 | Käynnistysohjelmat | `startup` | suunniteltu | Medium |
| 6 | Levytilan analyysi | `diskusage` | suunniteltu | – (vain luku) |
| 7 | Rekisterin puhdistus | `registry` | suunniteltu, viimeisenä | High |

## 2. Kerrokset

```
┌─────────────────────────────────────────────────┐
│  Käyttöliittymät: Cleaner.Cli  ·  Cleaner.UI    │   tietävät moduuleista vain
│  (kummallakin oma ModuleFactory)                │   ModuleFactoryn kautta
├─────────────────────────────────────────────────┤
│  Cleaner.Core.Engine.CleanerEngine              │   reitittää kutsut moduuleille
├─────────────────────────────────────────────────┤
│  ICleanerModule-toteutukset                     │   yksi projekti per aihe
│  Files · Browsers · RecycleBin · Startup · ...  │
├─────────────────────────────────────────────────┤
│  Cleaner.Core.Services                          │   jaetut turvapalvelut
│  PathGuard · SafeFileDeleter · PathExpander ... │
└─────────────────────────────────────────────────┘
```

Riippuvuussääntö: nuolet osoittavat vain alaspäin. Moduuli ei saa viitata toiseen
moduuliin eikä käyttöliittymään. Core ei saa viitata mihinkään.

## 3. Keskeiset rajapinnat

### 3.1 `ICleanerModule`

```csharp
public interface ICleanerModule
{
    string Id { get; }              // "files", vakaa, käytetään asetuksissa
    string DisplayName { get; }
    string Description { get; }
    RiskLevel Risk { get; }
    bool RequiresAdmin { get; }
    bool SelectedByDefault => true; // false: mukaan vain kun käyttäjä valitsee moduulin erikseen

    Task<ScanResult>  ScanAsync(CleanerContext ctx, CancellationToken ct);
    Task<CleanResult> CleanAsync(IReadOnlyList<CleanItem> items, CleanerContext ctx, CancellationToken ct);
}
```

Sopimus, jota jokaisen toteutuksen on noudatettava:

- `ScanAsync` **ei muuta mitään**. Se saa olla hidas, mutta sen on oltava turvallinen.
- `CleanAsync` poistaa vain kohteita, jotka se itse on tuottanut (`item.ModuleId == Id`).
  Muut kohteet merkitään `Skipped`.
- Kun `ctx.DryRun == true`, `CleanAsync` ei tee yhtään kirjoittavaa levy- tai rekisterioperaatiota,
  mutta palauttaa `Removed`-listan ikään kuin olisi tehnyt.
- Virheet yksittäisissä kohteissa eivät saa kaataa koko moduulia. Ne kirjataan `Skipped`- tai `Errors`-listaan.
- Levy-I/O ajetaan `Task.Run`-kutsussa, jotta käyttöliittymä ei jäädy.
- `SelectedByDefault == false` tarkoittaa, että "skannaa kaikki" / CLI ilman `--module` ohittaa moduulin.
  Käytetään, kun kohteet ovat käyttäjän omia valintoja (käynnistysohjelmat) eikä niitä saa käsitellä kaikkia kerralla.

### 3.2 Mallit

- `CleanItem` – muuttumaton record: `Id, ModuleId, Category, Path, SizeBytes, Description, Risk`.
  Käyttöliittymä näyttää näitä ja antaa valitut takaisin.
- `ScanResult` – `Items`, `Errors`, laskettu `TotalBytes`.
- `CleanResult` – `Removed`, `Skipped (item, syy)`, `Errors`, `FreedBytes`, `WasDryRun`.
- `CleanerContext` – `DryRun` (oletus **true**), `Logger`, `Progress`, `IsElevated`.
- `RiskLevel` – `Low` / `Medium` / `High`. High-tason kohteet eivät ole koskaan oletuksena valittuina.

### 3.3 `CleanerEngine`

Ainoa asia, jota käyttöliittymä kutsuu. Ottaa moduulilistan konstruktorissa.

- `ScanAsync(ctx, moduleIds?)` – skannaa valitut tai kaikki, kerää tulokset, ottaa kiinni moduulin kaatumiset.
- `CleanAsync(items, ctx)` – ryhmittelee kohteet `ModuleId`:n mukaan ja antaa jokaiselle moduulille omansa.
  Käyttöliittymä voi siis antaa yhden sekalaisen valintalistan.

## 4. Sääntötiedosto (`rules/*.json`)

Tiedostosiivous ja selainvälimuistit ovat samaa `FileCleanerModule`-luokkaa eri sääntötiedostoilla.
Tämä on tarkoituksellista: 90 % siivouskohteista on "poista tiedostot kansiosta X, jotka ovat
vanhempia kuin Y", eikä sitä kannata koodata uudestaan.

```jsonc
{
  "schemaVersion": 1,
  "rules": [
    {
      "id": "user-temp",                 // vakaa tunniste
      "name": "Käyttäjän väliaikaistiedostot",
      "category": "Windows",             // ryhmä käyttöliittymässä
      "description": "Miksi tämä on turvallista poistaa",
      "paths": [ "%TEMP%" ],             // ympäristömuuttujat + yksi * kansiotasolla
      "patterns": [ "*.tmp" ],           // tyhjä = kaikki tiedostot
      "recursive": true,
      "minAgeHours": 24,                 // 0 = ei ikärajaa
      "removeEmptyDirs": true,
      "risk": "Low",                     // Low | Medium | High
      "requiresAdmin": false,
      "enabledByDefault": true,
      "skipIfProcessRunning": [ "chrome" ]
    }
  ]
}
```

`RuleLoader` validoi: Id pakollinen ja yksilöllinen, vähintään yksi polku, `minAgeHours ≥ 0`.
Kommentit ja loppupilkut sallitaan. Testi `Shipped_rule_files_are_valid` varmistaa, että
toimitetut tiedostot latautuvat.

Jokerin laajennus (`PathExpander`): `%LOCALAPPDATA%\Google\Chrome\User Data\*\Cache` →
yksi polku jokaiselle Chrome-profiilille, joka on olemassa levyllä. Vain yksi `*` tuetaan, ja vain
kansiotasolla. Jos tarvitaan monimutkaisempaa, se on merkki siitä, että kohde kuuluu omaan moduuliin.

## 5. Turvallisuusmekanismit

Kerroksittain, niin että yhden pettäminen ei riitä vahinkoon:

1. **Kuiva ajo oletuksena** (`CleanerContext.DryRun = true`). CLI vaatii `--apply`; UI vaatii erillisen napin.
2. **Ikäraja** (`minAgeHours`). Verrataan uudempaan aikaleimoista (luonti/muokkaus).
3. **Prosessitarkistus** (`skipIfProcessRunning`). Avoimen selaimen välimuistia ei kosketa.
4. **PathGuard** – kova lista: levyjen juuret, `Windows`, `System32`, `Program Files`, käyttäjäprofiili,
   Tiedostot, Lataukset, Työpöytä, `AppData`-juuret. Myös näiden *yläpuolella* olevat polut estetään.
   Ajetaan jokaiselle poistolle `SafeFileDeleter`-luokassa; moduulit eivät kutsu `File.Delete` suoraan.
5. **Ei symlinkkien seuraamista** (`AttributesToSkip = ReparsePoint`), jottei junctionin kautta poisteta jotain muualta.
6. **Tyhjien kansioiden poisto** vain säännön juuren *alla*, ei koskaan juurta itseään.
7. **Loki** jokaisesta poistosta tiedostoon, PID nimessä.
8. **Roskakori** tyhjennetään `SHEmptyRecycleBin`-rajapinnalla, ei kansiota poistamalla.

## 6. Tulevat moduulit – suunnitelma

### 6.1 Käynnistysohjelmat (`startup`) – toteutettu

Projekti `src/Cleaner.Modules.Startup`. Lähteet (`StartupSource.Defaults()`):

| Lähde (`Id`-osa) | Sijainti | StartupApproved-avain | Admin |
|---|---|---|---|
| `hkcu-run` | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | `HKCU\...\Explorer\StartupApproved\Run` | ei |
| `hklm-run` | `HKLM\...\Run` | `HKLM\...\StartupApproved\Run` | kyllä |
| `hklm-run32` | `HKLM\Software\Wow6432Node\...\Run` | `HKLM\...\StartupApproved\Run32` | kyllä |
| `folder-user` | `shell:startup` | `HKCU\...\StartupApproved\StartupFolder` | ei |
| `folder-common` | `shell:common startup` | `HKLM\...\StartupApproved\StartupFolder` | kyllä |
| `task` | Task Schedulerin juurikansio, kirjautumistriggeri, ei piilotetut | `Enabled = false` | tehtäväkohtainen |

Toimenpide on **poista käytöstä**, ei poista: `StartupApproved`-avaimeen kirjoitetaan 12 tavun arvo
(`03 00 00 00` + FILETIME) täsmälleen kuten Tehtävienhallinta, joten käyttäjä voi palauttaa kohteen sieltä.
Skannaus palauttaa vain käytössä olevat kohteet. `CleanItem.Id = startup:<lähde>:<nimi>`, `Path` on
rekisteri-, tiedosto- tai tehtäväpolku, `Description` on komento (.lnk-kohde selvitetty), `SizeBytes = 0`.
`SelectedByDefault = false`: moduuli ajetaan vain, kun käyttäjä valitsee sen; CLI:ssä `--module startup` ja
yksittäiset kohteet `--item`-valinnalla. COM (Task Scheduler, WScript.Shell) käytetään `dynamic`-tyypillä
ilman interop-kokoonpanoja. Ei vielä tuettu: pakattujen sovellusten (MSIX) StartupTask-kohteet.

### 6.2 Levytilan analyysi (`diskusage`) – toteutettu

Projekti `src/Cleaner.Modules.DiskUsage`. `ScanAsync` rakentaa kansiopuun (`DirectoryNode`: `OwnBytes`,
`TotalBytes`, `FileCount`, `HadErrors`, `Children`) juurta kohden. Juurten ylätason kansiot käsitellään
`Parallel.ForEach`-silmukassa (`DiskUsageOptions.MaxDegreeOfParallelism`, oletus ytimien määrä), alipuut
peräkkäin. Reparse pointteja ei seurata; piilotetut ja järjestelmätiedostot lasketaan. Lukuvirhe merkitsee
solmun `HadErrors`-lipun eikä kaada skannausta. Puu jää `DiskUsageModule.LastTrees`-ominaisuuteen treemapia varten.

Tulos: top-N suurimmat kansiot (juuria lukuun ottamatta, koko alipuun mukaan) ja top-N suurimmat tiedostot
`CleanItem`-listana, `Id = diskusage:dir:<polku>` / `diskusage:file:<polku>`, `Risk = High`,
`SelectedByDefault = false`. Oletusjuuret ovat kaikki kiinteät asemat; CLI:ssä `--path` ja `--top`.

`CleanAsync` poistaa vain tiedostokohteita `SafeFileDeleter`-luokan kautta. Kansiokohteet ohitetaan aina
(`Skipped`), koska suurimman kansion rekursiivinen poisto on juuri se vahinko, jota ohjelma ei saa tehdä.
Treemap-näkymä WinUI:hin vaiheessa 2.

### 6.3 Rekisterin puhdistus (`registry`) – rajattu, toteutettu

Projekti `src/Cleaner.Modules.Registry` (nimiavaruus `Cleaner.Modules.RegistryCleaner`, jotta
`Microsoft.Win32.Registry` ei sekoitu). Polut ovat aina konkreettisia (`HKCU\...`, `HKLM\...`), ei HKCR-näkymää.
`RegistryBackup`, `RegistryGuard` ja `RegistryPaths` ovat Core-palveluita (`Cleaner.Core.Services`,
kansio `src/Cleaner.Core/Services/Registry/`), jotta muutkin moduulit voivat käyttää niitä viittaamatta
Registry-moduuliin.

| Kategoria | Sijainti | Tulkinta "orvoksi" | Toimenpide | Riski |
|-----------|----------|--------------------|------------|-------|
| Uninstall-merkinnät | `HKLM\...\Uninstall\*`, `Wow6432Node`, `HKCU\...\Uninstall\*` | `UninstallString`-exe absoluuttinen ja puuttuu, ei msiexec, `SystemComponent`≠1, `InstallLocation` ei ole olemassa | avain poistetaan | Medium |
| Tiedostotyypit `.ext` | `HKCU/HKLM\Software\Classes\.ext` | oletusarvo on ProgId-muotoinen (ei välilyöntejä), ProgId puuttuu kaikista Classes-juurista, ei aliavaimia | avain poistetaan | High |
| ProgId | `...\Classes\<ProgId>\shell\open\command` | exe absoluuttinen ja puuttuu, ei `DelegateExecute`; päätteettömälle kokeillaan `.exe` | ProgId-avain poistetaan | High |
| MUICache | `HKCU\...\Shell\MuiCache` | arvonimen exe-osa puuttuu (`.FriendlyAppName`, `.ApplicationCompany`) | arvo poistetaan | Low |
| Viimeksi käytetyt | `RecentDocs`, `RunMRU`, `OpenSavePidlMRU`, `LastVisitedPidlMRU`, `TypedPaths` | ei orpotarkistusta (yksityisyys) | sisältö tyhjennetään, avain säilyy | Low |

Puuttumistarkistus (`CommandLineParser.IsDefinitelyMissing`) on varovainen: suhteellinen polku, verkkopolku,
puuttuva asema tai `WindowsApps`-kansio tulkitaan "ei puutu".

`RegistryGuard` tarkistetaan ennen jokaista poistoa: polun segmentit `CLSID`, `Interface`, `TypeLib`, `AppID`,
`Services`, `Drivers`, `SharedDLLs`, `Fonts` estävät aina; lisäksi kohteen on oltava tyyppinsä juuren suora lapsi
(MUICache ja MRU: täsmälleen oma avain).

Turvamekanismit (toteutettu):
- `RegistryBackup.TryExport` vie avaimen `reg export` -komennolla kansioon
  `%LOCALAPPDATA%\PcCleaner\registry-backups\<aikaleima>\NNN-<avain>.reg` ennen muutosta; epäonnistunut vienti
  ohittaa kohteen. `manifest.txt` listaa viedyt avaimet ja käsitellyt tunnisteet.
- Palautus: `pccleaner restore [aikaleima]` (`RestoreCommand`) ja UI:n nappi "Palauta varmuuskopio..." tuovat kansion
  kaikki `.reg`-tiedostot (`reg import`). HKLM-avaimet vaativat järjestelmänvalvojan.
- Jokainen löydös kertoo kuvauksessaan polun, johon merkintä viittaa, ja miksi se katsotaan puuttuvaksi.
- `SelectedByDefault = false`, moduuli ajetaan vain pyydettäessä; UI ei valitse mitään oletuksena.

## 7. WinUI 3 -käyttöliittymä – toteutettu (ensimmäinen versio)

- Projekti `src/Cleaner.UI`: Windows App SDK 1.8 (self-contained, `WindowsPackageType=None`, x64),
  `net8.0-windows10.0.19041.0`, `CommunityToolkit.Mvvm` 8.4 (partial-ominaisuudet, `[RelayCommand]`).
  Moduulit `UiModuleFactory`-luokan kautta; se lataa kaikki säännöt, myös `enabledByDefault: false`.
- `app.manifest`: `requireAdministrator` (käyttäjän päätös 19.10.2026), UI käynnistyy aina korotettuna. InfoBar ja
  "Käynnistä uudelleen järjestelmänvalvojana" ovat yhä koodissa tavallisen käyttäjän varalle.
- `MainWindow.xaml`: InfoBar (tavallinen käyttäjä + nappi "Käynnistä uudelleen järjestelmänvalvojana",
  ShellExecute `runas`), NavigationView moduuleille, oikealla moduulin otsikko, huomautukset, kategoriat
  Expandereina (valintaruutu kategorialle ja riveille), erillinen oletuksena suljettu "Korkean riskin kohteet"
  -lohko, alapalkissa ProgressBar + Peruuta, valinnan yhteenveto, **Skannaa** ja **Puhdista**, lokinäkymä.
- ViewModelit: `MainViewModel` (engine, komennot, `Progress<T>` → ProgressBar, `CancellationTokenSource` → Peruuta),
  `ModuleViewModel` (viimeisin `ScanResult` kategorioina, valinnan summa), `CategoryViewModel`
  (enintään 500 näkyvää riviä, kolmitilainen valintaruutu), `ItemViewModel`.
- Puhdistus on kolmivaiheinen: kuiva ajo → yhteenvetodialogi → vahvistusdialogi → oikea ajo → tulosdialogi →
  moduulin uudelleenskannaus. Dialogit `IDialogService`-rajapinnan kautta (`MainWindow` toteuttaa).
- Oletusvalinta: `SelectedByDefault && Risk != High`. Startup- ja diskusage-kohteet ovat aina aluksi valitsematta.
- Levytilan analyysin sivu: kansio (TextBox + FolderPicker), Top-N (NumberBox) ja treemap. `Treemap.Squarify`
  (DiskUsage-projekti, puhdas laskenta, testattu) antaa suorakulmiot, `MainWindow` piirtää ne Canvasille Border-lohkoina:
  nimi ja koko, ToolTip, napsautus avaa alikansion, "Ylös" palaa. Useampi asema yhdistetään `DirectoryNode.Combine`-solmuksi.
- Julkaisu: `dotnet publish src\Cleaner.UI -c Release -o publish\PcCleaner`. Csproj asettaa `Platform=x64`-oletuksen
  (muuten XAML/PRI-vaiheet jäävät pois) ja kopioi `PcCleaner.pri`- ja `.xbf`-tiedostot julkaisukansioon
  (`CopyPriAndXbfToPublishDir`), koska publish ei tee sitä pakkaamattomalle sovellukselle.
- Kuvake: `tools\make-icon.ps1` piirtää sen System.Drawing-kirjastolla ja kirjoittaa `Assets\PcCleaner.ico`
  (PNG-pakatut merkinnät 16–256 px) ja `PcCleaner.png`. `<ApplicationIcon>` antaa exe-kuvakkeen; otsikko- ja
  tehtäväpalkin kuvake asetetaan `AppWindow.SetIcon`-kutsulla tiedostosta, joka kopioidaan exen viereen (`Content`).
- Ei vielä: MSIX. Rivikohtainen admin-harmaannus puuttuu, koska `CleanItem` ei kanna admin-lippua; moduulitason vihje on.

## 8. Testaus

- Yksikkötestit (`Cleaner.Tests`, xUnit): `PathGuard`, `RuleLoader`, `FileCleanerModule` oikealla
  väliaikaiskansiolla (kuiva ajo säilyttää, apply poistaa, juuri säilyy, vieraat kohteet hylätään).
- `StartupModule` testataan oikealla rekisterillä omassa avaimessa `HKCU\Software\PcCleanerTests\<guid>`
  (lähteet annetaan konstruktorissa, Task Scheduler pois). Avain poistetaan testin lopuksi.
- `DiskUsageModule` testataan väliaikaisella kansiopuulla: koot, järjestys, rinnakkaisuus 1 vs 8 sama tulos,
  kuiva ajo, tiedoston poisto, kansion ohitus, peruutus.
- Jokainen uusi moduuli tuo omat testinsä; `ScanAsync`-kutsun on oltava idempotentti ja
  `CleanAsync` kuivalla ajolla ei saa muuttaa levyn tilaa – nämä kaksi testiä kirjoitetaan aina.
- `RegistryModule` testataan oikealla rekisterillä omassa avaimessa `HKCU\Software\PcCleanerTests\<guid>` ja
  väliaikaisessa varmuuskopiokansiossa: orpotulkinnat (myös väärät positiivit: selkonimi, aliavain, päätteetön exe,
  trailing null), kuiva ajo, export → poisto → import -kierros, vartijan esto väärennetyillä tunnisteilla.
- UI:lla ei ole yksikkötestejä. Savutesti tehdään UI Automationilla (PowerShell `System.Windows.Automation`:
  Invoke "Skannaa", Toggle valintaruutu, Invoke "Puhdista", lue dialogin tekstit, Invoke "Peruuta").
- Manuaalinen testi ennen julkaisua: `scan` tavallisena käyttäjänä ja adminina, `clean` kuivana,
  `clean --apply` vasta kun kuivan ajon lista on luettu läpi.

## 9. Konventiot

- C# 12, nullable päällä, `sealed` oletuksena.
- Kaikki käyttäjälle näkyvät tekstit suomeksi; koodin nimet englanniksi.
- Moduulin `Id` on pieniä kirjaimia ilman välilyöntejä ja ei muutu koskaan julkaisun jälkeen.
- Levyoperaatiot vain `SafeFileDeleter`- ja `PathExpander`-luokkien kautta.

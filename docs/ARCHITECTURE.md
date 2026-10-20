# PC Cleaner – tekninen määrittely

Versio 0.5 · 20.10.2026

Lisenssi: MIT, ks. `LICENSE` repon juuressa.

## 1. Tavoite

Modulaarinen siivousohjelma Windows 11:lle (vrt. CCleaner), jossa jokainen ominaisuus on itsenäinen
moduuli. Ydin ei tiedä moduuleista mitään etukäteen, joten uuden ominaisuuden lisääminen
ei vaadi muutoksia olemassa olevaan koodiin.

Ominaisuudet kehitysjärjestyksessä. Kaikki ovat toteutettu ja käytössä sekä CLI:ssä että WinUI 3 -sovelluksessa.

| # | Moduuli | Id | Tila | Riski | Luku |
|---|---------|----|------|-------|------|
| 1 | Tiedostosiivous | `files` | valmis | Low–High | 4 |
| 2 | Selainten välimuistit | `browsers` | valmis | Medium | 4 |
| 3 | Roskakori | `recyclebin` | valmis | Low | – |
| 4 | WinUI 3 -käyttöliittymä | – | valmis (ensimmäinen versio) | – | 7 |
| 5 | Käynnistysohjelmat | `startup` | valmis | Medium | 6.1 |
| 6 | Levytilan analyysi | `diskusage` | valmis | High (vain luku, ei poista) | 6.2 |
| 7 | Rekisterin puhdistus | `registry` | valmis, rajattu | High | 6.3 |
| 8 | Ohjelman poisto | `uninstall` | valmis | High, vaatii adminin | 6.4 |
| 9 | Prosessit | `processes` | valmis | High (kohteet Medium, avoimet ohjelmat High) | 6.5 |

## 2. Kerrokset

```
┌─────────────────────────────────────────────────┐
│  Käyttöliittymät: Cleaner.Cli  ·  Cleaner.UI    │   tietävät moduuleista vain
│  (kummallakin oma ModuleFactory)                │   ModuleFactoryn kautta
├─────────────────────────────────────────────────┤
│  Cleaner.Ai (vain UI viittaa, CLI ei)           │   tekoälyavustaja, NuGet Anthropic
├─────────────────────────────────────────────────┤
│  Cleaner.Core.Engine.CleanerEngine              │   reitittää kutsut moduuleille
├─────────────────────────────────────────────────┤
│  ICleanerModule-toteutukset                     │   yksi projekti per aihe
│  Files · Browsers · RecycleBin · Startup ·      │
│  DiskUsage · Registry · Uninstall · Processes   │
├─────────────────────────────────────────────────┤
│  Cleaner.Core.Services                          │   jaetut turvapalvelut
│  PathGuard · SafeFileDeleter · PathExpander ... │
└─────────────────────────────────────────────────┘
```

Riippuvuussääntö: nuolet osoittavat vain alaspäin. Moduuli ei saa viitata toiseen
moduuliin eikä käyttöliittymään. Core ei saa viitata mihinkään. `Cleaner.Ai` viittaa vain Coreen ja
NuGet-pakettiin `Anthropic` 12.58.0. `Cleaner.UI` viittaa siihen, `Cleaner.Cli` ja moduulit eivät.

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

- `CleanItem` – muuttumaton record: `Id, ModuleId, Category, Path, SizeBytes, Description, Risk, Preselected`.
  Käyttöliittymä näyttää näitä ja antaa valitut takaisin. `Preselected` (`bool?`, oletus null) on kohteen oma
  oletusvalinta; null tarkoittaa moduulin `SelectedByDefault`-arvoa.
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
      "name": "User temporary files",    // englanniksi
      "nameFi": "Käyttäjän väliaikaistiedostot", // valinnainen suomenkielinen nimi
      "category": "Windows",             // ryhmä käyttöliittymässä
      "description": "Why this is safe to remove", // englanniksi
      "descriptionFi": "Miksi tämä on turvallista poistaa", // valinnainen suomenkielinen kuvaus
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

`name` ja `description` ovat englanniksi. Kentät `nameFi` ja `descriptionFi` ovat valinnaisia; jos ne puuttuvat,
englanninkielinen teksti näytetään myös suomeksi.

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

## 6. Moduulit 5–9

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
| `storetask` | `HKCU\...\AppModel\SystemAppData\<pakettiperhe>\<tehtävä>`, DWORD `State` (Store/MSIX StartupTask) | `State = 1` (DisabledByUser) | ei |

Toimenpide on **poista käytöstä**, ei poista: `StartupApproved`-avaimeen kirjoitetaan 12 tavun arvo
(`03 00 00 00` + FILETIME) täsmälleen kuten Tehtävienhallinta, joten käyttäjä voi palauttaa kohteen sieltä.
Skannaus palauttaa vain käytössä olevat kohteet. `CleanItem.Id = startup:<lähde>:<nimi>`, `Path` on
rekisteri-, tiedosto- tai tehtäväpolku, `Description` on komento (.lnk-kohde selvitetty), `SizeBytes = 0`.
`SelectedByDefault = false`: moduuli ajetaan vain, kun käyttäjä valitsee sen; CLI:ssä `--module startup` ja
yksittäiset kohteet `--item`-valinnalla. COM (Task Scheduler, WScript.Shell) käytetään `dynamic`-tyypillä
ilman interop-kokoonpanoja.

Store/MSIX-sovellusten StartupTask-kohteet (`StoreStartupTaskSource`): skannaus listaa tehtävät, joiden `State` on
2 (Enabled) tai 4 (EnabledByPolicy), kategoriassa "Store-sovellukset (StartupTask)". Tunnisteen nimiosa on
`<pakettiperhe>|<tehtävä>`, esim. `startup:storetask:Microsoft.YourPhone_8wekyb3d8bbwe|YourPhone.Start`. Käytöstä
poisto kirjoittaa `State = 1` täsmälleen kuten Tehtävienhallinta; tila 4 ohitetaan syyllä "Käytäntö pakottaa käyttöön".
Näyttönimi luetaan `AppModel\Repository\Packages\<täysnimi>\DisplayName`-arvosta (perhe `Nimi_JulkaisijaId` →
täysnimi `Nimi_*_JulkaisijaId`); `ms-resource:`-viittaus ratkaistaan `SHLoadIndirectString`-kutsulla muodossa
`@{<täysnimi>?ms-resource://<Nimi>/Resources/<resurssi>}`, ja jos se ei onnistu, nimeksi jää paketin nimiosa.
`StoreStartupTaskSource.Enable` palauttaa tilan 2 (ei vielä UI:ssa).

### 6.2 Levytilan analyysi (`diskusage`) – toteutettu

Projekti `src/Cleaner.Modules.DiskUsage`. `ScanAsync` rakentaa kansiopuun (`DirectoryNode`: `OwnBytes`,
`TotalBytes`, `FileCount`, `HadErrors`, `Children`) juurta kohden. Juurten ylätason kansiot käsitellään
`Parallel.ForEach`-silmukassa (`DiskUsageOptions.MaxDegreeOfParallelism`, oletus ytimien määrä), alipuut
peräkkäin. Reparse pointteja ei seurata; piilotetut ja järjestelmätiedostot lasketaan. Lukuvirhe merkitsee
solmun `HadErrors`-lipun eikä kaada skannausta. Puu jää `DiskUsageModule.LastTrees`-ominaisuuteen treemapia varten.

Tulos kolmena kategoriana: "Suurimmat kansiot" (top-N juuria lukuun ottamatta, koko alipuun mukaan,
`Id = diskusage:dir:<polku>`), "Kansiot omien tiedostojen mukaan" (top-N `OwnBytes`-kentän mukaan, juuri mukana,
`Id = diskusage:owndir:<polku>`, `SizeBytes` = omat tavut, kuvaus "omia tiedostoja X (n kpl), alipuu yhteensä Y") ja
"Suurimmat tiedostot" (top-N, `Id = diskusage:file:<polku>`). Kaikki `Risk = High`, `SelectedByDefault = false`.
Alipuun mukainen lista täyttyy sisäkkäisistä ketjuista (AppData, AppData\Local, ...); omien tiedostojen lista kertoo,
missä tiedostot oikeasti ovat. Sama kansio voi olla molemmissa eri tunnisteella. Oletusjuuret ovat kaikki kiinteät asemat; CLI:ssä `--path` ja `--top`.

`CleanAsync` poistaa vain tiedostokohteita `SafeFileDeleter`-luokan kautta. Kansiokohteet (`dir` ja `owndir`) ohitetaan aina
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

### 6.4 Ohjelman poisto (`uninstall`) – toteutettu

Projekti `src/Cleaner.Modules.Uninstall`. Käyttäjä valitsee asennetun Win32-ohjelman, moduuli ajaa ohjelman oman
poisto-ohjelman, odottaa sen valmistumista ja etsii jäänteet. "Etsi jäänteet" toimii myös ilman poisto-ohjelman ajoa.
`Risk = High`, `RequiresAdmin = true`, `SelectedByDefault = false`. Store/MSIX-sovellukset eivät kuulu moduuliin.

- `InstalledProgramReader` lukee Uninstall-juuret `HKLM\SOFTWARE\...\Uninstall`, `HKLM\SOFTWARE\WOW6432Node\...\Uninstall` ja
  `HKCU\Software\...\Uninstall`. Pois jäävät `SystemComponent = 1`, tyhjä `DisplayName` ja `ParentKeyName`-arvolliset päivitykset.
  `InstallDate`-arvo (`yyyyMMdd`, myös `yyyy-MM-dd`) jäsennetään `DateOnly`-tyypiksi; `InstalledProgramReader.Sort` järjestää
  listan nimen tai asennuspäivän mukaan (uusin ensin, päivättömät loppuun). UI:n ComboBox ja CLI:n `--sort` käyttävät sitä.
- `UninstallRunner` ajaa `QuietUninstallString`-komennon hiljaisessa tilassa, muuten `UninstallString`. MSI-ohjelmalle se ajaa
  `msiexec /x {GUID}`, hiljaisena lisäksi `/qb-` (ei koskaan `/qn`). Se odottaa prosessin päättymistä ja kysyy sen jälkeen 2 s
  välein enintään 60 s, onko Uninstall-avain kadonnut (`KeyRemoved`). Peruutus lopettaa odotuksen eikä tapa poisto-ohjelmaa.
- `RunningProcessCheck` estää poisto-ohjelman ajon ja karanteenisiirron, jos jonkin prosessin exe on kohdekansion alla
  tai täsmälleen siirrettävä tiedosto. Kohde päätyy `Skipped`-listalle prosessin nimellä.
  Prosessit, joiden polkua ei voi lukea (32-bittiset, suojatut), ohitetaan.
- Moduulin ominaisuudet: `Target` (skannattava ohjelma), `TargetKeyRemoved`, `ListPrograms()`, `RunUninstallerAsync`,
  `LastQuarantineDirectory`, `LastBackupTimestamp`.

Nimivertailu (`NameMatcher`) tehdään sanalistoina: suluissa oleva sisältö, versiot (`1.2.3`, `v2`, `x64`, `64-bit`) ja ™®©
poistetaan, ja merkitsevä sana on vähintään 3 merkkiä eikä kieltolistalla (`the`, `windows`, `software`, `inc`, `corporation`,
`oy` ...). Osumatyypit:

- `Exact`: kansion sanalista on sama kuin ohjelman.
- `Tokens`: kaikki ohjelman merkitsevät sanat ovat kansion nimessä, ja sanoja on vähintään 2 tai ainoa sana on vähintään
  5 merkkiä ("Git" osuu vain kansioon `Git`).
- `Publisher`: kansio on julkaisijan nimi. Kohde on silloin osuva lapsikansio, ei julkaisijakansio itse.
- Kieltolistan kansiot eivät osu koskaan: `Microsoft`, `Windows`, `WindowsApps`, `Common Files`, `Microsoft Shared`, `Packages`,
  `Temp`, `Programs`, `Google`, `Intel`, `NVIDIA`, `AMD`.

Peruskansiot (`LeftoverScanOptions.BaseFolders`): Program Files, Program Files (x86), ProgramData, LocalAppData,
LocalAppData\Programs, Roaming ja LocalLow. Pikakuvakkeet haetaan Käynnistä-valikon Programs-kansioista ja työpöydiltä
(käyttäjä ja yhteinen).

| Kategoria | Sääntö | Riski | Esivalittu |
|-----------|--------|-------|------------|
| Asennuskansio | `InstallLocation` olemassa, ei peruskansio eikä sen yläkansio; tai `DisplayIcon`/`UninstallString`-exen kansio Program Files- tai LocalAppData\Programs-kansiossa, kun nimi osuu | Medium | kyllä |
| Ohjelmakansiot | peruskansion lapsi Program Files-, ProgramData- tai LocalAppData\Programs-kansiossa: `Exact` | Medium | kyllä |
| Ohjelmakansiot | samoissa kansioissa `Tokens` tai julkaisijakansion osuva lapsi | Medium | ei |
| Käyttäjän datakansiot | mikä tahansa osuma LocalAppData-, Roaming- tai LocalLow-kansiossa (voi sisältää käyttäjän dataa) | High | ei |
| Pikakuvakkeet | `.lnk`, jonka kohde on asennuskansion tai osuneen ohjelmakansion alla; Käynnistä-valikon kansio, jonka nimi osuu | Low | kyllä |
| Rekisteriavaimet | `<juuri>\<Nimi>` ja `<juuri>\<Julkaisija>\<Nimi>` juurissa `HKCU\Software`, `HKLM\SOFTWARE`, `HKLM\SOFTWARE\WOW6432Node`; Uninstall-avain, jos se ei kadonnut; App Paths -avain ja Classes-ProgId, jotka osoittavat asennuskansioon | High | ei |
| Rekisteriarvot | merkkijonoarvo, joka alkaa `InstallLocation`-polulla, juurten alla enintään 6 tason syvyydessä (`Classes`, `Microsoft`, `Policies`, `WOW6432Node`, `Clients`, `RegisteredApplications` ja `RegistryGuard`-kieltosegmentit ohitetaan) sekä Run-avaimissa | High | ei |

Kategorian kaikilla kohteilla on sama riski. Sama polku tai avain tulee vain kerran, ensimmäinen osuva sääntö voittaa.
Jos ohjelman Uninstall-avain on yhä olemassa (jäänteet haettiin asennetulle ohjelmalle), mitään ei esivalita ja kuvaukseen
lisätään "(ohjelma on yhä asennettu, ei esivalittu)", jotta Puhdista ei siirrä toimivaa ohjelmaa karanteeniin.
Jokaisen kohteen kuvaus kertoo osumasyyn, esim. "Pikakuvake osoittaa kansioon C:\...". Skannaus käy kansiot ja avaimet
aina samassa järjestyksessä eikä muuta mitään.

Tunnisteet: `uninstall:dir:<polku>`, `uninstall:file:<polku>`, `uninstall:key:<avain>`, `uninstall:value:<avain>|<arvonimi>`.

Karanteeni ja palautus:
- Kansiot ja tiedostot siirretään `Quarantine`-palvelulla (`Cleaner.Core.Services`) kansioon
  `%LOCALAPPDATA%\PcCleaner\quarantine\<aikaleima>\NNN\<nimi>`, ja `manifest.txt` kirjaa `NNN → alkuperäinen polku`. Eri asemalle
  kopioidaan rekursiivisesti ja alkuperäinen poistetaan vasta onnistuneen kopion jälkeen. Lukittu tiedosto (`IOException`)
  ohittaa kohteen koskematta mihinkään.
- Rekisteriavaimet ja -arvot viedään `RegistryBackup`-palvelulla ennen poistoa samalla aikaleimalla, joten yksi
  `pccleaner restore <aikaleima>` palauttaa molemmat. Epäonnistunut siirto tai vienti ohittaa kohteen (`Skipped`).
- `BackupSessions` yhdistää rekisterivarmuuskopiot ja karanteenisessiot aikaleiman mukaan. `Restore` tuo `.reg`-tiedostot
  (`reg import`) ja siirtää karanteenin kohteet takaisin, puuttuvat yläkansiot luodaan. Jos alkuperäinen polku on jo
  olemassa, kohde jää karanteeniin ja syy raportoidaan. CLI:n `restore` ja UI:n "Palauta varmuuskopio..." käyttävät tätä.
- Siirron jälkeen tyhjäksi jäänyt yläkansio (julkaisijakansio) poistetaan, jos se ei ole peruskansio. Tyhjäksi jäänyt
  yläavain poistetaan ilman omaa vientiä, jos se ei ole juuri; `reg import` luo sen palautuksessa uudelleen.

`LeftoverGuard` tarkistetaan ennen jokaista siirtoa tai poistoa tunnistetyypin mukaan:
- `dir`/`file`: polku on peruskansion tai pikakuvakekansion alla tai ohjelman `InstallLocation`-kansiossa, ei näistä mikään
  itse, ja `PathGuard` sallii sen.
- `key`: täsmälleen `<juuri>\<X>` tai `<juuri>\<X>\<Y>` rekisterijuuren alla, tai yksi taso Uninstall-, App Paths- tai
  Classes-juuren alla. Juuren jälkeisessä osassa ei saa olla `RegistryGuard`-kieltosegmenttejä eikä segmenttejä
  `Microsoft`, `Classes`, `Policies`, `WOW6432Node`, `Clients`, `RegisteredApplications`.
- `value`: avain rekisterijuuren alla enintään 6 tason syvyydessä samoilla segmenttiehdoilla, tai täsmälleen Run-avain.
- HKLM-kohteet ohitetaan ilman korotusta syyllä "Tarvitsee järjestelmänvalvojan oikeudet".

Mitä ei kosketa: Store/MSIX-sovellukset, ajurit ja palvelut, Windows-komponentit, muiden käyttäjien profiilit,
peruskansiot ja julkaisijakansiot kohteina, `Microsoft`- ja `Policies`-haarat rekisterijuurten alla (poikkeuksina
Run-avainten arvot sekä App Paths- ja Classes-juurten suorat lapset).

### 6.5 Prosessit (`processes`) – toteutettu

Projekti `src/Cleaner.Modules.Processes`. Skannaus listaa käyttäjän istunnon prosessit, joiden exe ei ole Windows-kansiossa,
ja lopetus päättää käyttäjän valitsemat. Levyltä ei poisteta mitään. `Risk = High` (korkein kohdetaso),
`RequiresAdmin = false`, `SelectedByDefault = false`. Palvelut ja linkitys käynnistysohjelmiin eivät kuulu moduuliin (vaihe 2).

`ScanAsync` raportoi vaiheet "Luetaan prosessit", "Mitataan suorittimen käyttöä (3 s)", "Luokitellaan" ja "Valmis".
Suoritinaika luetaan kahdesti 3 s välein. Prosessi on joutilas vain, jos molemmat näytteet onnistuivat ja aika ei muuttunut.

Suojatut prosessit (`ProcessGuard`) jätetään kokonaan pois listasta. Syyt:
- "Oma prosessi": PcCleanerin oma prosessi ja sen kaikki esivanhemmat (vanhempi, isovanhempi jne., esimerkiksi pääte,
  josta CLI ajettiin). Ketju seurataan `ParentPid`-kenttää pitkin vain, kun esivanhempi käynnistyi aikaisintaan lapsensa
  kanssa; kierrätetty PID katkaisee ketjun. Jo päättynyt vanhempi suojataan `OwnParentPid`-tunnisteella.
- "Järjestelmäprosessi": PID 0–4 (System Idle ja System).
- "Toinen istunto tai palvelu": istunto eri kuin käyttäjän.
- "Tietoja ei voitu lukea": exe-polku tai käynnistysaika jäi lukematta.
- "Windows-kansion prosessi": exe on `%WINDIR%`-kansion alla.
- "Kriittinen prosessi": Windows merkitsee prosessin kriittiseksi.
- "Ei oikeuksia lopettaa": prosessia ei voi avata lopetusoikeudella.

Sulautus: saman exen (polku, kirjainkoko ei merkitse) lapsiprosessi, joka käynnistyi aikaisintaan vanhempansa kanssa,
sulautetaan elossa olevan vanhemman riviin. Ketju seurataan ylimpään saman exen vanhempaan. Rivin muisti on ryhmän summa ja
kuvaus kertoo "N aliprosessia". Vain ryhmän juuri tuottaa `CleanItem`-kohteen.

Kategoriat etusijajärjestyksessä, ensimmäinen osuva voittaa:

| Kategoria | Ehto | Riski | Esivalittu |
|-----------|------|-------|------------|
| Avoimet ohjelmat | jollakin ryhmän prosessilla on näkyvä ikkuna; puuttuva exe tai sääntöosuma näkyy vain kuvauksessa | High | ei koskaan |
| Ohjelmatiedosto puuttuu levyltä | exe-polku tunnetaan, mutta tiedostoa ei ole levyllä | Medium | kyllä |
| Orvot apuprosessit | vanhempi on päättynyt, tai vanhemman PID:llä elää lasta nuorempi prosessi (PID kierrätetty) | Medium | vain jos joutilas |
| Tunnetut taustaprosessit | nimi osuu sääntöön tiedostossa `rules/processes/known.json` | säännön riski, oletus Medium | ei |
| Taustaprosessit, ei käyttöä | suoritinaika ei muuttunut 3 s näytteiden välillä | Medium | ei |
| Taustaprosessit, aktiivisia | muut | Medium | ei |

Lista järjestetään `ProcessCategory`-järjestyksessä (puuttuva exe, orvot, tunnetut, avoimet ohjelmat, ei käyttöä, aktiiviset)
ja kategorian sisällä muistin mukaan suurin ensin.

Sääntötiedosto `rules/processes/known.json` (`ProcessRuleLoader`): kentät `id` (pakollinen, yksilöllinen), `name` (pakollinen),
`match` (vähintään yksi prosessin nimen jokerikuvio, `*` ja `?`, kirjainkoko ei merkitse, `.exe` saa puuttua), `description`
(englanniksi), `descriptionFi` (valinnainen, puuttuessa käytetään `description`-arvoa) ja `risk` (oletus Medium). Toimitetut säännöt: `google-crash-handler`, `adobe-ipc-broker`, `adobe-ccx-process`,
`adobe-collab-sync`, `steam-webhelper` (Low) ja `discord-helper` (Low). Tiedosto on alikansiossa, koska testi
`Shipped_rule_files_are_valid` lukee `rules\*.json`-juuren tiedostot tiedostosääntöinä.

Tunniste `CleanItem.Id = processes:<pid>:<startFileTime>` (`ProcessItemId`), käynnistysaika on kernelin luontiaika
FILETIME-muodossa. `Path` on exe-polku, `SizeBytes` on ryhmän yksityinen muisti. `Description` (`ProcessDescription`)
kertoo järjestyksessä syyn, suorittimen käytön, iän, aliprosessien määrän ja käynnistäjän, esim.
"Käynnistäjä (PID 1234) on päättynyt · ei suorittimen käyttöä 3 s aikana · käynnistetty 2 h 5 min · 3 aliprosessia".

Lopetus (`CleanAsync`, `ProcessTerminator`) kohde kerrallaan:
1. Tunniste tarkistetaan. Muun moduulin kohde ohitetaan syyllä "Kohde ei kuulu tälle moduulille", jäsentymätön syyllä
   "Virheellinen tunniste".
2. Prosessi haetaan tuoreesta näytteestä. Puuttuva ohitetaan syyllä "Prosessi on jo päättynyt", eri käynnistysaika syyllä
   "PID on käytetty uudelleen".
3. `ProcessGuard` ajetaan uudelleen tuoreelle tiedolle, ja suojattu ohitetaan suojauksen syyllä.
4. Ikkunalliselle prosessille lähetetään `CloseMainWindow` ja odotetaan 5 s. Jos prosessi on yhä käynnissä, `Kill` ja
   odotus 5 s. Käynnistysaika tarkistetaan vielä juuri ennen lopetusta.
5. Saman exen jälkeläiset lopetetaan perään lapsista lapsenlapsiin samalla suojaustarkistuksella. Aliprosessin epäonnistuminen kirjataan varoituksena
   eikä muuta juuren tulosta.

Turvamekanismit:
- Kuiva ajo ei lopeta mitään. Loki kirjaa "[DRY RUN] Would end <nimi> (PID n) <polku>" (englanti) tai
  "[KUIVA] Lopetettaisiin <nimi> (PID n) <polku>" (suomi), ja kohde menee `Removed`-listalle.
- Jokainen lopetus kirjataan lokiin muodossa "Lopetettiin <nimi> (PID n): <polku>", aliprosessit omalla rivillään.
- PID yksin ei koskaan riitä. Tunnisteen käynnistysaika estää kierrätetyn PID:n lopettamisen sekä skannauksen ja lopetuksen
  välissä että lopettajassa.
- Avoimet ohjelmat ovat High-riskin kohteita eikä niitä esivalita koskaan. Moduuli ei ole mukana "skannaa kaikki" -ajossa.
- Prosessia ei käynnistetä uudelleen. Loki nimillä ja poluilla riittää palautusohjeeksi.

## 7. WinUI 3 -käyttöliittymä – toteutettu

- Projekti `src/Cleaner.UI`: Windows App SDK 1.8 (self-contained, `WindowsPackageType=None`, x64),
  `net8.0-windows10.0.19041.0`, `CommunityToolkit.Mvvm` 8.4 (partial-ominaisuudet, `[RelayCommand]`).
  Moduulit `UiModuleFactory`-luokan kautta; se lataa kaikki säännöt, myös `enabledByDefault: false`.
- `app.manifest`: `requireAdministrator` (käyttäjän päätös 19.10.2026), UI käynnistyy aina korotettuna. InfoBar ja
  "Käynnistä uudelleen järjestelmänvalvojana" ovat yhä koodissa tavallisen käyttäjän varalle.
- `MainWindow` on ikkunan kuori: Mica, oma otsikkopalkki, koko 1200 × 820 (minimi 960 × 640) ja `IDialogService`.
  Sen sisältö on `Views/MainView` (UserControl, ks. 7.1): otsikkopalkki, InfoBar (tavallinen käyttäjä + nappi
  "Restart as administrator" / "Käynnistä uudelleen järjestelmänvalvojana", ShellExecute `runas`), NavigationView
  moduuleille (240 px, aina auki, ei toggle-nappia, PaneFooter-alueella Scan all / Skannaa kaikki, rataskuvake avaa
  asetussivun: paletti, fontti ja kielivalitsin English/Suomi), oikealla moduulin otsikko ja merkkirivi (riski,
  järjestelmänvalvoja, valitaan erikseen), skannauksen jälkeen laatat Löytyi/Valittu, ennen skannausta tyhjä tila
  Skannaa-napilla, kategoriat kortteina riskiraidalla ja Expanderilla (valintaruutu kategorialle ja riveille),
  erillinen oletuksena suljettu "High-risk items" / "Korkean riskin kohteet" -kortti. Alapalkissa (56 px) valinnan
  yhteenveto, kiireisenä 4 px ProgressBar yläreunassa ja Cancel/Peruuta, Scan/Skannaa, Clean/Puhdista ja lokinappi,
  joka avaa lokipaneelin oikealta.
- ViewModelit: `MainViewModel` (engine, komennot, `Progress<T>` → ProgressBar, `CancellationTokenSource` → Peruuta),
  `ModuleViewModel` (viimeisin `ScanResult` kategorioina, valinnan summa), `CategoryViewModel`
  (enintään 500 näkyvää riviä, kolmitilainen valintaruutu), `ItemViewModel`.
- Puhdistus on kolmivaiheinen: kuiva ajo → yhteenvetodialogi → vahvistusdialogi → oikea ajo → tulosdialogi →
  moduulin uudelleenskannaus. Dialogit `IDialogService`-rajapinnan kautta (`MainWindow` toteuttaa).
- Oletusvalinta: `Risk != High && (Preselected ?? SelectedByDefault)`. Startup-, diskusage- ja registry-kohteet ovat
  aina aluksi valitsematta; uninstall valitsee vain kohteet, joille skanneri asettaa `Preselected = true`.
- Levytilan analyysin sivu: kansio (TextBox + FolderPicker), Top-N (NumberBox) ja treemap. `Treemap.Squarify`
  (DiskUsage-projekti, puhdas laskenta, testattu) antaa suorakulmiot, `MainView` piirtää ne Canvasille Border-lohkoina:
  nimi ja koko, ToolTip, napsautus avaa alikansion, "Ylös" palaa. Useampi asema yhdistetään `DirectoryNode.Combine`-solmuksi.
- Julkaisu: `dotnet publish src\Cleaner.UI -c Release -o publish\PcCleaner`. Csproj asettaa `Platform=x64`-oletuksen
  (muuten XAML/PRI-vaiheet jäävät pois). Kohde `CopyPriAndXbfToPublishDir` kopioi julkaisukansioon
  `PcCleaner.pri`-tiedoston, kaikki `*.xbf`-tiedostot alikansioineen (`Views\MainView.xbf`) ja `Assets\**`-tiedostot,
  koska publish ei tee sitä pakkaamattomalle sovellukselle.
- Kuvake: `tools\make-icon.ps1` piirtää sen System.Drawing-kirjastolla (PNG-pakatut merkinnät 16–256 px), yksi
  kuvake jokaiselle paletille (ks. 7.1). `<ApplicationIcon>` antaa exe-kuvakkeen `PcCleaner.ico`. Otsikkopalkin kuvake on
  MainView'n oman otsikkopalkin Image (`ms-appx:///Assets/PcCleaner-<id>.ico`). `AppWindow.SetIcon` asettaa
  tehtäväpalkin ja ikkunan kuvakkeen tiedostosta exen vieressä.
- Ei vielä: MSIX. Rivikohtainen admin-harmaannus puuttuu, koska `CleanItem` ei kanna admin-lippua; moduulitason vihje on.

### 7.1 Ulkoasu

- `Cleaner.UI.Theming.Appearance` (staattinen luokka, kansio `src/Cleaner.UI/Theming`) tarjoaa listat `Palettes`
  (mint, indigo, steel, graphite, sun; oletus mint) ja `Fonts` (bahnschrift, segoe, inter; oletus bahnschrift).
  `Palette` sisältää lokalisoidun nimen, 14 väriä tummalle ja vaalealle tilalle (`PaletteColors`) ja kuusi
  treemap-väriä. `FontChoice` sisältää otsikko-, leipäteksti- ja lukufontin; lokin fontti on kaikissa
  `Cascadia Mono, Consolas`. Arvot ovat suunnitelman `docs/plans/2026-10-20-visual-refresh.md` osiossa Scales.
  `ResolvePalette` ja `ResolveFont` palauttavat oletuksen tuntemattomalle, tyhjälle tai null-arvolle kirjainkoosta
  riippumatta.
- Fontit: Bahnschrift ja Segoe UI Variable kuuluvat Windows 11:een. Inter 4.1 pakataan sovellukseen tiedostona
  `Assets/Fonts/InterVariable.ttf` (muuttuva fontti, viittaus `ms-appx:///Assets/Fonts/InterVariable.ttf#Inter`)
  lisenssin `LICENSE-Inter.txt` (SIL Open Font License 1.1) kanssa. WinUI:n oletusglob `Assets/**/*.*` kopioi
  molemmat build-kansioon ilman csproj-rivejä. `dotnet publish` tarvitsee csproj-kohteen
  `CopyPriAndXbfToPublishDir`, joka kopioi `PcCleaner.pri`-tiedoston, kaikki `*.xbf`-tiedostot alikansioineen
  (`Views\MainView.xbf`) ja `Assets\**`-tiedostot julkaisukansioon.
- `Appearance.Apply(palette, font)` rakentaa `ResourceDictionary`-olion ja vaihtaa sen
  `Application.Current.Resources.MergedDictionaries`-listaan. Edellisen kutsun sanakirja poistetaan ennen uuden
  lisäämistä. Teemasanakirjat ovat `Default` (tumma), `Light` ja `HighContrast`, joten vaalea/tumma seuraa Windowsin
  asetusta myös ajon aikana. `Default` ja `Light` ohittavat WinUI:n korostusavaimet (`SystemAccentColor*`,
  `AccentFillColor*Brush`, `NavigationViewSelectionIndicatorForeground` ym.), jolloin valintapalkki,
  AccentButtonStyle, CheckBox, RadioButtons ja ProgressBar piirtyvät paletin korostusvärillä. `HighContrast` osoittaa
  omat avaimet Windowsin järjestelmäväreihin eikä ohita korostusavaimia.
- Resurssiavaimet: värit `App*Brush` (esim. `AppAccentBrush`, `AppPaneBackgroundBrush`, `AppContentBackgroundBrush`,
  `AppTextPrimaryBrush`) ja samat Color-avaimina ilman `Brush`-päätettä, riskivärit `Risk*Brush` (`RiskLowBrush`,
  `RiskLowTintBrush`, `RiskLowTextBrush` ja samat Medium- ja High-tasoille, samat kaikissa paleteissa) ja fontit
  `App*FontFamily` (`AppHeadingFontFamily`, `AppBodyFontFamily`, `AppNumberFontFamily`, `AppMonoFontFamily`). WinUI:n
  `ContentControlThemeFontFamily` on Body-fontti. XAML viittaa avaimiin `{ThemeResource}`-viittauksina eikä
  kovakoodaa heksavärejä tai fonttinimiä. Poikkeus on treemapin lohkoteksti: koodi valitsee #FFFFFF tai #1A1A1A sen
  mukaan, kummalla on suurempi WCAG-kontrasti lohkon näkyvään väriin.
- Uudelleenluonti: sanakirjan vaihto ajon aikana päivittää ohjaimet mutta ei TextBlock-tyylejä (`TitleTextBlockStyle`,
  `BodyTextBlockStyle`), jotka jäisivät vanhaan fonttiin. Siksi `MainWindow.BuildView()` luo `MainView`-näkymän
  uudelleen `MainViewModel.AppearanceChanged`-tapahtumassa. Vanha näkymä irrottaa ensin tilauksensa (`Detach`,
  `Bindings.StopTracking()`). Valittu sivu, moduuli, skannaustulokset, valinnat ja lokirivit ovat ViewModelissa ja
  säilyvät. Expanderien tila, vieritys, lokipaneelin auki-tila ja treemapin alikansiopino nollautuvat, ja treemap
  piirtyy juuresta.
- Asetussivu: NavigationView'n asetuskohde (rataskuvake, `IsSettingsVisible`) asettaa
  `MainViewModel.IsSettingsSelected`. Osiot ovat Ulkoasu ja Kieli. Paletti valitaan `RadioButtons`-listasta (nimi ja
  kolme 16 px värinäytettä), fontti `RadioButtons`-listasta (nimi ja näyteteksti omalla fontillaan) ja kieli
  `RadioButtons`-listasta English/Suomi. Rivit ovat `AppearanceOptionViewModel`-olioita (`PaletteOptions`,
  `FontOptions`), valinta on `PaletteIndex`/`FontIndex`. Valitsimet ovat pois käytöstä kiireisenä (`IsIdle`). Kielen
  vaihto päivittää sivun tekstit ilman uudelleenluontia.
- `settings.json` (`%LOCALAPPDATA%\PcCleaner\settings.json`, `AppSettings`): kentät `language`, `palette` (oletus
  `mint`) ja `font` (oletus `bahnschrift`) sekä avustajan `aiModel` ja `aiAutoMode` (luku 7.2). Puuttuva tai null-kenttä antaa oletuksen, joten vanha `{ "language": "fi" }`
  kelpaa. Core ei validoi arvoja. Tuntemattoman arvon (esim. `"palette": "neon"`) UI ratkaisee oletukseksi, eikä
  tiedostoa kirjoiteta käynnistyksessä. `App.OnLaunched` kutsuu `Appearance.Apply` ennen ikkunan luontia. Muutos
  tallennetaan `AppSettings.Update`-kutsulla, joka lataa tiedoston, muuttaa yhden kentän ja tallentaa, joten muut
  kentät säilyvät. Tallennuksen epäonnistuminen kirjataan varoituksena lokiin eikä estä vaihtoa.
- Kuvakkeet: `tools\make-icon.ps1` kirjoittaa `Assets\PcCleaner-<id>.ico` jokaiselle paletille (sama luuta, taustan
  liukuväri paletin mukaan, värit skriptin hajautustaulukossa), `PcCleaner.ico` identtisenä mint-tiedoston kanssa ja
  `PcCleaner.png` (256 px, mint). Exen kuvake on siksi aina mint. Otsikko- ja tehtäväpalkin kuvake on
  `Appearance.IconFileName(palette)`, ja `BuildView` asettaa sen joka kerta; jos tiedostoa ei ole, käytetään
  `PcCleaner.ico`.
- Mica ja otsikkopalkki: `MainWindow` asettaa `SystemBackdrop = new MicaBackdrop()` ja
  `ExtendsContentIntoTitleBar = true`. MainView'n otsikkopalkki (36 px, `AppTitleBar`, paletin kuvake 16 px ja
  `TitleText`) on raahausalue (`SetTitleBar`). Navigointipaneelin tausta tulee siitä, että `Appearance`
  ohittaa WinUI:n avaimet `NavigationViewDefaultPaneBackground` ja `NavigationViewExpandedPaneBackground` paletin
  PaneBackground-värillä (70 % peittävä Mican päällä). Otsikkopalkki käyttää samaa väriä avaimella
  `AppPaneBackgroundBrush`, joten ne näkyvät yhtenä pintana. Kun `MicaController.IsSupported()` on false, Micaa ei
  aseteta ja näkymän juuren tausta on `AppContentBackgroundBrush`.
- Lokipaneeli: `SplitView` oikealla (`PanePlacement=Right`, `DisplayMode=Overlay`, 420 px). Alapalkin lokinappi avaa
  ja sulkee sen. Paneelissa on otsikko, sulkemisnappi, lokirivit Mono-fontilla 12 px, Tyhjennä-nappi ja
  lokitiedoston polku.

### 7.2 Tekoälyavustaja

- Projekti `src/Cleaner.Ai` (net8.0, nimiavaruus `Cleaner.Ai`) viittaa vain `Cleaner.Core`-projektiin ja
  NuGet-pakettiin `Anthropic` 12.58.0. `Cleaner.UI` ja testiprojekti viittaavat siihen, CLI ei. Luokat:
  `PathSanitizer`, `ScanDigest` (`DigestOptions`), `HealthDigest`, `SystemFactsCollector` (`SystemFacts`, `DriveFacts`),
  `AiFormat`, `AiPrompts`, `AiReviewSchema`, `AiReviewParser`, `VerdictApplier`, `AiAssistant` (`AiAssistantOptions`),
  `IAiBackend` ja sen toteutus `AnthropicBackend`, `AiException` (`AiErrorKind`) sekä mallit tiedostossa `AiModels.cs`.
  UI:ssa `AiAssistantViewModel` (`MainViewModel.Ai`) ja `AiMessageViewModel`. Vain `AiAssistant` kutsuu
  `IAiBackend`-metodeja.
- API-avain: `AnthropicBackend` luo `AnthropicClient`-olion ilman parametreja, joten SDK lukee avaimen
  ympäristömuuttujasta `ANTHROPIC_API_KEY`. Avainta ei kirjoiteta asetuksiin, lokiin eikä paneeliin.
  `AnthropicBackend.IsConfigured` on tosi, kun muuttuja ei ole tyhjä. UI lukee sen kerran käynnistyksessä
  (`AiAssistantViewModel.IsAvailable`), joten avaimen asettamisen jälkeen sovellus käynnistetään uudelleen. Asiakas
  ja `AiAssistant` luodaan vasta ensimmäisessä kutsussa; käynnistys ei tee API-kutsua.
- Malli: `settings.json`-kenttä `aiModel` (oletus `claude-opus-5-5`, null antaa oletuksen). UI lukee sen
  käynnistyksessä, eikä sille ole valitsinta. Arvio käyttää `Messages.Create`-kutsua JSON-skeemalla
  (`OutputConfig.Format`, `AiReviewSchema.Json`), effort High ja `max_tokens` 16 000. Koneen kunto ja kysymykset
  käyttävät `CreateStreaming`-kutsua, effort Medium ja 8 000. Thinking-parametria ei lähetetä. Token-määrät kirjataan
  lokiin Debug-tasolla, paneelissa niitä ei näytetä.
- Mitä lähetetään: järjestelmäkehote (`AiPrompts.System`, englanniksi tai suomeksi `Loc.Current`-kielen mukaan), koko
  keskusteluhistoria ja uusi käyttäjäviesti, joka on yksi seuraavista:
  - Arvio: `AiPrompts.ReviewInstruction` ja `ScanDigest.Text` valitun moduulin kaikista kohteista, myös
    High-lohkosta. Jokaisella kategorialla on otsikkorivi (nimi, korkein riski, kohdemäärä, yhteiskoko, valittujen
    määrä). Kohderivit ovat koon mukaan suurin ensin: avain `i<n>` (juokseva), `[x]` tai `[ ]`, koko, siistitty polku
    ja siistitty kuvaus katkaistuna 120 merkkiin. Rajat (`DigestOptions`): kategoria, jossa on enintään 40 kohdetta,
    listataan kokonaan; suuremmasta listataan 20 suurinta ja rivi `(+N more, not listed)`. Koko tekstissä on
    enintään 400 kohderiviä. Kiintiön loputtua kategoria katkaistaan, ja loput kategoriat saavat vain otsikkorivin ja
    rivin `(items not listed)`. Koot muotoilee `AiFormat.Bytes` (B, KB, MB, GB, TB, yksi desimaali,
    `InvariantCulture`), ei `ByteFormatter`, joten teksti ei riipu käyttöliittymän kielestä. Opt-in-moduuleilla
    (`SelectedByDefault == false`) koon tilalla on `size not summed (nested items)`. Digest rakennetaan
    `Task.Run`-kutsussa.
  - Koneen kunto: `AiPrompts.HealthInstruction` ja `HealthDigest`-teksti: käyttöjärjestelmän versio, suoritinytimet,
    muisti yhteensä ja vapaana (`GlobalMemoryStatusEx`), käyntiaika, onko prosessi korotettu, jokainen kiinteä asema
    (nimi, nimiö, koko, vapaana, prosentti) ja jokaisesta moduulista `not scanned` tai kohdemäärä, yhteiskoko ja
    enintään 5 suurinta kategoriaa (nimi, määrä, koko). Tiedot kerää `SystemFactsCollector.Collect()`, joka ei
    heitä. Skannauksia ei käynnistetä. Tekstissä ei ole polkuja.
  - Kysymys: käyttäjän kirjoittama teksti sellaisenaan, siistimättä.
  - `PathSanitizer.ForCurrentUser()` siistii digestin polut ja kuvaukset kirjainkoosta riippumatta: nykyisen profiilin
    polku korvataan merkinnällä `%USERPROFILE%`, kun sitä ei seuraa kirjain, numero, `_`, `-` tai `.` (siksi
    `C:\Users\ville` ei osu polkuun `C:\Users\villeneuve`), ja muualla esiintyvä `\Users\<käyttäjänimi>\` muodolla
    `\Users\%USERNAME%\`. Muiden käyttäjien profiilit eivät muutu. Kehote kertoo mallille, että merkinnät ovat
    tahallisia.
- Mitä ei lähetetä: lokitiedostoa, `settings.json`-tiedostoa, API-avainta, tiedostojen sisältöä eikä erikseen
  luettuja rekisteriarvoja. Rekisterikohteista lähtee sama polku ja kuvaus kuin kortissa, ja kuvauksessa voi olla
  polku, johon merkintä viittaa (esim. puuttuva exe).
- Arviot: skeema `AiReviewSchema` (`summary`, `categories[]` ja `items[]`, joissa `category` tai `key`, `verdict` ja
  `reason`; kaikki pakollisia, `additionalProperties: false`). `verdict` on `safe` (voi poistaa ilman tietojen
  menetystä), `caution` (luultavasti turvallinen, tarkista) tai `keep` (ei saa poistaa). `AiReviewParser` kuvaa
  avaimen `i<n>` takaisin `CleanItem.Id`-tunnisteeksi ja pudottaa hiljaa tuntemattoman avaimen, kategorian ja
  arvion; kahdesti annetusta ensimmäinen voittaa. Rikkinäinen JSON tai puuttuva kenttä on `InvalidResponse`.
- Periytyminen (`VerdictApplier.Resolve`): kohteen oma arvio voittaa, sitten kategorian arvio, muuten arviota ei ole.
  Kategorian arvio koskee myös digestistä pois jääneitä kohteita.
- Merkit: `ModuleViewModel.ApplyReview` antaa kategorialle kategorian arvion ja kohteelle vain sen oman arvion (ei
  periytyvää), joten kohteen merkki kertoo, mistä avustaja sanoi jotain erityistä. Kortissa kategorian merkki on
  otsikkorivillä riskitekstin jälkeen ja kohteen merkki koon vasemmalla puolella. Teksti on safe/caution/keep tai
  turvallinen/varo/säilytä (`VerdictText`), värit `RiskLowTextBrush`, `RiskMediumTextBrush` ja `RiskHighTextBrush`,
  ja ToolTip näyttää perustelun. `LoadScan` ja `ClearScan` poistavat arvion ja merkit ja kasvattavat
  `ScanGeneration`-laskuria. Arvio viedään merkeiksi vain, jos laskuri on sama kuin pyyntöä tehtäessä; muuten se
  näkyy paneelissa pelkkänä tekstinä.
- Arvion viestit: käyttäjän viesti "Tarkista tulokset: <moduuli>" (digestiä ei näytetä) ja avustajan viesti, jossa on
  `summary` ja sen alla rivi jokaisesta caution- ja keep-arviosta (arvio, kategoria tai polku, perustelu).
- "Poista varoitetut valinnasta" (`DeselectFlaggedCommand`) poistaa valinnan jokaiselta kohteelta, jonka `Resolve`
  antaa caution tai keep, myös 500 näkyvän rivin ulkopuolelta, ja lisää paneeliin rivin "Poistettu valinnasta N
  kohdetta". Nappi näkyy vain, kun `HasFlagged` on tosi eli valitulla moduulilla on arvio ja vähintään yksi valittu
  kohde on caution tai keep. Oletuksena avustaja vain neuvoo eikä muuta valintoja.
- Auto-tila: `settings.json`-kenttä `aiAutoMode` (oletus false). Kytkin "Auto-tila" on paneelissa ja asetussivun
  osiossa "Tekoälyavustaja", ja molemmat sitovat `Ai.AutoMode`-arvoon. Muutos tallennetaan heti
  `AppSettings.Update`-kutsulla; epäonnistuminen kirjataan varoituksena eikä estä vaihtoa. Auto-tilassa arvio myös
  valitsee kohteet (`ModuleViewModel.ApplyAutoSelection`, `VerdictApplier.AutoSelection`): High-riskin kohde ei ole
  koskaan valittuna, caution ja keep poistavat valinnan, safe valitsee, ja kohde ilman arviota pysyy ennallaan.
  Paneeliin tulee rivi "Valittu N, poistettu valinnasta M".
- Automaattinen siivous (`MainViewModel.AutoCleanCommand`, sallittu kun sovellus ei ole kiireinen, avustaja ei
  ajattele, avain on asetettu ja Auto-tila on päällä). Vaiheet 1–3 ajetaan yhden `BeginBusy`-tilan ja yhden
  `CancellationTokenSource`-olion alla. Alapalkin ja paneelin Peruuta keskeyttävät ne ilman dialogia ja ilman
  puhdistusta.
  1. Skannaa kaikki `SelectedByDefault`-moduulit kuten "Skannaa kaikki".
  2. Pyytää arvion jokaisesta moduulista, jolla on kohteita (`BusyTitle` "Arvio: <moduuli>"), ja soveltaa Auto-tilan
     valinnat. Virhe tai peruutus lopettaa ajon, samoin Auto-tilan kytkeminen pois kesken ajon.
  3. Ajaa kuivan ajon näiden moduulien valituille kohteille. High-riskin kohteet suodatetaan pois, vaikka käyttäjä
     olisi rastinut ne kesken ajon.
  4. Näyttää yhden vahvistusdialogin "Automaattinen siivous": jokaisen arvioidun moduulin `summary`, kuivan ajon
     kohdemäärä ja koko, ohitetut syineen (enintään 4 yleisintä syytä), virheet (enintään 4) ja rivi "Tiedostot
     eivät mene roskakoriin". Painike on "Poista pysyvästi".
  5. Ajaa oikean ajon, näyttää tulosdialogin ja skannaa moduulit uudelleen.

  Kun valittuja kohteita ei ole tai kuiva ajo ei poistaisi mitään, näytetään "Ei mitään tehtävää". Opt-in-moduulit
  (käynnistysohjelmat, levytilan analyysi, rekisteri, ohjelman poisto, prosessit) eivät ole koskaan mukana.
  Tavallinen Puhdista-ketju (kuiva ajo, yhteenveto, vahvistus) on ennallaan myös Auto-tilassa.
- Virheet: `AnthropicBackend` kääri SDK:n poikkeukset `AiException`-olioiksi (alkuperäinen on `InnerException`): 401
  `Unauthorized`, 429 `RateLimited`, muu 4xx `BadRequest`, 5xx ja suoratoiston virhetapahtuma (`AnthropicSseException`)
  `Server`, `AnthropicIOException` `Network`, `AnthropicInvalidDataException` `InvalidResponse`, muut `Other`. SDK:n
  aikakatkaisu (peruutus, jota käyttäjä ei pyytänyt) on `Network`. `stop_reason` `refusal` on `Refused` ja kantaa
  luokan (`StopDetails.Category`). Paneeli näyttää virheen omana viestinään lokalisoidulla tekstillä tyypin mukaan,
  ja loki saa varoituksen. Peruutus jättää jo saadun tekstin ja lisää "(peruutettu)". `max_tokens` lisää rivin
  "(vastaus katkesi)"; katkennut arvio-JSON on `InvalidResponse`.
- Historia (`AiAssistant.History`) on vain muistissa eikä tallennu levylle. Jokainen kutsu lähettää koko historian.
  Rajat `MaxHistory` 20 viestiä ja `MaxHistoryChars` 120 000 merkkiä. Ylityksessä vanhimpia käyttäjän ja avustajan
  viestipareja poistetaan alusta, mutta uusin pari säilyy aina. Vain uusimman arvion digest on kokonaisena; aiempien
  arvioiden käyttäjäviesteissä digest korvataan tekstillä `(earlier review of <module>; item list omitted)`. Arviosta
  tallennetaan raaka JSON, jotta jatkokysymykset näkevät perustelut. Epäonnistunut, peruttu tai jäsentymätön kutsu
  poistaa oman käyttäjäviestinsä, joten historia pysyy pareina. "Uusi keskustelu" (`ResetCommand`) tyhjentää
  historian ja viestit mutta ei merkkejä; jos kutsu on silloin kesken, sen myöhästynyttä vastausta ei lisätä
  historiaan. Kahta kutsua ei ajeta yhtä aikaa (`IsThinking`). Tarkista tulokset, Koneen kunto ja kysymys eivät ole
  sallittuja skannauksen tai puhdistuksen aikana, mutta avustajan ajatellessa Skannaa ja Puhdista ovat sallittuja.
- Paneeli (`MainView`): `AiSplitView` (`SplitView`, `PanePlacement=Right`, `DisplayMode=Inline`, `OpenPaneLength=400`,
  oletuksena kiinni) ympäröi `NavigationView`-elementin `LogSplitView`-sisällön sisällä, joten moduulisivu kapenee
  paneelin auetessa ja lokipaneeli piirtyy sen päälle. Alapalkin nappi (FontIcon `&#xE99A;`, ToolTip
  "Tekoälyavustaja") lokinapin vasemmalla puolella avaa ja sulkee paneelin. Paneelissa ylhäältä alas: otsikko ja
  sulkunappi; InfoBar (`UnavailableText`), kun avainta ei ole; kytkin "Auto-tila"; viestilista (käyttäjän viesti
  `AppCardHoverBrush`-taustalla ja vasen marginaali 24 px, avustajan viesti `AppCardBackgroundBrush` ja oikea
  marginaali 24 px, virhe `RiskHighTintBrush`, tiedote ilman taustaa; teksti on valittavissa eikä markdownia
  renderöidä), joka vierittää uusimman viestin näkyviin; ProgressRing ja Peruuta ajattelun aikana; napit "Tarkista
  tulokset", "Koneen kunto", "Automaattinen siivous" (vain Auto-tilassa), "Poista varoitetut valinnasta" (vain kun
  `HasFlagged`) ja "Uusi keskustelu"; alimpana kysymyskenttä (Enter lähettää) ja lähetysnappi. Asetussivun osio
  näyttää saman kytkimen, tekstin "Malli: <aiModel>", avainvihjeen (`KeyHint`) ja tietosuojahuomautuksen.
  Viestit, arviot ja Auto-tila ovat ViewModelissa, joten ulkoasun vaihto säilyttää ne; paneeli sulkeutuu kuten
  lokipaneeli. Kielen vaihto päivittää kiinteät tekstit, `UnavailableText`-tekstin ja merkit, mutta jo kirjoitetut
  viestit jäävät syntykieleensä.

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
- Ohjelman poisto: `QuarantineTests` ja `BackupSessionsTests` (siirto, palautus, olemassa oleva kohde ohitetaan,
  PathGuard-hylkäys, yhdistetty listaus), `NameMatcherTests` (sanalistat, lyhyt nimi, kieltolista),
  `InstalledProgramReaderTests`, `LeftoverScannerTests` (säännöt ja esivalinta temp-kansioissa ja testiavaimessa),
  `UninstallRunnerTests` (komentorivin valinta, MSI, avaimen katoamisen odotus, puuttuva exe, `RunningProcessCheck`) ja `UninstallModuleTests`
  (idempotentti skannaus, kuiva ajo, karanteeni + rekisteri + palautus, väärennetyt tunnisteet). Rekisteritestit
  käyttävät `HKCU\Software\PcCleanerTests\<guid>`-avainta, tiedostotestit `%TEMP%\pccleaner-tests\<guid>`-kansiota.
- Tekoälyavustaja (`Cleaner.Ai`): `PathSanitizerTests`, `ScanDigestTests` (rajat 40/20/400, järjestys, kielestä
  riippumaton teksti), `HealthDigestTests`, `AiReviewParserTests`, `VerdictApplierTests`, `AiAssistantTests`
  (väärennetty `IAiBackend`: parit, historian rajat, aiemman digestin korvaus, Reset kesken kutsun) ja
  `AnthropicBackendTests` (`IsConfigured` ja konstruktori ympäristömuuttujalla, ei verkkokutsuja). Kentät `aiModel`
  ja `aiAutoMode` testataan `LocalizationTests`-luokassa.
- UI:lla ei ole yksikkötestejä. Savutesti tehdään UI Automationilla (PowerShell `System.Windows.Automation`:
  Invoke "Scan", Toggle valintaruutu, Invoke "Clean", lue dialogin tekstit, Invoke "Cancel"). Nimet ovat
  oletuskielellä englanniksi; suomeksi ne ovat "Skannaa", "Puhdista" ja "Peruuta".
- Manuaalinen testi ennen julkaisua: `scan` tavallisena käyttäjänä ja adminina, `clean` kuivana,
  `clean --apply` vasta kun kuivan ajon lista on luettu läpi.

## 9. Konventiot

- C# 12, nullable päällä, `sealed` oletuksena.
- Koodin nimet englanniksi. Käyttäjälle näkyvät tekstit ovat englanniksi ja suomeksi, oletuskieli on englanti
  (Windowsin kieli ei vaikuta):
  - `Cleaner.Core.Localization.Loc`: `Loc.Current` (`Language.En`/`Language.Fi`, oletus `En`), `Loc.T(en, fi)`
    palauttaa nykyisen kielen tekstin sillä hetkellä, kun teksti syntyy, `Loc.Changed` laukeaa kerran jokaisesta
    todellisesta kielen muutoksesta ja `Loc.Culture` (`en-US`/`fi-FI`) muotoilee käyttäjälle näkyvät numerot.
    `CultureInfo.CurrentCulture` jää ennalleen.
  - Uusi teksti kirjoitetaan koodiin `Loc.T("English", "Suomi")`-parina, englanti ensin. Arvo, joka luetaan
    myöhemmin uudelleen (esim. moduulin nimi ja kuvaus), tallennetaan tyyppinä `LocalizedText(En, Fi)`: sen
    `ToString()` palauttaa `Loc.T(En, Fi)`, ja `string` muuntuu siihen implisiittisesti. Ei `.resx`- eikä `.resw`-tiedostoja.
  - Olemassa oleva suomenkielinen teksti pysyy merkki merkiltä ennallaan; käännös lisää vain englanninkielisen version.
  - Sääntötiedostot: `name` ja `description` englanniksi, suomi kentissä `nameFi` ja `descriptionFi`
    (`CleanRule.DisplayName`, `CleanRule.DisplayDescription`). Prosessisäännöissä (`rules/processes/known.json`)
    suomi on kentässä `descriptionFi` (`ProcessRule.DisplayDescription`). Jos suomenkielinen kenttä puuttuu,
    englanninkielinen teksti näytetään molemmilla kielillä.
  - Kielivalinta: `AppSettings` lukee ja kirjoittaa tiedoston `%LOCALAPPDATA%\PcCleaner\settings.json`
    (`{ "language": "en" }` tai `{ "language": "fi" }`). Puuttuva, rikkinäinen tai tuntematon arvo → englanti.
    CLI valitsee kielen järjestyksessä `--lang` > `settings.json` > `en` eikä kirjoita tiedostoa. UI lukee asetuksen
    ennen pääikkunan luontia. Navigaatiopaneelin valitsin `English`/`Suomi` tallentaa valinnan ja vaihtaa kielen heti:
    `Loc.Changed` päivittää `UiText`-olion ja näkymämallit, joihin XAML on sidottu `x:Bind ... Mode=OneWay`
    -sidonnoilla. Jo tuotetut skannaustulokset ja lokirivit jäävät kielelle, jolla ne syntyivät.
  - Pysyvät tiedostomuodot pysyvät suomeksi: karanteenin ja rekisterivarmuuskopioiden `manifest.txt` (myös erotinrivi
    `Käsitellyt kohteet:`, jota `RegistryBackup` jäsentää). Kohdetunnisteet sekä CLI:n komennot ja liput eivät
    riipu kielestä.
  - Testit ajetaan oletuksena suomeksi: testiprojektin moduulialustaja asettaa `Loc.Current = Language.Fi`, joten
    suomenkieliset tarkistukset pysyvät voimassa. Englantia testaavat luokat kuuluvat xUnit-kokoelmaan `Language`
    (`DisableParallelization = true`), asettavat `Language.En` ja palauttavat `Language.Fi` lopuksi.
- Moduulin `Id` on pieniä kirjaimia ilman välilyöntejä ja ei muutu koskaan julkaisun jälkeen.
- Levyoperaatiot vain `SafeFileDeleter`- ja `PathExpander`-luokkien kautta.
- Ulkoiset NuGet-paketit vain `Cleaner.Ai`- ja `Cleaner.UI`-projekteissa. Testiprojekti käyttää lisäksi xUnitia.

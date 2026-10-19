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

### 6.1 Käynnistysohjelmat (`startup`)

Lähteet: `HKCU\...\Run`, `HKLM\...\Run` (+ `Wow6432Node`), `shell:startup`-kansiot (käyttäjä + yhteinen),
Task Scheduler -tehtävät, joiden triggeri on kirjautuminen.

Toimenpide on **poista käytöstä**, ei poista: siirretään arvo `...\Explorer\StartupApproved\Run`-avaimeen
samalla tavalla kuin Tehtävienhallinta tekee, jotta käyttäjä voi palauttaa sen sieltä. `CleanItem.Path`
sisältää rekisteripolun tai tiedostopolun, `SizeBytes = 0`.

### 6.2 Levytilan analyysi (`diskusage`)

Vain lukeva moduuli: `ScanAsync` rakentaa kansiopuun kokoineen rinnakkaisesti (`Parallel.ForEach`
ylätason kansioille), `CleanAsync` ei tee mitään. Palauttaa top-N suurimmat kansiot ja tiedostot
`CleanItem`-listana (`Risk = High`, `EnabledByDefault = false`), jotta käyttäjä voi halutessaan poistaa
suoraan käyttöliittymästä. Treemap-näkymä WinUI:hin vaiheessa 2.

### 6.3 Rekisterin puhdistus (`registry`) – rajattu

Toteutetaan viimeisenä ja vain näille kategorioille:

| Kategoria | Sijainti | Tulkinta "orvoksi" |
|-----------|----------|--------------------|
| Uninstall-merkinnät | `HKLM/HKCU\...\Uninstall\*` | `InstallLocation` tai `UninstallString`-polku ei ole olemassa |
| Tiedostotyyppikytkökset | `HKCR\<.ext>` → `HKCR\<ProgId>\shell\open\command` | komennon exe-polku ei ole olemassa |
| MUICache | `HKCU\...\Shell\MuiCache` | viitattu exe ei ole olemassa |
| Viimeksi käytetyt | `RecentDocs`, `RunMRU`, `OpenSavePidlMRU` | yksityisyys, ei orpotarkistusta |

Ei kosketa koskaan: `CLSID`, `Interface`, `TypeLib`, `Services`, `Drivers`, `SharedDLLs`, fontit.

Pakolliset turvamekanismit ennen ensimmäistä `--apply`-ajoa:
- Jokaista poistettavaa avainta vastaava `.reg`-vienti (`reg export`) kansioon
  `%LOCALAPPDATA%\PcCleaner\registry-backups\<aikaleima>\` ennen muutosta.
- Palautuskomento (`pccleaner restore <aikaleima>`) ja -nappi käyttöliittymässä.
- Löydös näyttää aina polun, johon merkintä viittaa, ja miksi se katsotaan puuttuvaksi.

## 7. WinUI 3 -käyttöliittymä – suunnitelma

- Projekti `Cleaner.UI` (Windows App SDK 1.6+, `net8.0-windows10.0.19041.0`), MVVM-malli
  (`CommunityToolkit.Mvvm`), moduulit `ModuleFactory`-luokan kautta kuten CLI:ssä.
- Päänäkymä: vasemmalla moduulilista (NavigationView), oikealla löydökset kategorioittain
  valintaruuduin, alhaalla yhteenveto ja kaksi nappia: **Skannaa** ja **Puhdista**.
- Puhdista-nappi näyttää ensin yhteenvedon kuivasta ajosta ja vaatii toisen vahvistuksen.
- `High`-riskin kohteet erillisessä, oletuksena kiinni olevassa lohkossa.
- `Progress<T>` sidotaan ProgressBariin; `CancellationToken` Peruuta-nappiin.
- Admin-oikeuksia vaativat kohteet näytetään harmaina ja "Käynnistä uudelleen järjestelmänvalvojana" -linkillä
  (ShellExecute `runas`), ei pakoteta koko sovellusta adminiksi.

## 8. Testaus

- Yksikkötestit (`Cleaner.Tests`, xUnit): `PathGuard`, `RuleLoader`, `FileCleanerModule` oikealla
  väliaikaiskansiolla (kuiva ajo säilyttää, apply poistaa, juuri säilyy, vieraat kohteet hylätään).
- Jokainen uusi moduuli tuo omat testinsä; `ScanAsync`-kutsun on oltava idempotentti ja
  `CleanAsync` kuivalla ajolla ei saa muuttaa levyn tilaa – nämä kaksi testiä kirjoitetaan aina.
- Manuaalinen testi ennen julkaisua: `scan` tavallisena käyttäjänä ja adminina, `clean` kuivana,
  `clean --apply` vasta kun kuivan ajon lista on luettu läpi.

## 9. Konventiot

- C# 12, nullable päällä, `sealed` oletuksena.
- Kaikki käyttäjälle näkyvät tekstit suomeksi; koodin nimet englanniksi.
- Moduulin `Id` on pieniä kirjaimia ilman välilyöntejä ja ei muutu koskaan julkaisun jälkeen.
- Levyoperaatiot vain `SafeFileDeleter`- ja `PathExpander`-luokkien kautta.

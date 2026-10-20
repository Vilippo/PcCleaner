# PC Cleaner

Oma, modulaarinen siivousohjelma Windows 11:lle. C# / .NET 8.

## Rakenne

```
PcCleaner.sln
├── src/
│   ├── Cleaner.Core/               Ydin: ICleanerModule, mallit, säännöt (JSON), CleanerEngine, lokitus.
│   │   └── Services/               PathGuard, SafeFileDeleter, PathExpander, ProcessChecker, ElevationHelper,
│   │                               Quarantine, BackupSessions, Registry/ (RegistryBackup, RegistryGuard, RegistryPaths)
│   ├── Cleaner.Modules.Files/      Tiedostosiivous + selainvälimuistit (rules/files.json, rules/browsers.json) + roskakori
│   ├── Cleaner.Modules.Startup/    Käynnistysohjelmat: Run-avaimet, käynnistyskansiot, Task Scheduler, Store StartupTask
│   ├── Cleaner.Modules.DiskUsage/  Levytilan analyysi (vain luku) + treemap-algoritmi
│   ├── Cleaner.Modules.Registry/   Rekisterin puhdistus: orvot Uninstall-, tiedostopääte-, ProgId-, MUICache- ja MRU-merkinnät
│   ├── Cleaner.Modules.Uninstall/  Ohjelman poisto: poisto-ohjelman ajo, jäännöshaku, karanteeni
│   ├── Cleaner.Modules.Processes/  Prosessit: käyttäjän istunnon taustaprosessien luokittelu ja lopetus (rules/processes/known.json)
│   ├── Cleaner.Cli/                Komentorivi: scan, clean, modules, uninstall, restore (ModuleFactory)
│   └── Cleaner.UI/                 WinUI 3 -sovellus PcCleaner.exe: MainWindow, ViewModels/, Services/ (UiModuleFactory)
├── tests/
│   └── Cleaner.Tests/              xUnit-testit (moduulit, palvelut, karanteeni, treemap)
├── tools/
│   └── make-icon.ps1               Generoi sovelluskuvakkeen src/Cleaner.UI/Assets/PcCleaner.ico
└── docs/
    ├── ARCHITECTURE.md             Tekninen määrittely
    └── plans/                      Toteutussuunnitelmat
```

## Vaatimukset

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 (17.8+) tai VS Code + C# Dev Kit

## Käännös ja ajo

Graafinen sovellus (WinUI 3, ei vaadi Visual Studiota):

```powershell
dotnet build
dotnet test
dotnet run --project src\Cleaner.UI
```

Exe on kansiossa `src\Cleaner.UI\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\PcCleaner.exe`.
Windows App SDK -ajonaika kopioituu ohjelman viereen, erillistä asennusta ei tarvita.
Graafinen sovellus pyytää aina järjestelmänvalvojan oikeudet (UAC), jotta järjestelmäkansiot ja HKLM-avaimet
ovat käsiteltävissä. Komentorivi toimii tavallisena käyttäjänä.

Julkaisukansio (Release, siirrettävissä toiselle koneelle, vaatii .NET 8 -ajonajan):

```powershell
dotnet publish src\Cleaner.UI -c Release -o publish\PcCleaner
```

Levytilan analyysin sivulla voi rajata kansion, valita top-N:n ja selata treemapia (napsautus avaa alikansion).
Sovelluskuvake generoidaan skriptillä `tools\make-icon.ps1` (ei vaadi kuvankäsittelyohjelmaa).
Puhdistus kysyy kahdesti: ensin kuivan ajon yhteenveto, sitten vahvistus. Korkean riskin kohteet ovat
omassa, oletuksena suljetussa lohkossa eikä niitä valita koskaan automaattisesti.

Komentorivi:

```powershell
cd src\Cleaner.Cli
dotnet run -- modules
dotnet run -- scan
dotnet run -- clean              # kuiva ajo, ei poista mitään
dotnet run -- clean --apply      # poistaa, kysyy vahvistuksen
dotnet run -- scan --include-disabled   # myös oletuksena pois päältä olevat säännöt (Prefetch, Windows.old)
dotnet run -- scan --module startup     # käynnistysohjelmat (ajetaan vain erikseen pyydettäessä)
dotnet run -- clean --module startup --item "startup:hkcu-run:Spotify" --apply   # poista yksi käytöstä
dotnet run -- scan --module diskusage --path C:\Users\%USERNAME% --top 10         # suurimmat kansiot ja tiedostot
dotnet run -- scan --module registry                                             # orvot rekisterimerkinnät (vain luku)
dotnet run -- clean --module registry --item "registry:mru:" --apply              # tyhjennä MRU-listat, varmuuskopio ensin
dotnet run -- scan --module processes                                            # käyttäjän istunnon taustaprosessit, luokiteltuina
dotnet run -- clean --module processes --item "processes:<pid>:<käynnistysaika>" --apply   # lopeta yksi prosessi, kysyy vahvistuksen
dotnet run -- uninstall                                                           # listaa asennetut ohjelmat nimen mukaan
dotnet run -- uninstall --sort date                                               # sama, asennuspäivä uusin ensin
dotnet run -- uninstall --program "7-Zip"                                         # aja poisto-ohjelma, listaa jäänteet kuivana
dotnet run -- uninstall --program "7-Zip" --quiet                                 # sama hiljaisella poistokomennolla
dotnet run -- uninstall --program "7-Zip" --leftovers-only                        # vain jäänteiden haku, ei poisto-ohjelmaa
dotnet run -- uninstall --program "7-Zip" --leftovers-only --apply                # siirrä esivalitut (*) karanteeniin
dotnet run -- uninstall --program "7-Zip" --leftovers-only --apply --all          # kaikki jäänteet, myös High-riskin
dotnet run -- restore                                                             # listaa varmuuskopiot (rekisteri + karanteeni)
dotnet run -- restore 20261019-170000                                             # tuo session takaisin (reg import + karanteeni)
```

Ohjelman poisto (`uninstall`) ajaa ohjelman oman poisto-ohjelman, odottaa sen loppumista ja etsii jäänteet: kansiot,
pikakuvakkeet, rekisteriavaimet ja -arvot. `--program` hyväksyy tarkan nimen tai yksikäsitteisen osan nimestä; usealla
osumalla komento listaa ehdokkaat ja palauttaa koodin 2. Jäänteet tulostetaan kuivana, esivalitut `*`-merkillä.
`--apply` siirtää esivalitut kohteet, `--item`-rajauksen kohteet tai `--all`-valinnalla kaikki, ja kysyy vahvistuksen
`[k/E]` ellei `--yes`. Kansiot ja tiedostot siirtyvät kansioon `%LOCALAPPDATA%\PcCleaner\quarantine\<aikaleima>\`,
rekisteri viedään `.reg`-tiedostoihin samalla aikaleimalla. Tuloste kertoo karanteenikansion ja palautuskomennon.
Vain Win32-ohjelmat; Store-sovellukset eivät näy listassa.

Graafisessa sovelluksessa sivulla "Ohjelman poisto" on hakukenttä, ohjelmalista (nimi, versio, julkaisija, koko) ja
napit "Päivitä lista", "Poista ohjelma" ja "Etsi jäänteet". "Poista ohjelma" näyttää odotustilan
"Odotetaan poisto-ohjelmaa: <nimi>" (Peruuta lopettaa odotuksen, ei poistoa) ja skannaa jäänteet automaattisesti.
Jäänteet näkyvät tavallisessa kategorianäkymässä, ja Puhdista siirtää valitut karanteeniin. "Palauta varmuuskopio..."
listaa sekä rekisterivarmuuskopiot että karanteenisessiot.

Rekisterin puhdistus (`registry`) on rajattu: orvot Uninstall-merkinnät, tiedostotyyppikytkökset, MUICache ja
viimeksi käytettyjen listat. Jokainen muutettava avain viedään `.reg`-tiedostoon kansioon
`%LOCALAPPDATA%\PcCleaner\registry-backups\<aikaleima>\` ennen muutosta. Jos vienti epäonnistuu, kohdetta ei kosketa.
CLSID-, Interface-, TypeLib-, Services-, Drivers-, SharedDLLs- ja Fonts-avaimiin ei kosketa koskaan.

Levytilan analyysi (`diskusage`) vain listaa suurimmat kansiot ja tiedostot. `--apply` poistaa
ainoastaan `--item`-valinnalla rajatut tiedostot, ei koskaan kansioita. Koko aseman skannaus kestää
minuutteja; rajaa `--path`-valinnalla.

Prosessit (`processes`) listaa käyttäjän istunnon prosessit, joiden exe ei ole Windows-kansiossa, kuudessa kategoriassa
(avoimet ohjelmat, puuttuva ohjelmatiedosto, orvot apuprosessit, tunnetut, joutilaat ja aktiiviset taustaprosessit).
Skannaus mittaa suorittimen käyttöä 3 s. Lopetus tarkistaa tunnisteen käynnistysajan, joten kierrätettyä PID:tä ei lopeteta,
ja jokainen lopetus kirjataan lokiin nimellä, PID:llä ja polulla. PcCleanerin oma prosessi ja sen kaikki esivanhemmat
(esimerkiksi pääte ja Windows Terminal, joista komento ajettiin) ovat suojattuja: niitä ei listata eikä lopeteta.
Varoitus: CLI:n `clean` ei suodata esivalinnan tai riskin mukaan. `--apply` ilman tarkkaa `--item`-tunnistetta (tai pelkällä
etuliitteellä `"processes:"`) lopettaa kaikki listatut prosessit, myös avoimet ohjelmat.

Käynnistysohjelmia ei poisteta, vaan ne poistetaan käytöstä samalla tavalla kuin Tehtävienhallinnassa
(`StartupApproved`-avain, Store-sovelluksilla StartupTask-tila). Palautus onnistuu Tehtävienhallinnan Käynnistys-välilehdeltä.

Komentorivillä järjestelmäkansiot (`Windows\Temp`, `SoftwareDistribution`) vaativat, että PowerShell on
käynnistetty järjestelmänvalvojana. Ilman sitä ne vain ohitetaan.

## Turvallisuusperiaatteet

1. **Kuiva ajo on oletus.** Mitään ei poisteta ilman `--apply`.
2. **Ikäraja.** Temp-tiedostot poistetaan vasta 24 h jälkeen, jotta käynnissä olevat ohjelmat eivät kärsi.
3. **PathGuard.** Levyn juuria, Windows-, Program Files- ja käyttäjäkansioita ei poisteta koskaan, vaikka sääntötiedostossa olisi virhe.
4. **Selain auki = ohitus.** Välimuistia ei kosketa, jos selain on käynnissä.
5. **Kaikki lokitetaan** tiedostoon `%LOCALAPPDATA%\PcCleaner\logs\`.
6. **Rekisteri varmuuskopioidaan aina ensin.** Ilman onnistunutta `.reg`-vientiä avainta ei muuteta, ja palautus on yksi komento.
7. **Jäänteet karanteeniin, ei poistoon.** Ohjelman poisto siirtää jäännöskansiot ja -tiedostot karanteeniin, ja `pccleaner restore <aikaleima>` tuo ne takaisin.

## Uuden siivouskohteen lisääminen

Lisää sääntö tiedostoon `src/Cleaner.Modules.Files/rules/files.json`. Koodia ei tarvitse muuttaa.
Katso kentät tiedostosta `docs/ARCHITECTURE.md`.

## Uuden moduulin lisääminen

1. Luo projekti `src/Cleaner.Modules.<Nimi>/`, viittaa `Cleaner.Core`-projektiin.
2. Toteuta `ICleanerModule`.
3. Rekisteröi moduuli `ModuleFactory.CreateAll`-metodissa (CLI) ja `UiModuleFactory.CreateAll`-metodissa (WinUI).

# PC Cleaner

[English](README.md) | **Suomi**

Modulaarinen siivousohjelma Windows 11:lle. C# / .NET 8. Graafinen WinUI 3 -sovellus ja komentorivi.
PC Cleaner on julkinen avoimen lähdekoodin projekti (https://github.com/Vilippo/PcCleaner), MIT-lisenssi.
Ohjelma toimii englanniksi ja suomeksi.

## Rakenne

```
PcCleaner.sln
├── src/
│   ├── Cleaner.Core/               Ydin: ICleanerModule, mallit, säännöt (JSON), CleanerEngine, lokitus.
│   │   ├── Localization/           Loc, LocalizedText: englanninkieliset ja suomenkieliset tekstit
│   │   └── Services/               PathGuard, SafeFileDeleter, PathExpander, ProcessChecker, ElevationHelper,
│   │                               Quarantine, BackupSessions, AppSettings, Registry/ (RegistryBackup, RegistryGuard, RegistryPaths)
│   ├── Cleaner.Modules.Files/      Tiedostosiivous + selainvälimuistit (rules/files.json, rules/browsers.json) + roskakori
│   ├── Cleaner.Modules.Startup/    Käynnistysohjelmat: Run-avaimet, käynnistyskansiot, Task Scheduler, Store StartupTask
│   ├── Cleaner.Modules.DiskUsage/  Levytilan analyysi (vain luku) + treemap-algoritmi
│   ├── Cleaner.Modules.Registry/   Rekisterin puhdistus: orvot Uninstall-, tiedostopääte-, ProgId-, MUICache- ja MRU-merkinnät
│   ├── Cleaner.Modules.Uninstall/  Ohjelman poisto: poisto-ohjelman ajo, jäännöshaku, karanteeni
│   ├── Cleaner.Modules.Processes/  Prosessit: käyttäjän istunnon taustaprosessien luokittelu ja lopetus (rules/processes/known.json)
│   ├── Cleaner.Ai/                 Tekoälyavustaja: Claude API (NuGet Anthropic), skannausdigestit, polkujen siistiminen, arviot
│   ├── Cleaner.Cli/                Komentorivi: scan, clean, modules, uninstall, restore (ModuleFactory)
│   └── Cleaner.UI/                 WinUI 3 -sovellus PcCleaner.exe: MainWindow, Views/ (MainView), ViewModels/,
│       │                           Services/ (UiModuleFactory), Theming/ (Appearance: paletit ja fontit)
│       └── Assets/Fonts/           InterVariable.ttf ja LICENSE-Inter.txt
├── tests/
│   └── Cleaner.Tests/              xUnit-testit (moduulit, palvelut, karanteeni, treemap, tekoäly)
├── tools/
│   ├── make-icon.ps1               Generoi palettikohtaiset kuvakkeet PcCleaner-<paletti>.ico ja PcCleaner.ico/.png (mint)
│   └── snap-window.py              Ottaa sovellusikkunasta kuvakaappauksen PrintWindow-kutsulla
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
dotnet run -- --lang fi scan                                                      # suomenkielinen tuloste, ks. Kieli
```

Ohjelman poisto (`uninstall`) ajaa ohjelman oman poisto-ohjelman, odottaa sen loppumista ja etsii jäänteet: kansiot,
pikakuvakkeet, rekisteriavaimet ja -arvot. `--program` hyväksyy tarkan nimen tai yksikäsitteisen osan nimestä; usealla
osumalla komento listaa ehdokkaat ja palauttaa koodin 2. Jäänteet tulostetaan kuivana, esivalitut `*`-merkillä.
`--apply` siirtää esivalitut kohteet, `--item`-rajauksen kohteet tai `--all`-valinnalla kaikki, ja kysyy vahvistuksen
`[k/E]` (englanniksi `[y/N]`) ellei `--yes`. Kansiot ja tiedostot siirtyvät kansioon `%LOCALAPPDATA%\PcCleaner\quarantine\<aikaleima>\`,
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

## Kieli

Oletuskieli on englanti. Windowsin kieli ei vaikuta siihen.

- Graafisessa sovelluksessa asetussivulla on valitsin `English`/`Suomi`. Se vaihtaa kielen heti
  ilman uudelleenkäynnistystä ja tallentaa valinnan tiedostoon `%LOCALAPPDATA%\PcCleaner\settings.json`.
  Valitsin on pois käytöstä, kun ohjelma skannaa, puhdistaa, palauttaa tai odottaa poisto-ohjelmaa.
- Komentorivillä lippu `--lang en|fi` valitsee kielen yhdelle ajolle ja ohittaa tallennetun valinnan, esimerkiksi
  `dotnet run -- --lang fi scan`. Ilman lippua komentorivi käyttää `settings.json`-tiedoston kieltä tai englantia, jos
  tiedostoa ei ole. Komentorivi ei kirjoita `settings.json`-tiedostoa.

Jo skannatut tulokset (kategorioiden nimet, kohteiden kuvaukset) ja jo kirjoitetut lokirivit jäävät kielelle, jolla
ne syntyivät. Ne päivittyvät uudelle kielelle seuraavalla skannauksella.

## Ulkoasu

Navigointipaneelin rataskuvake avaa asetussivun. Siellä valitaan väripaletti, fontti ja kieli.

- Paletit: Minttu (oletus), Indigo, Teräs, Grafiitti ja Aurinko. Jokaisella paletilla on vaalea ja tumma versio, ja
  sovellus seuraa Windowsin vaalea/tumma-asetusta. Suuren kontrastin tilassa sovellus käyttää Windowsin järjestelmävärejä.
- Fontit: Bahnschrift ja Segoe UI Variable (oletus, Bahnschrift otsikoissa ja luvuissa), Segoe UI Variable ja Inter.
  Bahnschrift ja Segoe UI Variable kuuluvat Windows 11:een. Inter tulee sovelluksen mukana.
- Vaihto näkyy heti koko ikkunassa ilman uudelleenkäynnistystä. Valitsimet ovat pois käytöstä, kun sovellus on kiireinen.
- Otsikko- ja tehtäväpalkin kuvake seuraa palettia. Exen kuvake on aina minttu.

Valinnat tallentuvat tiedostoon `%LOCALAPPDATA%\PcCleaner\settings.json` kenttiin `language`, `palette` ja `font`:

```json
{
  "language": "fi",
  "palette": "indigo",
  "font": "inter"
}
```

Paletin arvot ovat `mint`, `indigo`, `steel`, `graphite` ja `sun`. Fontin arvot ovat `bahnschrift`, `segoe` ja `inter`.
Puuttuva tai tuntematon arvo antaa oletuksen (`mint`, `bahnschrift`), eikä sovellus kirjoita tiedostoa käynnistyksessä.
Yhden asetuksen tallennus säilyttää muut kentät.

## Tekoälyavustaja

Graafisessa sovelluksessa on tekoälyavustaja, joka käyttää Anthropicin Claude API:a. Alapalkin nappi lokinapin
vasemmalla puolella avaa sen 400 px:n paneeliksi oikealle moduulisivun viereen. Avustaja:

- Arvioi valitun moduulin skannaustulokset ("Tarkista tulokset"). Jokainen kategoria ja kohteet, joista on sanottavaa,
  saavat arvion turvallinen, varo tai säilytä (`safe`, `caution`, `keep`) perusteluineen. Arviot näkyvät merkkeinä
  kategoriakorteissa, ja ToolTip näyttää perustelun. Kategorian arvio koskee kaikkia sen kohteita.
- Arvioi koneen kunnon ("Koneen kunto") järjestelmätietojen ja nykyisten skannaustulosten perusteella.
- Vastaa kysymyksiin samassa keskustelussa.

Oletuksena avustaja vain neuvoo eikä muuta valintoja. "Poista varoitetut valinnasta" poistaa valinnan jokaiselta
kohteelta, jonka arvio on varo tai säilytä. Keskustelu on vain muistissa (enintään 20 viestiä), ja "Uusi keskustelu"
tyhjentää sen. Avustaja vastaa sovelluksen kielellä.

Avustaja tarvitsee Anthropicin API-avaimen ympäristömuuttujaan `ANTHROPIC_API_KEY`:

```powershell
setx ANTHROPIC_API_KEY sk-ant-...
```

Muuttujan voi asettaa myös Windowsissa kohdassa Järjestelmä > Tietoja > Järjestelmän lisäasetukset > Ympäristömuuttujat.
Käynnistä PC Cleaner sen jälkeen uudelleen, koska sovellus lukee muuttujan käynnistyksessä. Ilman avainta paneeli
näyttää ilmoituksen ja sen napit ovat pois käytöstä; muu sovellus toimii kuten ennenkin. Avainta ei kirjoiteta
`settings.json`-tiedostoon, lokiin eikä paneeliin.

API-käyttö laskutetaan Anthropic-tililtäsi Anthropicin hinnaston mukaan. Jokainen arvio, kuntotarkistus ja kysymys on
yksi API-kutsu, ja jokainen kutsu lähettää koko keskustelun uudelleen.

Mitä Anthropicille lähetetään:

- Arvio: valitun moduulin tulokset tekstinä. Kategoriasta nimi, riski, kohdemäärä, yhteiskoko ja valittujen määrä;
  kohteesta koko, polku, kuvaus ja onko se valittu. Kategoriasta, jossa on yli 40 kohdetta, lähtee 20 suurinta, ja
  yhdessä arviossa on enintään 400 kohdetta. Loput vain lasketaan.
- Koneen kunto: Windowsin versio, suoritinytimet, muisti, käyntiaika, onko sovellus järjestelmänvalvojana, kiinteät
  asemat (nimi, nimiö, koko, vapaa tila) ja jokaisesta moduulista kohdemäärä, yhteiskoko ja viisi suurinta kategoriaa.
  Ei polkuja.
- Kysymyksesi sellaisenaan.
- Poluissa ja kuvauksissa käyttäjäprofiilin polku korvataan merkinnällä `%USERPROFILE%` ja käyttäjänimi muissa
  `\Users\<nimi>\`-poluissa merkinnällä `%USERNAME%`.

Ei lähetetä: lokia, `settings.json`-tiedostoa, API-avainta, tiedostojen sisältöä eikä erikseen luettuja rekisteriarvoja.
Rekisterikohteista lähtee sama polku ja kuvaus kuin kortissa näkyy.

Auto-tilassa avustaja saa muuttaa valintoja. Kytkin "Auto-tila" on paneelissa ja asetussivulla, ja se on oletuksena
pois. Auto-tilassa arvio valitsee turvalliset kohteet ja poistaa valinnan varo- ja säilytä-kohteilta. Nappi
"Automaattinen siivous" skannaa oletusmoduulit (Tiedostosiivous, Selainten välimuistit, Roskakori), arvioi jokaisen
moduulin, jolla on tuloksia, soveltaa valinnat, ajaa kuivan ajon ja näyttää yhden vahvistusdialogin, jossa ovat
avustajan yhteenvedot, kuivan ajon kohdemäärä ja koko sekä ohitetut kohteet. Mitään ei poisteta ennen vahvistusta, ja
Peruuta pysäyttää minkä tahansa vaiheen ennen dialogia. Rajat:

- Korkean riskin kohteita ei valita koskaan, vaikka avustaja arvioisi ne turvallisiksi.
- Erikseen valittavat moduulit (Käynnistysohjelmat, Levytilan analyysi, Rekisterin puhdistus, Ohjelman poisto,
  Prosessit) eivät ole koskaan mukana.
- Tiedostot eivät mene roskakoriin. Tavallinen Puhdista-nappi kysyy edelleen kahdesti.

Asetukset tiedostossa `settings.json`:

```json
{
  "aiModel": "claude-opus-5-5",
  "aiAutoMode": false
}
```

`aiModel` on mallin tunnus sellaisenaan (oletus `claude-opus-5-5`). Sovelluksessa ei ole sille valitsinta, joten muokkaa
tiedostoa ja käynnistä sovellus uudelleen. `aiAutoMode` tallentuu kytkimestä. Puuttuva kenttä antaa oletuksen.

## Turvallisuusperiaatteet

1. **Kuiva ajo on oletus.** Mitään ei poisteta ilman `--apply`.
2. **Ikäraja.** Temp-tiedostot poistetaan vasta 24 h jälkeen, jotta käynnissä olevat ohjelmat eivät kärsi.
3. **PathGuard.** Levyn juuria, Windows-, Program Files- ja käyttäjäkansioita ei poisteta koskaan, vaikka sääntötiedostossa olisi virhe.
4. **Selain auki = ohitus.** Välimuistia ei kosketa, jos selain on käynnissä.
5. **Kaikki lokitetaan** tiedostoon `%LOCALAPPDATA%\PcCleaner\logs\`.
6. **Rekisteri varmuuskopioidaan aina ensin.** Ilman onnistunutta `.reg`-vientiä avainta ei muuteta, ja palautus on yksi komento.
7. **Jäänteet karanteeniin, ei poistoon.** Ohjelman poisto siirtää jäännöskansiot ja -tiedostot karanteeniin, ja `pccleaner restore <aikaleima>` tuo ne takaisin.

## Vastuuvapaus

PC Cleaner poistaa tiedostoja, muuttaa rekisteriä ja käynnistysasetuksia sekä lopettaa prosesseja. Ohjelma
toimitetaan sellaisenaan ilman minkäänlaista takuuta. Tekijä ei vastaa vahingoista eikä tietojen menetyksestä,
jotka aiheutuvat ohjelman käytöstä. Käyttö on omalla vastuullasi. Tee varmuuskopio tärkeistä tiedoista ennen
puhdistusta.

## Uuden siivouskohteen lisääminen

Lisää sääntö tiedostoon `src/Cleaner.Modules.Files/rules/files.json`. Koodia ei tarvitse muuttaa.
Kirjoita `name` ja `description` englanniksi ja suomenkielinen teksti kenttiin `nameFi` ja `descriptionFi`.
Jos suomenkieliset kentät puuttuvat, englanninkielinen teksti näkyy molemmilla kielillä.
Katso kentät tiedostosta `docs/ARCHITECTURE.md`.

## Uuden moduulin lisääminen

1. Luo projekti `src/Cleaner.Modules.<Nimi>/`, viittaa `Cleaner.Core`-projektiin.
2. Toteuta `ICleanerModule`. Kirjoita käyttäjälle näkyvät tekstit `Loc.T("English", "Suomi")`-pareina.
3. Rekisteröi moduuli `ModuleFactory.CreateAll`-metodissa (CLI) ja `UiModuleFactory.CreateAll`-metodissa (WinUI).

## Lisenssi

PC Cleaner on julkaistu MIT-lisenssillä, ks. [LICENSE](LICENSE). Ohjelma toimitetaan sellaisenaan ilman
minkäänlaista takuuta, ja käyttö on omalla vastuulla.

Inter-fontti (`src/Cleaner.UI/Assets/Fonts/InterVariable.ttf`) on SIL Open Font License 1.1 -lisenssillä, ks.
`src/Cleaner.UI/Assets/Fonts/LICENSE-Inter.txt`.

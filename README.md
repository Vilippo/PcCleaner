# PC Cleaner

Oma, modulaarinen siivousohjelma Windows 11:lle. C# / .NET 8.

## Rakenne

```
PcCleaner.sln
├── src/
│   ├── Cleaner.Core/            Ydin: rajapinnat, säännöt, turvapalvelut, moottori. Ei UI:ta.
│   ├── Cleaner.Modules.Files/   Tiedostosiivous + selainvälimuistit (sääntöpohjainen) + roskakori
│   └── Cleaner.Cli/             Komentorivikäyttöliittymä
├── tests/
│   └── Cleaner.Tests/           xUnit-testit
└── docs/
    └── ARCHITECTURE.md          Tekninen määrittely
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
dotnet run -- restore                                                             # listaa rekisterivarmuuskopiot
dotnet run -- restore 20261019-170000                                             # tuo yksi takaisin (reg import)
```

Rekisterin puhdistus (`registry`) on rajattu: orvot Uninstall-merkinnät, tiedostotyyppikytkökset, MUICache ja
viimeksi käytettyjen listat. Jokainen muutettava avain viedään `.reg`-tiedostoon kansioon
`%LOCALAPPDATA%\PcCleaner\registry-backups\<aikaleima>\` ennen muutosta. Jos vienti epäonnistuu, kohdetta ei kosketa.
CLSID-, Interface-, TypeLib-, Services-, Drivers-, SharedDLLs- ja Fonts-avaimiin ei kosketa koskaan.

Levytilan analyysi (`diskusage`) vain listaa suurimmat kansiot ja tiedostot. `--apply` poistaa
ainoastaan `--item`-valinnalla rajatut tiedostot, ei koskaan kansioita. Koko aseman skannaus kestää
minuutteja; rajaa `--path`-valinnalla.

Käynnistysohjelmia ei poisteta, vaan ne poistetaan käytöstä samalla tavalla kuin Tehtävienhallinnassa
(`StartupApproved`-avain). Palautus onnistuu Tehtävienhallinnan Käynnistys-välilehdeltä.

Järjestelmäkansiot (`Windows\Temp`, `SoftwareDistribution`) vaativat, että PowerShell on
käynnistetty järjestelmänvalvojana. Ilman sitä ne vain ohitetaan.

## Turvallisuusperiaatteet

1. **Kuiva ajo on oletus.** Mitään ei poisteta ilman `--apply`.
2. **Ikäraja.** Temp-tiedostot poistetaan vasta 24 h jälkeen, jotta käynnissä olevat ohjelmat eivät kärsi.
3. **PathGuard.** Levyn juuria, Windows-, Program Files- ja käyttäjäkansioita ei poisteta koskaan, vaikka sääntötiedostossa olisi virhe.
4. **Selain auki = ohitus.** Välimuistia ei kosketa, jos selain on käynnissä.
5. **Kaikki lokitetaan** tiedostoon `%LOCALAPPDATA%\PcCleaner\logs\`.
6. **Rekisteri varmuuskopioidaan aina ensin.** Ilman onnistunutta `.reg`-vientiä avainta ei muuteta, ja palautus on yksi komento.

## Uuden siivouskohteen lisääminen

Lisää sääntö tiedostoon `src/Cleaner.Modules.Files/rules/files.json`. Koodia ei tarvitse muuttaa.
Katso kentät tiedostosta `docs/ARCHITECTURE.md`.

## Uuden moduulin lisääminen

1. Luo projekti `src/Cleaner.Modules.<Nimi>/`, viittaa `Cleaner.Core`-projektiin.
2. Toteuta `ICleanerModule`.
3. Rekisteröi moduuli `ModuleFactory.CreateAll`-metodissa (CLI) ja myöhemmin WinUI-sovelluksen vastaavassa.

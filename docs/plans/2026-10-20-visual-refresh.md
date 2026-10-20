# Plan: Visuaalinen päivitys: valittavat väripaletit ja fontit, uusi asettelu
Branch: feature/visual-refresh
Created: 2026-10-20

## Background & scope

WinUI 3 -sovellus (`src/Cleaner.UI`) käyttää WinUI:n oletusulkoasua: järjestelmän korostusväri, Segoe UI,
pelkkää tekstiä otsikon alla ja tyhjä sisältöalue ennen skannausta. Käyttäjä pyysi uudet värit ja fontin sekä
paremman asettelun. Käyttäjän päätökset (kysytty 20.10.2026):

- Viisi väripalettia, joista käyttäjä valitsee mieleisensä asetuksista: Minttu, Indigo, Teräs, Grafiitti ja Aurinko.
  Kaksi viimeistä ovat tarkoituksella erilaisia (yksivärinen ja lämmin hiekka).
- Kolme fonttivaihtoehtoa, joista käyttäjä valitsee: Bahnschrift + Segoe UI Variable (oletus), Segoe UI Variable,
  Inter (OFL-lisenssi, pakataan sovellukseen).
- Vaalea ja tumma teema Windowsin asetuksen mukaan, kuten nytkin. Jokaisella paletilla on molemmat.
- Asettelu: oma otsikkopalkki ja Mica-tausta, riskimerkit ja lukulaatat otsikon alla, tyhjä tila sisältöalueen
  keskelle Skannaa-napin kanssa, loki liukupaneeliin oikealta. Lisäksi kategoriat kortteina riskiraidalla ja
  tiiviimpi alapalkki.

Valinnat tallennetaan samaan `settings.json`-tiedostoon kuin kieli ja vaihtuvat heti ilman uudelleenkäynnistystä.
Asetukset saavat oman sivun navigointipaneelin rataskuvakkeen takaa; kielivalitsin siirtyy sinne.

Ulkopuolella: CLI (ei muutu), MSIX, high contrast -teeman oma muotoilu (siinä käytetään Windowsin järjestelmävärejä,
ks. REQ-4), uusi
sovelluslogo (nykyinen luuta säilyy, vain värit seuraavat palettia), mukautettu teeman pakotus (vain tumma / vain
vaalea), animaatiot.

### Todennettu kokeilulla 20.10.2026 (älä tutki uudelleen)

Kokeilu tehtiin tässä worktreessä ja peruttiin. Tulokset:

1. Paikallinen TTF latautuu pakkaamattomassa WinUI 3 -sovelluksessa (Windows App SDK 1.8) muodossa
   `ms-appx:///Assets/Fonts/InterVariable.ttf#Inter`, kun tiedosto on csproj:ssa `<Content ... CopyToOutputDirectory="PreserveNewest">`.
   Muuttuvan fontin painoakseli toimii: `FontWeight=SemiBold` piirtyi lihavoituna samasta tiedostosta.
2. Sovellustason `ResourceDictionary`, jolla on `ThemeDictionaries["Default"]` (tumma) ja `["Light"]`, lisättynä
   `Application.Current.Resources.MergedDictionaries`-listaan ennen ikkunan luontia, ohittaa WinUI:n omat avaimet
   `AccentFillColorDefaultBrush`, `NavigationViewSelectionIndicatorForeground` ja `ContentControlThemeFontFamily`:
   napit, NavigationView ja InfoBar piirtyivät annetulla korostusvärillä ja fontilla.
3. Sanakirjan vaihto ajon aikana päivitti ohjauselementit (`{ThemeResource}`-viittaukset) mutta EI TextBlock-tyylejä
   (`TitleTextBlockStyle`, `BodyTextBlockStyle`): otsikko jäi vanhaan fonttiin. Siksi vaihto tehdään luomalla
   ikkunan sisältönäkymä (`MainView`) uudelleen sanakirjan vaihdon jälkeen. Se ratkaisee molemmat.
4. `app.manifest` vaatii `requireAdministrator`: sovelluksen käynnistys Claude-sessiosta pysähtyy UAC-kehotteeseen,
   jota sessio ei voi hyväksyä. Automaattista kuvakaappausta varten manifestiin voi vaihtaa väliaikaisesti
   `level="asInvoker"` (EI commitoida), ja ikkunan voi kaapata PrintWindow-kutsulla (`tools/snap-window.py`, T4),
   koska `CopyFromScreen` epäonnistuu sessiosta. Vaihtoehtoisesti käyttäjä hyväksyy UAC-kehotteen itse.
5. Inter 4.1: `https://github.com/rsms/inter/releases/download/v4.1/Inter-4.1.zip`,
   SHA256 `9883FDD4A49D4FB66BD8177BA6625EF9A64AA45899767DDE3D36AA425756B11E`, 33 707 794 tavua. Sisältää
   `InterVariable.ttf` (879 708 tavua, SHA256 `4989B125924991B90D05B2D16E0E388C48F7D5BB8B30539BBF9C755278D0CCAF`)
   ja `LICENSE.txt` (SIL Open Font License 1.1, SHA256 `262481E844521B326F5ECD053E59B98C8B2DA78C8EE1BDBB6E8174305E54935A`).
6. Koneella on valmiina fontit Segoe UI Variable (Display, Text, Small), Bahnschrift (muuttuva, painot Light–SemiBold)
   ja Cascadia Mono. Ne kuuluvat Windows 11:een, joten niitä ei pakata.
7. Nykyinen build: `dotnet build PcCleaner.sln` 0 varoitusta / 0 virhettä, `dotnet test` 214/214 läpi. SDK 10.0.402.
8. WinUI:n Microsoft.WinUI.props sisältää oletuksena `<Content Include="Assets/**/*.*">` (`EnableDefaultAssets`), joten kaikki
   `src/Cleaner.UI/Assets`-kansion tiedostot kopioituvat build- ja publish-kansioon ilman csproj-rivejä (bin-kansiossa on jo
   `Assets/PcCleaner.png`, jota csproj ei mainitse). Csproj:ta ei tarvitse muuttaa fontteja tai kuvakkeita varten.
   Huomio: kokeilun jäljiltä `src/Cleaner.UI/bin/x64/Debug/.../Assets/Fonts/InterVariable.ttf` on jo olemassa; se ei todista
   mitään ennen kuin bin/obj on siivottu.

## Scales

Hyväksytyt suunnitteluarvot. Tehtävät viittaavat tähän osioon; arvoja ei keksitä uudelleen.

### Paletit

Jokaisella paletilla on 14 väriä kahdessa tilassa. Avain → mihin sitä käytetään:

| Avain | Käyttö |
|---|---|
| Accent | korostustäyttö: Puhdista-nappi, valintaruudun täyttö, valintapalkki, ProgressBar |
| AccentHover / AccentPressed | korostustäytön hover- ja painettu tila |
| OnAccent | teksti korostustäytön päällä |
| AccentText | korostusväri tekstinä ja kuvakkeena pinnoilla (Valittu-laatan luku) |
| PaneBackground | navigointipaneeli ja otsikkopalkki Mican päällä (alpha 0xB3 = 70 %) |
| ContentBackground | sisältöalue ja alapalkki |
| CardBackground / CardBorder / CardHover | kortit, laatat, merkit |
| TextPrimary / TextSecondary / TextTertiary | teksti, toissijainen teksti, vihjeet ja tyhjän tilan kuvake |
| Divider | viivat |

| Paletti | Tila | Accent | AccentHover | AccentPressed | OnAccent | AccentText | PaneBackground | ContentBackground | CardBackground | CardBorder | CardHover | TextPrimary | TextSecondary | TextTertiary | Divider |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| mint (Mint / Minttu) | Dark | #1D9E75 | #2FB388 | #178A66 | #04342C | #5DCAA5 | #B30E1716 | #152120 | #1A2A27 | #243733 | #1F302C | #E6F0ED | #9FB8B0 | #6B8580 | #223330 |
| mint | Light | #0F6E56 | #138163 | #0B5A46 | #FFFFFF | #0F6E56 | #B3E9F1EC | #F6F9F7 | #FFFFFF | #D5E2DC | #EEF5F1 | #0F1F1A | #4C6B61 | #7E978E | #D5E2DC |
| indigo (Indigo / Indigo) | Dark | #7F77DD | #928BE3 | #6E66CF | #1A1540 | #AFA9EC | #B3121320 | #1A1B2C | #22233A | #2E2F48 | #282A42 | #E9E8F5 | #A9A7C8 | #6E6D8C | #2A2B42 |
| indigo | Light | #534AB7 | #5F56C4 | #453D9E | #FFFFFF | #534AB7 | #B3ECEBF7 | #F7F7FB | #FFFFFF | #DBDAEC | #F0EFF8 | #16152B | #55547A | #8685A3 | #DBDAEC |
| steel (Steel / Teräs) | Dark | #378ADD | #4E99E3 | #2C7AC8 | #042C53 | #85B7EB | #B30F161F | #16202C | #1C2938 | #273646 | #223141 | #E6EDF5 | #9FB1C6 | #5F7389 | #223040 |
| steel | Light | #185FA5 | #1C6BB8 | #134E88 | #FFFFFF | #185FA5 | #B3E8EEF5 | #F5F8FB | #FFFFFF | #D6DFE9 | #EDF2F7 | #0E1A26 | #4A5D72 | #7B8CA0 | #D6DFE9 |
| graphite (Graphite / Grafiitti) | Dark | #F1EFE8 | #FFFFFF | #D3D1C7 | #141414 | #F1EFE8 | #B3101010 | #181818 | #202020 | #2E2E2E | #262626 | #F1EFE8 | #A8A69E | #6E6D68 | #2A2A2A |
| graphite | Light | #1C1C1A | #2C2C2A | #000000 | #FFFFFF | #1C1C1A | #B3EDECE8 | #F7F6F3 | #FFFFFF | #DCDAD3 | #F1EFE8 | #141414 | #5F5E5A | #8A8984 | #DCDAD3 |
| sun (Sun / Aurinko) | Dark | #E07A52 | #E98B66 | #D2683F | #2B1208 | #F0997B | #B31E1915 | #26201B | #2E2721 | #3C332C | #352D26 | #F3EDE6 | #BDAE9E | #85776A | #3A312A |
| sun | Light | #B8471F | #C4532B | #993C1D | #FFFFFF | #993C1D | #B3F1E9DD | #F8F3EB | #FFFFFF | #E3D8C8 | #F3ECE0 | #2A1F16 | #6B5A4B | #9B8A7A | #E3D8C8 |

Järjestys asetussivulla ja `Appearance.Palettes`-listassa: mint, indigo, steel, graphite, sun. Oletus mint.

Treemapin kategoriavärit (6 kpl, samat molemmissa tiloissa, lohkot täytetään alpha 170 kuten nyt):

| Paletti | Värit järjestyksessä |
|---|---|
| mint | #1D9E75 #378ADD #7F77DD #EF9F27 #D4537E #888780 |
| indigo | #7F77DD #1D9E75 #378ADD #EF9F27 #D4537E #888780 |
| steel | #378ADD #1D9E75 #7F77DD #EF9F27 #D4537E #888780 |
| graphite | #B4B2A9 #5F5E5A #D3D1C7 #888780 #444441 #F1EFE8 |
| sun | #D85A30 #1D9E75 #378ADD #7F77DD #EF9F27 #888780 |

### Riskivärit

Samat kaikille paleteille, vain tila vaihtelee. Fill = raita ja piste, Tint = merkin tausta, Text = merkin teksti.

| Tila | RiskLow Fill / Tint / Text | RiskMedium Fill / Tint / Text | RiskHigh Fill / Tint / Text |
|---|---|---|---|
| Dark | #97C459 / #27500A / #C0DD97 | #EF9F27 / #633806 / #FAC775 | #F09595 / #791F1F / #F7C1C1 |
| Light | #639922 / #EAF3DE / #27500A | #EF9F27 / #FAEEDA / #633806 | #E24B4A / #FCEBEB / #791F1F |

### Fontit

| Id | Nimi (EN / FI) | Heading | Body (myös `ContentControlThemeFontFamily`) | Number |
|---|---|---|---|---|
| bahnschrift (oletus) | Bahnschrift and Segoe UI / Bahnschrift ja Segoe UI | `Bahnschrift` | `Segoe UI Variable Text, Segoe UI` | `Bahnschrift` |
| segoe | Segoe UI Variable / Segoe UI Variable | `Segoe UI Variable Display, Segoe UI` | `Segoe UI Variable Text, Segoe UI` | `Segoe UI Variable Text, Segoe UI` |
| inter | Inter / Inter | `ms-appx:///Assets/Fonts/InterVariable.ttf#Inter` | sama | sama |

Mono (loki): `Cascadia Mono, Consolas` kaikissa. Järjestys asetussivulla: bahnschrift, segoe, inter.

Tyyppiasteikko (px): moduulin otsikko 28 SemiBold (Heading), asetussivun otsikko 28 SemiBold, osion otsikko 16 SemiBold
(Heading), tyhjän tilan otsikko 18 SemiBold (Heading), laatan luku 22 SemiBold (Number), leipäteksti 14 (Body),
kortin nimi 14 SemiBold (Body), caption ja merkit 12 (Body), loki 12 (Mono). Luvut (koot, kpl) Number-fontilla.

### Mitat

Ikkuna oletus 1200 × 820, minimi 960 × 640. Otsikkopalkki 36 px (kuvake 16 px + nimi 12 px, vasen marginaali 12).
Navigointipaneeli 240 px, aina auki, ei toggle-nappia. Sisältöalue: padding 32/24/32/16, sisällön enimmäisleveys
1100 px vasemmalle tasattuna. Kortin kulmasäde 8, raita 3 px, kortin padding 12/10, rivin minimikorkeus 32.
Merkki (badge): kulmasäde 10, padding 8/2, fontti 12, 8 px piste vasemmalla riskimerkissä. Laatta 160 × 64, padding 14/10.
Alapalkki 56 px + 4 px ProgressBar sen yläreunassa kiireisenä. Lokipaneeli 420 px oikealta, Overlay.
Treemap 300 px korkea kuten nyt. Välit 8/12/16/24.

## Requirements

Ydin

- REQ-1: `AppSettings` saa kentät `Palette` (JSON `palette`, oletus `"mint"`) ja `Font` (JSON `font`, oletus
  `"bahnschrift"`). Tiedosto, josta kentät puuttuvat tai jossa ne ovat `null`, antaa oletukset. `Load` ei heitä koskaan.
- REQ-2: `AppSettings.Update(Action<AppSettings> change, string? path = null)` lataa tiedoston, soveltaa muutoksen ja
  tallentaa, joten yhden asetuksen tallennus säilyttää muut. Kielen tallennus UI:ssa käyttää tätä.

Ulkoasun hallinta (UI)

- REQ-3: `Cleaner.UI.Theming.Appearance` tarjoaa listat `Palettes` (5, Scales-järjestys) ja `Fonts` (3) sekä
  `ResolvePalette(string?)` ja `ResolveFont(string?)`, jotka palauttavat oletuksen tuntemattomalle, tyhjälle tai
  null-arvolle kirjainkoosta riippumatta.
- REQ-4: `Appearance.Apply(palette, font)` vaihtaa sovellusresursseihin sanakirjan, jossa on Scales-osion kaikki
  väri- ja fonttiavaimet tumman (`Default`) ja vaalean (`Light`) teemasanakirjan alla, ja ohittaa WinUI:n omat
  korostusavaimet niin, että NavigationView'n valintapalkki, AccentButtonStyle, CheckBox, RadioButtons ja ProgressBar
  piirtyvät paletin korostusvärillä. Edellinen sanakirja poistetaan ennen uuden lisäämistä. Sanakirjassa on myös
  `HighContrast`-teemasanakirja, jossa samat `App*`- ja `Risk*`-avaimet osoittavat Windowsin järjestelmäväreihin eikä
  WinUI:n korostusavaimia ohiteta, jotta suuren kontrastin tila säilyy järjestelmän näköisenä.
- REQ-5: Sovellus käynnistyy asetustiedoston paletilla ja fontilla ja Windowsin vaalea/tumma-asetuksen mukaisella
  teemalla. Teeman vaihto Windowsissa ajon aikana vaihtaa värit ilman uudelleenkäynnistystä (ThemeDictionaries hoitaa).
- REQ-6: Paletin tai fontin vaihto asetussivulla näkyy heti koko ikkunassa (otsikkopalkki, paneeli, sisältö, alapalkki,
  napit, tekstit) ilman uudelleenkäynnistystä, tallentuu `settings.json`-tiedostoon ja on voimassa seuraavassa
  käynnistyksessä. Tallennuksen epäonnistuminen kirjataan varoituksena lokiin eikä estä vaihtoa.
- REQ-7: Otsikko- ja tehtäväpalkin kuvake seuraa palettia: `Assets/PcCleaner-<id>.ico` jokaiselle viidelle paletille,
  ja `PcCleaner.ico` (exen kuvake) on mint-värinen. Kuvakkeet tuottaa `tools/make-icon.ps1`.
- REQ-8: Inter toimitetaan tiedostona `src/Cleaner.UI/Assets/Fonts/InterVariable.ttf` lisenssitiedoston
  `LICENSE-Inter.txt` kanssa, ja molemmat kopioituvat build- ja publish-kansioon (WinUI:n oletusglob `Assets/**/*.*`,
  todennettu kohta 8).

Asetussivu

- REQ-9: NavigationView'n asetuskohde (rataskuvake, `IsSettingsVisible=True`) avaa sisältöalueelle asetussivun, jolla on
  osiot Ulkoasu (väripaletti ja fontti) ja Kieli. Paletit valitaan `RadioButtons`-listasta, jossa jokaisella on nimi ja
  kolmen ympyrän värinäyte (Dark.CardBackground, Light.CardBackground, Dark.Accent, kukin 16 px ja 1 px CardBorder-reunus).
  Fontit valitaan `RadioButtons`-listasta, jossa jokaisella on nimi ja näyteteksti omalla fontillaan.
- REQ-10: Kielivalitsin on asetussivulla (`RadioButtons`: English, Suomi) ja poistuu navigointipaneelin alaosasta.
  Se on pois käytöstä kiireisenä kuten ennen (`IsIdle`). Kielen vaihto päivittää myös asetussivun tekstit ja
  palettien nimet heti.
- REQ-11: Näkymän uudelleenluonti ulkoasun vaihdossa säilyttää valitun sivun (asetussivu pysyy auki), valitun moduulin,
  skannaustulokset, valinnat ja lokirivit (kaikki ovat ViewModelissa).

Asettelu

- REQ-12: Ikkunalla on Mica-tausta (`MicaBackdrop`) ja oma otsikkopalkki: sisältö jatkuu otsikkopalkkiin
  (`ExtendsContentIntoTitleBar`), palkissa on paletin kuvake ja ikkunan otsikko (`TitleText`), ja palkki toimii
  raahausalueena (`SetTitleBar`). Ikkunan minimikoko on 960 × 640.
- REQ-13: Moduulisivun otsikon alla on merkkirivi: riskimerkki (RiskX Tint -tausta, RiskX Text -teksti, 8 px RiskX Fill
  -piste), tarvittaessa järjestelmänvalvojamerkki ja "valitaan erikseen" -merkki (CardBackground + CardBorder).
  Nykyiset caption-tekstit RiskText, AdminHint ja OptInHint korvautuvat merkeillä; AdminHint ja OptInHint ovat
  merkin ToolTip-tekstinä.
- REQ-14: Skannauksen jälkeen (`HasScanned`) otsikon alla on kaksi laattaa: Löytyi (kohdemäärä, tai koko kun se on
  tiedossa) ja Valittu (valittu määrä, tai koko kun se on tiedossa; AccentText-väri). Laatat päivittyvät valintojen
  mukana ja katoavat `ClearScan`-kutsussa.
- REQ-15: Kun moduulia ei ole skannattu (`!HasScanned && Status == NotScanned`), moduuli ei ole ohjelman poisto
  (`Id != "uninstall"`, sen sivulla on oma ohjelmalista ja Status alkaa myös NotScanned-tilasta) eikä sovellus ole
  kiireinen, sisältöalueen keskellä on tyhjä tila: moduulin kuvake 40 px TextTertiary-värillä, otsikko "Nothing scanned
  yet" / "Ei vielä skannattu", yksi rivi selitettä ja Skannaa-painike (AccentButtonStyle, `ScanCommand`). Levytilan
  analyysin omat ohjaimet pysyvät otsikon alla ja tyhjä tila näkyy niiden alla. Peruutettu skannaus (`ScanCanceled`)
  piilottaa tyhjän tilan ja näyttää tilatekstin.
- REQ-16: Jokainen kategoria on kortti (CardBackground, 1 px CardBorder, kulmasäde 8) ja kortin vasemmassa reunassa on
  3 px raita kategorian riskin Fill-värillä. Kortin otsikkorivillä on valintaruutu, nimi, riskiteksti (RiskX Text -väri)
  ja oikealla yhteenveto Number-fontilla. Expander avaa rivit kortin sisällä ilman omaa taustaa tai reunaa.
  Korkean riskin lohko on kortti RiskHigh-raidalla ja RiskHigh Tint -otsikkotaustalla.
- REQ-17: Alapalkki on 56 px korkea: vasemmalla valintateksti (kiireisenä BusyTitle ja ProgressMessage), oikealla
  Palauta varmuuskopio... (kun näkyvissä), Peruuta (kiireisenä), Skannaa ja Puhdista (AccentButtonStyle) sekä
  lokinappi (FontIcon ``, ToolTip "Log" / "Loki"). Kiireisenä alapalkin yläreunassa on 4 px ProgressBar koko leveydeltä.
- REQ-18: Loki on `SplitView`-paneelissa oikealla (`PanePlacement=Right`, `DisplayMode=Overlay`, 420 px), jonka
  lokinappi avaa ja sulkee. Paneelissa on otsikko, sulkemisnappi, lokirivit Mono-fontilla 12 px, Tyhjennä-nappi ja
  lokitiedoston polku. Alapalkin Expander poistuu.
- REQ-19: Navigointipaneeli on 240 px, aina auki, ilman toggle-nappia ja ilman PaneTitle-otsikkoa (otsikkopalkki
  nimeää sovelluksen). Paneelin tausta on PaneBackground Mican päällä. PaneFooter-alueella on Skannaa kaikki -nappi.
- REQ-20: Treemapin lohkot käyttävät paletin kategoriavärejä, lohkon reuna on ContentBackground-värinen 1 px ja
  lohkon tekstin väri on #FFFFFF tai #1A1A1A sen mukaan, kummalla on suurempi WCAG-kontrastisuhde näkyvään väriin.
  Näkyvä väri lasketaan yhdistämällä kategoriaväri lohkon alphalla (170 tai 90) ContentBackground-värin päälle
  (`c = a·fg + (1−a)·bg` kanavittain, a = alpha/255), ja kontrasti kaavalla `(L1 + 0.05) / (L2 + 0.05)`, jossa L on
  sRGB-linearisoitu suhteellinen luminanssi `0.2126 R' + 0.7152 G' + 0.0722 B'`. Esimerkki: amber #EF9F27 ilman
  taustaa saa L ≈ 0,43, jolloin #1A1A1A (suhde 8,0) voittaa valkoisen (2,2). Treemap piirretään uudelleen, kun
  Windowsin teema vaihtuu (`ActualThemeChanged`), koska tausta vaihtuu.
- REQ-21: Kaikki uudet näkyvät tekstit ovat `UiText`-luokassa muodossa `Loc.T(en, fi)` ja päivittyvät kielen vaihtuessa
  (`Refresh`). Uudet tekstit ja niiden käännökset:
  Settings / Asetukset · Appearance / Ulkoasu · Color palette / Väripaletti · Font / Fontti · Language / Kieli ·
  Nothing scanned yet / Ei vielä skannattu · "Scan shows what can be removed. Nothing is deleted before you confirm." /
  "Skannaus näyttää, mitä voi poistaa. Mitään ei poisteta ennen vahvistustasi." · Found / Löytyi · Selected / Valittu ·
  Administrator / Järjestelmänvalvoja · Manual selection / Valitaan erikseen · Close / Sulje ·
  fonttinäyte "File cleanup · Found 1,204 items, 727 MB" / "Tiedostosiivous · Löytyi 1 204 kohdetta, 727 MB".
- REQ-22: `README.md`, `README.fi.md`, `docs/ARCHITECTURE.md` (luku 7) ja `HANDOFF.md` kuvaavat asetussivun, paletit,
  fontit, `settings.json`-kentät `palette` ja `font`, Inter-lisenssin ja kuvakkeiden generoinnin.

## Edge cases

- EDGE-1: `settings.json` sisältää tuntemattoman paletin tai fontin (esim. `"palette": "neon"`) → sovellus käyttää
  oletusta, ei kirjoita tiedostoa käynnistyksessä, ja asetussivu näyttää oletuksen valittuna.
- EDGE-2: Ulkoasun vaihto levytilan analyysin treemapin ollessa avattuna alikansioon → näkymä luodaan uudelleen ja
  treemap piirtyy juuresta (`TreemapRoot`), alikansiopino nollautuu. Sallittu, dokumentoidaan koodikommenttiin.
- EDGE-3: Ulkoasun vaihto skannauksen tai puhdistuksen aikana → asetussivun paletti- ja fonttivalitsimet ovat pois
  käytöstä kiireisenä (`IsIdle`), kuten kielivalitsin, jotta näkymää ei luoda uudelleen kesken edistymisnäytön.
- EDGE-4: Mica ei ole käytettävissä (vanha Windows 10 -versio tai etätyöpöytä) → `MicaController.IsSupported()` on false,
  `SystemBackdrop` jätetään asettamatta ja MainView'n juuren tausta asetetaan `AppContentBackgroundBrush`-arvoon
  (muuten läpinäkyvä), jolloin 70 % peittävä PaneBackground piirtyy ContentBackground-värin päälle.
- EDGE-5: Graphite-paletin korostus on neutraali (tummassa valkoinen, vaaleassa musta) → Puhdista-napin teksti on
  OnAccent-värinen ja valintapalkki valkoinen/musta; riskivärit pysyvät värillisinä.
- EDGE-6: Vaihto kieleen kesken asetussivun → palettien ja fonttien nimet, näyteteksti ja osioiden otsikot päivittyvät
  ilman näkymän uudelleenluontia (vain `PropertyChanged`).

## Constraints

- CON-1: `settings.json`-tiedoston olemassa olevat kentät ja muoto säilyvät (`{ "language": "fi" }` ilman uusia kenttiä on
  yhä kelvollinen). Kirjoitus on camelCase ja sisennetty kuten nyt.
- CON-2: Ei uusia NuGet-paketteja. Fonttitiedosto on ainoa uusi binääri (InterVariable.ttf, 879 708 tavua).
- CON-3: Build pysyy 0 varoitusta / 0 virhettä `dotnet build PcCleaner.sln`.
- CON-4: Kaikki värit ja fontit tulevat `{ThemeResource}`-viittauksina Appearance-sanakirjan avaimiin. XAML:ssa ei ole
  kovakoodattuja heksavärejä eikä fonttinimiä (poikkeus: treemapin lohkoteksti REQ-20, joka lasketaan koodissa).
- CON-5: `app.manifest` pysyy `requireAdministrator`-tasolla commiteissa. Kuvakaappauksia varten tehty `asInvoker`-vaihto
  perutaan ennen stagingia.

## Assumptions

- Oletuspaletti on mint ja oletusfontti bahnschrift, koska ne olivat suositukset ja käyttäjä ei nimennyt oletusta.
- Asetukset avautuvat NavigationView'n omasta asetuskohteesta eikä erillisestä dialogista, koska se on WinUI:n tapa ja
  sisältöalue on jo olemassa.
- Näkymä luodaan uudelleen ulkoasun vaihdossa (kokeilu 3). Vaihtoehto, jossa jokainen tekstityyli viittaisi
  ThemeResource-fonttiin, ei kata WinUI:n omia tyylejä.
- `Appearance` on staattinen luokka, kuten `Loc`, koska sovelluksessa on yksi ikkuna ja XAML-resurssit ovat sovellustasolla.
- Palettien nimet ovat `LocalizedText`-arvoja Coren `Cleaner.Core.Localization`-nimiavaruudesta; Core ei tunne paletteja.
- Sun-paletin koralli on sävyltään lähellä korkean riskin punaista. Riskipunainen esiintyy vain merkeissä ja raidoissa,
  koralli vain napeissa ja valintapalkissa, joten ne eivät sekoitu. Käyttäjä pyysi erilaista palettia.
- Exen kuvake (Resurssienhallinta) ei voi seurata palettia; se on mint. Vain otsikko- ja tehtäväpalkin kuvake seuraa.
- Generoidut kuvakkeet ja PNG commitoidaan, kuten nykyinen `PcCleaner.ico`.
- `tools/snap-window.py` lisätään repoon verifiointia varten (PrintWindow-kaappaus), koska se on ainoa tapa saada
  kuvakaappaus Claude-sessiosta. Se ei kuulu sovellukseen.
- Ikkunan uudelleenluonnissa ei säilytetä Expanderien auki/kiinni-tilaa eikä vieritystä; ne ovat näkymän tilaa.

## Disposition

- EDGE-5: arvot ovat Scales-taulukossa; T2 kirjoittaa ne, T6/T7 käyttävät avaimia. Ei erillistä logiikkaa.
- CON-2: pysyy voimassa, kun yksikään tehtävä ei lisää PackageReference-riviä; tarkistetaan AC-1:ssä (`git diff` csproj).
- CON-5: tarkistetaan AC-6:ssa.

## Wave 1

Kaksi riippumatonta tehtävää rinnakkain: T1 (Core + testit) ja T4 (`tools/` + `Assets/*.ico|png`). T1:n Verify kääntää
Coren, moduulit ja testiprojektin, ei UI-projektia, joten T4:n kirjoittamat kuvakkeet (UI:n `ApplicationIcon` ja
Assets-glob) eivät ole kenenkään build-syötteenä tässä aallossa. T2 ja T3 ovat wave 2:ssa, koska niiden tarkistusbuild
kääntäisi T1:n keskeneräisen Coren.

### T1: AppSettings: palette ja font, Update-apuri
- Status: done e2ea1e9
- Model: opus
- Requirements: REQ-1, REQ-2
- Constraints/edge: CON-1, EDGE-1
- Files: src/Cleaner.Core/Services/AppSettings.cs, tests/Cleaner.Tests/LocalizationTests.cs
- Verify: `dotnet test tests/Cleaner.Tests --filter "FullyQualifiedName~LocalizationTests" -nologo` → kaikki läpi, mukana
  uudet testit; `dotnet build src/Cleaner.Core -nologo` → 0 varoitusta.
- Task:
  `Cleaner.Core.Services.AppSettings` (src/Cleaner.Core/Services/AppSettings.cs) tallentaa nyt vain kielen tiedostoon
  `%LOCALAPPDATA%\PcCleaner\settings.json` muodossa `{ "language": "en" }` (System.Text.Json, camelCase, sisennetty,
  luku kirjainkoosta riippumatta, `Load` ei koskaan heitä). Lisää:

  1. Kentät `public string Palette { get; set; } = "mint";` ja `public string Font { get; set; } = "bahnschrift";`
     (JSON `palette`, `font`). `Load` normalisoi `null`-arvot oletuksiin samalla tavalla kuin `Language` nyt
     (`settings.Language ??= "en"`). Core ei validoi arvoja: tuntematon merkkijono palautuu sellaisenaan ja UI ratkaisee
     oletuksen (EDGE-1), kuten `Language`-kenttä tekee kielelle (`ResolveLanguage`).
  2. `public static AppSettings Update(Action<AppSettings> change, string? path = null)`: lataa `Load(path)`, soveltaa
     `change`, kutsuu `Save(path)` ja palauttaa olion. Tarkoitus: yhden asetuksen tallennus ei nollaa muita (nykyinen UI
     tekee `new AppSettings { Language = ... }.Save()`, mikä kirjoittaisi oletuspaletin päälle; UI korjataan T5:ssä).
  3. Päivitä luokan XML-kommentti näyttämään kaikki kolme kenttää.

  Testit tiedostoon tests/Cleaner.Tests/LocalizationTests.cs nykyisten `AppSettings_*`-testien viereen (sama `_root`-kansio
  ja siivous):
  - round trip: `new AppSettings { Language = "fi", Palette = "indigo", Font = "inter" }.Save(path)` → `Load` palauttaa samat.
  - vanha tiedosto `{ "language": "fi" }` → Palette `"mint"`, Font `"bahnschrift"`, Language `"fi"`.
  - `{ "language": "en", "palette": null, "font": null }` → oletukset.
  - `AppSettings.Update(s => s.Palette = "steel", path)` olemassa olevaan `{ "language": "fi", "font": "inter" }` →
    tiedostossa language fi, font inter, palette steel.
  - `Update` puuttuvaan tiedostoon luo kansion ja tiedoston oletuksilla + muutoksella.
  - Kirjoitettu JSON sisältää avaimet pienellä alkukirjaimella (`"palette"`, `"font"`), CON-1.

## Wave 2

Kaksi riippumatonta tehtävää rinnakkain: T2 (uusi `Theming`-kansio) ja T3 (kaksi tiedostoa `Assets/Fonts`-kansioon,
ei csproj-muutosta). Vain T2 kääntää; T3:n Verify on tiivistetarkistus. WinUI:n Assets-glob lukee T3:n tiedostot
T2:n buildissa, mikä on vaaratonta (kopiointi). Foreman ajaa aallon lopuksi: poista
`src/Cleaner.UI/bin` ja `src/Cleaner.UI/obj`, sitten `dotnet build src/Cleaner.UI -c Debug -p:Platform=x64 -nologo`
→ 0/0 ja `src/Cleaner.UI/bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64/Assets/Fonts/InterVariable.ttf` ja
`LICENSE-Inter.txt` ovat olemassa (REQ-8).

### T2: Appearance: paletit, fontit ja resurssisanakirja
- Status: done f75bfd1
- Model: opus
- Requirements: REQ-3, REQ-4
- Constraints/edge: CON-4, EDGE-5
- Files: src/Cleaner.UI/Theming/Palette.cs, src/Cleaner.UI/Theming/FontChoice.cs, src/Cleaner.UI/Theming/Appearance.cs
- Verify: `dotnet build src/Cleaner.UI -c Debug -p:Platform=x64 -nologo` → 0 varoitusta, 0 virhettä. Luokkaa ei vielä kutsuta
  mistään (App.xaml.cs liittää sen T5:ssä), joten verifiointi on käännös ja koodikatselmus Scales-taulukkoa vasten.
- Task:
  Luo nimiavaruus `Cleaner.UI.Theming` (uusi kansio src/Cleaner.UI/Theming). WinUI 3 -projekti, `Windows.UI.Color`,
  `Microsoft.UI.Xaml.Media.SolidColorBrush` ja `FontFamily`, `Microsoft.UI.Xaml.ResourceDictionary`. `LocalizedText` ja
  `Loc` ovat Coressa (`Cleaner.Core.Localization`): `LocalizedText(string En, string Fi)` on record struct, jonka
  `ToString()` palauttaa nykyisen kielen tekstin.

  Toteuta tarkalleen tämä rajapinta (muut tehtävät on kirjoitettu sitä vasten):

  ```csharp
  public sealed record PaletteColors(
      Color Accent, Color AccentHover, Color AccentPressed, Color OnAccent, Color AccentText,
      Color PaneBackground, Color ContentBackground, Color CardBackground, Color CardBorder, Color CardHover,
      Color TextPrimary, Color TextSecondary, Color TextTertiary, Color Divider);

  public sealed record Palette(string Id, LocalizedText Name, PaletteColors Light, PaletteColors Dark, IReadOnlyList<Color> Treemap);

  public sealed record FontChoice(string Id, LocalizedText Name, string HeadingFamily, string BodyFamily, string NumberFamily)
  {
      public const string MonoFamily = "Cascadia Mono, Consolas";
  }

  public static class Appearance
  {
      public static IReadOnlyList<Palette> Palettes { get; }      // mint, indigo, steel, graphite, sun
      public static IReadOnlyList<FontChoice> Fonts { get; }      // bahnschrift, segoe, inter
      public static Palette DefaultPalette => Palettes[0];
      public static FontChoice DefaultFont => Fonts[0];
      public static Palette CurrentPalette { get; }               // Apply asettaa; ennen Applya DefaultPalette
      public static FontChoice CurrentFont { get; }
      public static Palette ResolvePalette(string? id);           // OrdinalIgnoreCase, trimmattu; tuntematon → DefaultPalette
      public static FontChoice ResolveFont(string? id);
      public static void Apply(Palette palette, FontChoice font); // vaihtaa sanakirjan Application.Current.Resources.MergedDictionaries-listaan
      public static string IconFileName(Palette palette) => $"PcCleaner-{palette.Id}.ico";
  }
  ```

  Arvot: kaikki paletit, treemap-värit, riskivärit ja fontit ovat suunnitelman `## Scales`-osiossa. Kopioi ne
  sellaisenaan. ARGB-heksat, joissa on alpha (PaneBackground `#B3...`), ovat `Color.FromArgb(0xB3, r, g, b)`.

  `Apply` rakentaa `ResourceDictionary`-olion, jolla on `ThemeDictionaries["Default"]` (tumma, paletin `Dark`),
  `ThemeDictionaries["Light"]` (paletin `Light`) ja `ThemeDictionaries["HighContrast"]`. Ilman HighContrast-avainta
  WinUI käyttäisi suuren kontrastin tilassa Default-sanakirjaa ja paletin korostusohitukset peittäisivät järjestelmän
  värit. HighContrast-sanakirjassa ovat vain omat avaimet ja fonttiavaimet (ei WinUI:n korostusohituksia), ja värit
  tulevat järjestelmäväreistä `Windows.UI.ViewManagement.UISettings.GetColorValue(UIColorType.*)`-kutsulla tai
  `{ThemeResource SystemColor*Color}`-avaimia vastaavista `UIColorType`-arvoista: Accent/AccentText = Accent,
  OnAccent = Background, Pane/Content/Card = Background, CardBorder/Divider/TextSecondary/TextTertiary = Foreground,
  TextPrimary = Foreground, CardHover = Background, kaikki Risk*Brush Fill/Text = Foreground ja Tint = Background.
  Default- ja Light-teemasanakirjoissa on:

  Omat avaimet (SolidColorBrush): `AppAccentBrush`, `AppAccentHoverBrush`, `AppAccentPressedBrush`, `AppOnAccentBrush`,
  `AppAccentTextBrush`, `AppPaneBackgroundBrush`, `AppContentBackgroundBrush`, `AppCardBackgroundBrush`,
  `AppCardBorderBrush`, `AppCardHoverBrush`, `AppTextPrimaryBrush`, `AppTextSecondaryBrush`, `AppTextTertiaryBrush`,
  `AppDividerBrush`, `RiskLowBrush`, `RiskLowTintBrush`, `RiskLowTextBrush`, `RiskMediumBrush`, `RiskMediumTintBrush`,
  `RiskMediumTextBrush`, `RiskHighBrush`, `RiskHighTintBrush`, `RiskHighTextBrush`. Lisäksi Color-avaimet samoilla
  nimillä ilman `Brush`-päätettä (`AppAccentColor`, `AppContentBackgroundColor`, ...) niitä paikkoja varten, jotka
  tarvitsevat värin (ikkunan tausta, treemap).

  Fonttiavaimet (FontFamily): `AppHeadingFontFamily`, `AppBodyFontFamily`, `AppNumberFontFamily`, `AppMonoFontFamily`
  ja WinUI:n `ContentControlThemeFontFamily` = Body.

  WinUI:n korostusavaimet, jotta sisäänrakennetut ohjaimet seuraavat palettia (kokeilu 2 todisti mekanismin
  `AccentFillColorDefaultBrush`- ja `NavigationViewSelectionIndicatorForeground`-avaimilla):
  Color: `SystemAccentColor` = Accent, `SystemAccentColorLight1/2/3` = AccentHover, `SystemAccentColorDark1/2/3` = AccentPressed.
  Brush: `AccentFillColorDefaultBrush` = Accent, `AccentFillColorSecondaryBrush` = AccentHover,
  `AccentFillColorTertiaryBrush` = AccentPressed, `AccentFillColorDisabledBrush` = Accent alpha 0x5C,
  `AccentTextFillColorPrimaryBrush` / `SecondaryBrush` / `TertiaryBrush` = AccentText,
  `TextOnAccentFillColorPrimaryBrush` = OnAccent, `TextOnAccentFillColorSecondaryBrush` = OnAccent alpha 0xB3,
  `AccentFillColorSelectedTextBackgroundBrush` = Accent, `NavigationViewSelectionIndicatorForeground` = Accent,
  `SystemControlHighlightAccentBrush` = Accent, `SystemFillColorAttentionBrush` = AccentText,
  `NavigationViewDefaultPaneBackground` ja `NavigationViewExpandedPaneBackground` = PaneBackground,
  `NavigationViewTopPaneBackground` = PaneBackground, `ExpanderHeaderBackground` ja `ExpanderContentBackground` = läpinäkyvä,
  `ExpanderHeaderBorderBrush` ja `ExpanderContentBorderBrush` = läpinäkyvä, `ExpanderContentDownBorderThickness` = 0 (Thickness).
  Avainten nimet ovat Windows App SDK 1.8:n generic.xaml-nimiä; jos jokin ei löydy ajon aikana, se vain jää käyttämättä.

  `Apply` poistaa edellisen `Apply`-kutsun lisäämän sanakirjan (pidä viite staattisessa kentässä) ja lisää uuden, ja
  asettaa `CurrentPalette`/`CurrentFont`. Sitä kutsutaan vain UI-säikeestä. Näkymän uudelleenluonti ei kuulu tähän
  luokkaan (T6 hoitaa).

  Koodityyli: suomenkieliset XML-kommentit kuten muualla projektissa, `file-scoped namespace`, nullable päällä.

### T3: Inter-fontti ja lisenssi
- Status: done 281952d
- Model: opus
- Requirements: REQ-8
- Constraints/edge: CON-2
- Files: src/Cleaner.UI/Assets/Fonts/InterVariable.ttf, src/Cleaner.UI/Assets/Fonts/LICENSE-Inter.txt
- Verify: `pwsh -NoProfile -Command "(Get-FileHash src/Cleaner.UI/Assets/Fonts/InterVariable.ttf -Algorithm SHA256).Hash"` →
  `4989B125924991B90D05B2D16E0E388C48F7D5BB8B30539BBF9C755278D0CCAF` ja sama `LICENSE-Inter.txt`-tiedostolle →
  `262481E844521B326F5ECD053E59B98C8B2DA78C8EE1BDBB6E8174305E54935A`. Ei buildia tässä tehtävässä (T2 kääntää samassa
  aallossa); kopioitumisen build-kansioon tarkistaa foreman aallon lopuksi puhtaalla buildilla (ks. Wave 2).
- Task:
  Lataa Inter 4.1 -julkaisu (todennettu 20.10.2026, älä tutki uudelleen):
  `https://github.com/rsms/inter/releases/download/v4.1/Inter-4.1.zip`, SHA256
  `9883FDD4A49D4FB66BD8177BA6625EF9A64AA45899767DDE3D36AA425756B11E` (33 707 794 tavua). Pura omaan tyhjään
  tilapäiskansioon, älä repon sisään. Tarkista zipin tiiviste ennen purkua. Kopioi zipin juuresta `InterVariable.ttf`
  tiedostoksi `src/Cleaner.UI/Assets/Fonts/InterVariable.ttf` (SHA256
  `4989B125924991B90D05B2D16E0E388C48F7D5BB8B30539BBF9C755278D0CCAF`) ja `LICENSE.txt` tiedostoksi
  `src/Cleaner.UI/Assets/Fonts/LICENSE-Inter.txt` (SIL Open Font License 1.1, SHA256
  `262481E844521B326F5ECD053E59B98C8B2DA78C8EE1BDBB6E8174305E54935A`). Muita tiedostoja ei kopioida.

  Csproj:ta EI muuteta: WinUI:n Microsoft.WinUI.props sisältää oletuksena `<Content Include="Assets/**/*.*">`, joten
  kaikki Assets-kansion tiedostot kopioituvat build- ja publish-kansioon (todennettu: bin-kansiossa on `Assets/PcCleaner.png`,
  jota csproj ei mainitse). Fontin XAML-viittaus on `ms-appx:///Assets/Fonts/InterVariable.ttf#Inter` (toimii
  pakkaamattomassa sovelluksessa, kokeilu 1); sitä käyttää T2. Ei uusia paketteja. Huomio: bin-kansiossa voi olla jo
  kokeilun jäljiltä sama ttf; se ei ole todiste mistään.

### T4: Palettikuvakkeet ja kuvakaappaustyökalu
- Status: done ad2d137
- Model: opus
- Requirements: REQ-7
- Files: tools/make-icon.ps1, tools/snap-window.py, src/Cleaner.UI/Assets/PcCleaner.ico, src/Cleaner.UI/Assets/PcCleaner.png, src/Cleaner.UI/Assets/PcCleaner-mint.ico, src/Cleaner.UI/Assets/PcCleaner-indigo.ico, src/Cleaner.UI/Assets/PcCleaner-steel.ico, src/Cleaner.UI/Assets/PcCleaner-graphite.ico, src/Cleaner.UI/Assets/PcCleaner-sun.ico
- Verify: `pwsh -NoProfile -File tools/make-icon.ps1` → kirjoittaa kuusi .ico- ja yhden .png-tiedoston; `pwsh -NoProfile -Command "Get-ChildItem src/Cleaner.UI/Assets/*.ico | Measure-Object | Select-Object -ExpandProperty Count"` → 6;
  `pwsh -NoProfile -Command "(Get-FileHash src/Cleaner.UI/Assets/PcCleaner.ico).Hash -eq (Get-FileHash src/Cleaner.UI/Assets/PcCleaner-mint.ico).Hash"` → True;
  `python -I tools/snap-window.py --help` → tulostaa käyttöohjeen.
- Task:
  `tools/make-icon.ps1` piirtää nykyisen kuvakkeen (pyöristetty neliö, sininen liukuväri #3884F0 → #164EAA kulmassa 60°,
  valkoinen luuta ja kipinät) System.Drawing-kirjastolla kokoihin 16–256 px ja kirjoittaa `PcCleaner.ico` ja
  `PcCleaner.png` kansioon `src\Cleaner.UI\Assets`. Laajenna se tuottamaan kuvake jokaiselle paletille. Luudan piirto ei muutu,
  vain taustan liukuväri:

  | Id | Alku | Loppu |
  |---|---|---|
  | mint | #2FB388 | #0F6E56 |
  | indigo | #928BE3 | #453D9E |
  | steel | #3884F0 | #164EAA |
  | graphite | #3A3A3A | #141414 |
  | sun | #E8714A | #B8471F |

  Tulosteet: `PcCleaner-<id>.ico` jokaiselle, `PcCleaner.ico` identtinen mint-tiedoston kanssa (exen kuvake) ja
  `PcCleaner.png` 256 px mint. Harjasten raidat (nyt `FromArgb(140, 22, 78, 170)`) piirretään liukuvärin loppuvärillä
  alpha 140. Taulukko on skriptissä yhtenä hajautustaulukkona, jotta uusi paletti on yksi rivi. Aja skripti ja commitoi
  tulokset. Päivitä skriptin `.SYNOPSIS`.

  `tools/snap-window.py` (uusi): Python 3 + Pillow, kaappaa prosessin pääikkunan PrintWindow-kutsulla (lippu
  PW_RENDERFULLCONTENT = 2), koska `CopyFromScreen` epäonnistuu Claude-sessiosta "Kahva ei kelpaa" -virheellä. Käyttö:
  `python -I tools/snap-window.py <exe> <out-dir> <sekunnit...>`: käynnistää exen, ottaa kuvan jokaisen annetun
  sekuntimäärän kohdalla tiedostoihin `<out-dir>/shot1.png, shot2.png, ...` ja lopettaa prosessin. Ikkuna etsitään
  `EnumWindows`-kutsulla pid:n perusteella (näkyvä, ilman omistajaa), koko `GetWindowRect`, bitit `GetDIBits` 32-bittisenä
  BGRA → `Image.frombuffer("RGBA", ..., "BGRA")`. `--help` tulostaa tämän. Vain ctypes ja PIL, ei muita riippuvuuksia.
  Lisää skriptin alkuun docstring, jossa mainitaan, että sovelluksen `requireAdministrator`-manifesti pysäyttää
  käynnistyksen UAC-kehotteeseen ja että kaappausta varten manifestiin vaihdetaan väliaikaisesti `asInvoker` (ei commitoida).

## Wave 3

Yksi tehtävä: T5 muokkaa MainViewModel-, ModuleViewModel- ja UiText-tiedostoja, joihin wave 4 ja 5 nojaavat.

### T5: ViewModelit ja käynnistys: ulkoasun valinta, asetussivun tila, laattojen arvot
- Status: done d8bc31b
- Model: opus
- Requirements: REQ-5, REQ-6, REQ-10, REQ-14, REQ-21
- Constraints/edge: EDGE-1, EDGE-3, EDGE-6
- Files: src/Cleaner.UI/App.xaml.cs, src/Cleaner.UI/ViewModels/MainViewModel.cs, src/Cleaner.UI/ViewModels/ModuleViewModel.cs, src/Cleaner.UI/ViewModels/AppearanceOptionViewModel.cs, src/Cleaner.UI/Services/UiText.cs
- Note: T6 (wave 4) muokkaa UiText.cs-tiedostoa tämän jälkeen (poistaa `PaneTitle`-ominaisuuden). Ei wave-rikkomus.
- Verify: `dotnet build src/Cleaner.UI -c Debug -p:Platform=x64 -nologo` → 0 varoitusta, 0 virhettä (MainWindow.xaml sitoo
  yhä `LanguageIndex`-ominaisuutta ja muita nykyisiä jäseniä; ne pysyvät). Manuaalinen (käyttäjä tai asInvoker-kokeilu +
  `python -I tools/snap-window.py`): sovellus käynnistyy mint-paletilla ja Bahnschrift-fontilla, napit ja valintapalkki
  ovat vihreitä.
- Task:
  Tausta: T2 toi luokan `Cleaner.UI.Theming.Appearance` (rajapinta alla), T1 toi `AppSettings.Palette`, `AppSettings.Font`
  ja `AppSettings.Update(Action<AppSettings>, string? path = null)`. Nyt sovellus on liitettävä niihin.

  ```csharp
  public sealed record Palette(string Id, LocalizedText Name, PaletteColors Light, PaletteColors Dark, IReadOnlyList<Color> Treemap);
  public sealed record FontChoice(string Id, LocalizedText Name, string HeadingFamily, string BodyFamily, string NumberFamily);
  public static class Appearance {
      public static IReadOnlyList<Palette> Palettes { get; }  public static IReadOnlyList<FontChoice> Fonts { get; }
      public static Palette CurrentPalette { get; }  public static FontChoice CurrentFont { get; }
      public static Palette ResolvePalette(string? id);  public static FontChoice ResolveFont(string? id);
      public static void Apply(Palette palette, FontChoice font);  public static string IconFileName(Palette palette);
  }
  ```
  `PaletteColors` on record, jonka kentät ovat `Accent, AccentHover, AccentPressed, OnAccent, AccentText, PaneBackground,
  ContentBackground, CardBackground, CardBorder, CardHover, TextPrimary, TextSecondary, TextTertiary, Divider` (Windows.UI.Color).

  1. `App.xaml.cs` `OnLaunched`: lue asetukset kerran (`var settings = AppSettings.Load()`), aseta `Loc.Current` kuten nyt
     ja kutsu `Appearance.Apply(Appearance.ResolvePalette(settings.Palette), Appearance.ResolveFont(settings.Font))`
     ENNEN `new MainWindow()`. Tuntematon arvo antaa oletuksen eikä tiedostoa kirjoiteta (EDGE-1).

  2. `MainViewModel` (src/Cleaner.UI/ViewModels/MainViewModel.cs, CommunityToolkit.Mvvm, `ObservableObject`, partial-ominaisuudet):
     - `public ObservableCollection<AppearanceOptionViewModel> PaletteOptions { get; }` ja `FontOptions` rakennetaan
       konstruktorissa `Appearance.Palettes`- ja `Appearance.Fonts`-listoista.
     - `public int PaletteIndex { get; set; }` ja `public int FontIndex { get; set; }`: getter palauttaa
       `Appearance.CurrentPalette`/`CurrentFont`-indeksin listassa. Setter: indeksi listan ulkopuolella tai sama kuin nyt →
       ei mitään (RadioButtons kirjoittaa alkuarvon takaisin, sama kuvio kuin `LanguageIndex`). Muuten
       `Appearance.Apply(uusi paletti, nykyinen fontti)` (tai päinvastoin), sitten `AppSettings.Update(s => s.Palette = id)`
       try/catch-lohkossa: epäonnistuminen kirjataan `_log.Warning(Loc.T("Saving the appearance setting failed: {msg}",
       "Ulkoasuasetuksen tallennus epäonnistui: {msg}"))`, lopuksi `OnPropertyChanged` ja tapahtuma
       `public event EventHandler? AppearanceChanged;` (UI-säikeessä; setteriä kutsutaan vain sieltä).
     - `LanguageIndex`-setterin tallennus vaihdetaan muotoon `AppSettings.Update(s => s.Language = Loc.ToCode(language))`,
       jotta paletti ja fontti säilyvät (REQ-2).
     - `[ObservableProperty] public partial bool IsSettingsSelected { get; set; }` (oletus false). Kun se on true,
       sisältöalue näyttää asetussivun (T6). `SelectedModule` ei muutu asetussivulle siirryttäessä.
     - `RefreshLanguage()`: kutsu lisäksi jokaisen `PaletteOptions`/`FontOptions`-alkion `RefreshLanguage()` ja
       `OnPropertyChanged(nameof(PaletteIndex))`, `nameof(FontIndex)` (EDGE-6).
     - `IsIdle` on jo olemassa; asetussivun valitsimet sidotaan siihen (EDGE-3), mitään uutta ei tarvita.

  3. `AppearanceOptionViewModel` (uusi tiedosto src/Cleaner.UI/ViewModels/AppearanceOptionViewModel.cs, `ObservableObject`):
     yksi rivi asetussivun RadioButtons-listassa, sama luokka paleteille ja fonteille.
     ```csharp
     public sealed partial class AppearanceOptionViewModel : ObservableObject
     {
         public string Id { get; }
         public string Name => _name.ToString();                 // LocalizedText nykyisellä kielellä
         public bool IsPalette { get; }
         // Paletti: kolme näyteväriä (REQ-9): Dark.CardBackground, Light.CardBackground, Dark.Accent. Fontille null.
         public SolidColorBrush? Swatch1 { get; }  public SolidColorBrush? Swatch2 { get; }  public SolidColorBrush? Swatch3 { get; }
         public Visibility SwatchVisibility => IsPalette ? Visibility.Visible : Visibility.Collapsed;
         // Fontti: näyteteksti omalla fontilla (REQ-9). Paletille Body = null ja Sample = "".
         public FontFamily? HeadingFontFamily { get; }  public FontFamily? BodyFontFamily { get; }
         public string Sample => IsPalette ? "" : Loc.T("File cleanup · Found 1,204 items, 727 MB", "Tiedostosiivous · Löytyi 1 204 kohdetta, 727 MB");
         public Visibility SampleVisibility => IsPalette ? Visibility.Collapsed : Visibility.Visible;
         public static AppearanceOptionViewModel ForPalette(Palette p);  public static AppearanceOptionViewModel ForFont(FontChoice f);
         public void RefreshLanguage();  // OnPropertyChanged(Name), (Sample)
     }
     ```
     Fontin `FontFamily`-oliot luodaan merkkijonoista `HeadingFamily`/`BodyFamily` (`new FontFamily(string)`).

  4. `ModuleViewModel` (src/Cleaner.UI/ViewModels/ModuleViewModel.cs): laattojen arvot (REQ-14). Lisää
     `public string FoundValue` → kun `_foundBytes > 0` `ByteFormatter.Format(_foundBytes)`, muuten `_foundCount`
     muotoiltuna `Loc.Culture`-kulttuurilla (`"N0"`), ja `public string SelectedValue` → kun `SelectedBytes > 0`
     `ByteFormatter.Format(SelectedBytes)`, muuten `SelectedCount` samoin. `ShowTiles => HasScanned`.
     `ShowEmptyState => !HasScanned && Status == ModuleScanStatus.NotScanned && Id != "uninstall"` (REQ-15: jokainen
     moduuli alkaa NotScanned-tilasta, myös ohjelman poisto, ja `SelectProgram`/`ScanCanceled` asetetaan MainViewModelista
     vasta ajon aikana `Status`-setterin kautta). Nosta `PropertyChanged(ShowEmptyState)` myös `Status`-setterissä (nyt se
     nostaa vain `StatusText`) ja `HasScanned`-ominaisuudelle `[NotifyPropertyChangedFor(nameof(ShowEmptyState), nameof(ShowTiles))]`.
     Nosta `PropertyChanged` `FoundValue`/`SelectedValue`-arvoille `LoadScan`, `ClearScan`, `UpdateSelection` ja
     `RefreshLanguage`-kohdissa (`FoundValue` riippuu kulttuurista ja yksiköistä). `SelectedCount`/`SelectedBytes` saavat
     `[NotifyPropertyChangedFor(nameof(SelectedValue))]`. Asetussivun kuvake tulee NavigationView'n omasta
     asetuskohteesta (T6), tänne ei lisätä glyph-arvoa.

  5. `UiText` (src/Cleaner.UI/Services/UiText.cs): lisää ominaisuudet samaan tyyliin kuin nykyiset
     (`public string X => Loc.T(en, fi);`, `Refresh()` nostaa `PropertyChanged(string.Empty)` tai vastaavan, tarkista
     nykyinen toteutus): `SettingsTitle` Settings / Asetukset, `AppearanceHeader` Appearance / Ulkoasu, `PaletteHeader`
     Color palette / Väripaletti, `FontHeader` Font / Fontti, `LanguageHeader` Language / Kieli, `EmptyStateTitle`
     Nothing scanned yet / Ei vielä skannattu, `EmptyStateBody` "Scan shows what can be removed. Nothing is deleted before
     you confirm." / "Skannaus näyttää, mitä voi poistaa. Mitään ei poisteta ennen vahvistustasi.", `FoundLabel` Found /
     Löytyi, `SelectedLabel` Selected / Valittu, `AdminBadge` Administrator / Järjestelmänvalvoja, `OptInBadge` Manual
     selection / Valitaan erikseen, `Close` Close / Sulje. `Log` ja `ClearLog` ovat jo olemassa ja jäävät.
     Poista mitään ei; `PaneTitle` jää vaikka T6 lakkaa käyttämästä sitä (siivotaan T6:ssa).

  Älä koske MainWindow.xaml-tiedostoon: wave 3 kirjoittaa sen uudelleen. Build pysyy vihreänä, koska kaikki nykyiset
  sidotut jäsenet säilyvät.

## Wave 4

Yksi tehtävä: T6 kirjoittaa MainWindow.xaml:n uudelleen ja luo MainView'n; T7 (wave 5) muokkaa samaa MainView.xaml-tiedostoa,
joten ne eivät voi olla rinnakkain. Note: T7 (wave 5) muokkaa MainView.xaml/.cs-tiedostojen moduulisivuosaa T6:n jälkeen.

### T6: Ikkunan kuori: MainView, otsikkopalkki, Mica, uudelleenluonti, asetussivu, lokipaneeli
- Status: done f96a97a
- Model: fable
- Requirements: REQ-6, REQ-9, REQ-10, REQ-11, REQ-12, REQ-18, REQ-19
- Constraints/edge: CON-4, CON-5, EDGE-2, EDGE-3, EDGE-4
- Files: src/Cleaner.UI/MainWindow.xaml, src/Cleaner.UI/MainWindow.xaml.cs, src/Cleaner.UI/Views/MainView.xaml, src/Cleaner.UI/Views/MainView.xaml.cs, src/Cleaner.UI/Services/UiText.cs
- Note: UiText.cs muokattiin T5:ssä (wave 3); tässä vain `PaneTitle` poistuu. T7 (wave 5) jatkaa Views/MainView.xaml- ja .cs-tiedostoja tämän jälkeen. Ei wave-rikkomuksia.
- Verify: `dotnet build src/Cleaner.UI -c Debug -p:Platform=x64 -nologo` → 0 varoitusta, 0 virhettä.
  Manual (ihminen tai asInvoker-kokeilu + `python -I tools/snap-window.py <exe> <out> 5`): otsikkopalkissa kuvake ja nimi,
  ikkuna raahautuu palkista, Mica näkyy paneelin läpi, rataskuvake avaa asetussivun, paletin vaihto värittää koko ikkunan
  heti, fontin vaihto vaihtaa napit ja otsikot, kielen vaihto asetussivulla päivittää tekstit, lokinappi avaa paneelin
  oikealta, `settings.json` sisältää valinnat ja uudelleenkäynnistys muistaa ne. Manifestin asInvoker-vaihto perutaan.
- Task:
  Tausta: MainWindow.xaml (443 riviä) sisältää nyt koko käyttöliittymän: InfoBar (tavallinen käyttäjä), NavigationView
  (moduulit `ViewModel.Modules`, PaneFooter: Skannaa kaikki + kielivalitsin ComboBox), moduulisivu (otsikko, levytila- ja
  poistosivun ohjaimet, kategoriat Expandereina, korkean riskin lohko), alapalkki (edistyminen, napit, loki-Expander).
  MainWindow.xaml.cs toteuttaa `IDialogService`-rajapinnan (ContentDialogit `Content.XamlRoot`-juurella), FolderPickerin
  (tarvitsee ikkunan hwnd:n) ja treemapin piirron Canvasille (`_treemapStack`, `DrawTreemap`, `Palette`-taulukko).
  Kokeilu (suunnitelman alku, kohdat 2 ja 3) osoitti: sovellustason resurssisanakirjan vaihto päivittää ohjaimet mutta ei
  TextBlock-tyylejä, joten ulkoasun vaihdossa koko sisältönäkymä luodaan uudelleen.

  T5 toi `MainViewModel`-luokkaan: `PaletteOptions`, `FontOptions` (ObservableCollection<AppearanceOptionViewModel>:
  `Name`, `Swatch1..3` (SolidColorBrush?), `SwatchVisibility`, `HeadingFontFamily`, `BodyFontFamily`, `Sample`,
  `SampleVisibility`), `PaletteIndex`, `FontIndex` (int, TwoWay), `event AppearanceChanged`, `IsSettingsSelected` (bool),
  `IsIdle`, `LanguageIndex` (0 = English, 1 = Suomi). `UiText` (`ViewModel.T`) sai `SettingsTitle`, `AppearanceHeader`,
  `PaletteHeader`, `FontHeader`, `LanguageHeader`, `Close`. `Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported()`
  kertoo, onko Mica käytettävissä (EDGE-4). T2 toi resurssiavaimet (kaikki `{ThemeResource}`):
  `AppAccentBrush`, `AppAccentTextBrush`, `AppOnAccentBrush`, `AppPaneBackgroundBrush`, `AppContentBackgroundBrush`,
  `AppCardBackgroundBrush`, `AppCardBorderBrush`, `AppCardHoverBrush`, `AppTextPrimaryBrush`, `AppTextSecondaryBrush`,
  `AppTextTertiaryBrush`, `AppDividerBrush`, `AppHeadingFontFamily`, `AppBodyFontFamily`, `AppNumberFontFamily`,
  `AppMonoFontFamily`, Color-versiot ilman `Brush`-päätettä (`AppContentBackgroundColor`), ja `Appearance.CurrentPalette`,
  `Appearance.IconFileName(palette)` → `PcCleaner-<id>.ico` kansiossa `Assets` exen vieressä (T4).

  Tee:

  1. Uusi `UserControl` `Cleaner.UI.Views.MainView` (src/Cleaner.UI/Views/MainView.xaml + .cs). Konstruktori
     `MainView(MainWindow window, MainViewModel viewModel)`; `ViewModel`-ominaisuus x:Bind-sidontoja varten. Siirrä sinne
     MainWindow.xaml:n koko nykyinen sisältö ja code-behindistä `Nav_SelectionChanged`, `PickFolder_Click` (hwnd ikkunasta:
     `WinRT.Interop.WindowNative.GetWindowHandle(window)`), treemapin kentät ja metodit. `IDialogService` jää MainWindow'hun.
     MainView'n rakenne ylhäältä alas: otsikkopalkki (rivi 36 px, `x:Name="AppTitleBar"`, Grid: kuvake `Image` 16 px
     tiedostosta `Assets/<IconFileName>` + `TextBlock` `TitleText` 12 px AppTextSecondaryBrush, vasen marginaali 12,
     tausta läpinäkyvä, jotta Mica näkyy), InfoBar kuten nyt, `SplitView` (REQ-18: `PanePlacement=Right`,
     `DisplayMode=Overlay`, `OpenPaneLength=420`; auki/kiinni-tila on MainView'n omaa tilaa: lokinapin `Click` kutsuu
     code-behindin `ToggleLog`-metodia, joka kääntää `IsPaneOpen`-arvon. MainViewModel.cs ei kuulu tämän tehtävän
     tiedostoihin, ja paneelin tila saa nollautua uudelleenluonnissa), jonka sisältö on NavigationView, ja alapalkki. Lokipaneelin sisältö: otsikko `T.Log` (Heading 16 SemiBold), sulkemisnappi
     (FontIcon ``, ToolTip `T.Close`), lokirivit ListView `AppMonoFontFamily` 12 px, alhaalla `T.ClearLog`-nappi ja
     `LogPath`. Poista alapalkin loki-Expander; alapalkkiin tulee lokinappi (FontIcon ``, ToolTip `T.Log`) muiden
     nappien vasemmalle puolelle. Alapalkin muu asettelu säilyy tässä tehtävässä (T7 muotoilee sen).
  2. NavigationView: `IsSettingsVisible="True"`, `IsPaneToggleButtonVisible="False"`, `IsPaneOpen="True"`,
     `OpenPaneLength="240"`, ei `PaneTitle`-arvoa (REQ-19). Poista PaneFooterista kielivalitsin-ComboBox; Skannaa kaikki jää.
     `SelectionChanged`: jos `args.IsSettingsSelected` → `ViewModel.IsSettingsSelected = true`; jos
     `args.SelectedItem is ModuleViewModel m` → `ViewModel.IsSettingsSelected = false; ViewModel.SelectedModule = m`;
     muuten (SelectedItem null, esim. mallin soveltamisen aikana) ei tehdä mitään. Alkuvalinta tehdään `Nav.Loaded`-
     tapahtumassa, EI konstruktorissa, koska `Nav.SettingsItem` on null ennen kuin NavigationView on soveltanut mallinsa
     (`OnApplyTemplate`): `Nav.SelectedItem = ViewModel.IsSettingsSelected ? Nav.SettingsItem : (ViewModel.SelectedModule ?? Modules[0])`
     (REQ-11). Sisältöalue: kaksi päällekkäistä Grid-elementtiä, moduulisivu `Visibility` = !IsSettingsSelected ja
     asetussivu = IsSettingsSelected (x:Bind-funktio tai `BoolToVisibility`-muunnin, kuten projektissa jo tehdään
     `Visibility="{x:Bind ViewModel.IsDiskUsageSelected}"`-sidonnoilla; WinUI muuntaa boolin suoraan).
  3. Asetussivu (REQ-9, REQ-10) ScrollViewerissä, padding 32/24: otsikko `T.SettingsTitle` (AppHeadingFontFamily 28 SemiBold),
     osio `T.AppearanceHeader` (16 SemiBold), alaotsikko `T.PaletteHeader` ja `RadioButtons ItemsSource=PaletteOptions
     SelectedIndex=PaletteIndex (TwoWay) IsEnabled=IsIdle` ItemTemplate: StackPanel vaakaan: kolme 16 px `Ellipse`-elementtiä
     (`Fill=Swatch1..3`, `Stroke={ThemeResource AppCardBorderBrush}` 1 px) ja `Name`. Alaotsikko `T.FontHeader` ja
     `RadioButtons ItemsSource=FontOptions SelectedIndex=FontIndex (TwoWay) IsEnabled=IsIdle` ItemTemplate: StackPanel pystyyn:
     `Name` (BodyFontFamily 14 SemiBold) ja `Sample` (HeadingFontFamily 16, AppTextSecondaryBrush). Osio `T.LanguageHeader`:
     `RadioButtons SelectedIndex=LanguageIndex (TwoWay) IsEnabled=IsIdle` kohteet "English", "Suomi" (aina omalla kielellään).
     Kaikki tekstit `{x:Bind ViewModel.T.X, Mode=OneWay}`.
  4. MainWindow.xaml kutistuu: `<Window ...><Grid x:Name="Root" /></Window>` (tai pelkkä Window ilman sisältöä). MainWindow.xaml.cs:
     - `SystemBackdrop = new MicaBackdrop();` `ExtendsContentIntoTitleBar = true;` ikkunan koko 1200 × 820 kuten nyt ja
       minimi 960 × 640 (`(AppWindow.Presenter as OverlappedPresenter)` → `PreferredMinimumWidth` ja
       `PreferredMinimumHeight`, Windows App SDK 1.4+). REQ-12.
     - `BuildView()`: jos `Content` on jo `MainView old`, kutsu ensin `old.Detach()` (irrottaa MainView'n omat
       ViewModel-tilaukset: `PropertyChanged`-käsittelijä TreemapRootille, `LanguageRefreshed` jos tilattu, kokoelmien
       kuuntelijat) ja `old.Bindings.StopTracking()` (x:Bind OneWay -kuuntelijat), jotta vanha näkymäpuu ei jää
       kuuntelemaan pitkäikäistä ViewModelia ja piirtämään treemappia irrotetulle Canvasille joka vaihdossa. Sitten
       `var view = new MainView(this, ViewModel); Content = view; SetTitleBar(view.AppTitleBar);`
       `AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", Appearance.IconFileName(Appearance.CurrentPalette)))`
       jos tiedosto on, muuten `PcCleaner.ico`. Ikkunan taustaväri (EDGE-4): juuren `Grid.Background` MainView'ssä on
       läpinäkyvä, jotta Mica näkyy, paitsi kun `MicaController.IsSupported()` on false: silloin `SystemBackdrop` jätetään
       asettamatta ja MainView saa konstruktoriparametrin `bool micaAvailable`, jonka ollessa false juuren tausta on
       `{ThemeResource AppContentBackgroundBrush}`. Sisältöalueen ja alapalkin taustat ovat `AppContentBackgroundBrush`
       (T7 asettaa ne, tässä riittää, että NavigationView'n sisällön juuri-Grid saa `Background={ThemeResource AppContentBackgroundBrush}`).
     - Konstruktori kutsuu `BuildView()` ja tilaa `ViewModel.AppearanceChanged += (_, _) => BuildView();` (REQ-6, REQ-11,
       EDGE-2: treemapin pino on MainView'n tilaa ja nollautuu; `TreemapRoot` on ViewModelissa ja piirtyy juuresta; kirjoita
       tästä kommentti). `LanguageRefreshed` päivittää yhä `Title`-arvon ja pyytää MainView'tä piirtämään treemapin
       (`view.RefreshLanguage()`-metodi: otsikkopalkin teksti on x:Bind `TitleText`, treemap `DrawTreemap()`).
     - `IDialogService`-metodit pysyvät; `XamlRoot = Content.XamlRoot` toimii, koska Content on aina MainView.
  5. Siivoa `UiText.PaneTitle` pois (ei enää käytössä).
  6. Kaikki värit ja fontit `{ThemeResource App...}`-avaimina (CON-4). Älä kovakoodaa heksoja. Älä muuta moduulisivun
     sisäistä asettelua enempää kuin siirto vaatii; T7 tekee sen.

  Huomio koodille: `MainView` saa `Loaded`-tapahtumassa kutsua `DrawTreemap()` jos `ViewModel.TreemapRoot != null`.
  `x:Bind`-funktiot tarvitsevat `x:DataType`-määrittelyt kuten nykyisessä XAML:ssä (`vm:ModuleViewModel` jne.).

## Wave 5

Kaksi riippumatonta tehtävää rinnakkain: T7 muokkaa vain `Views/MainView.xaml` ja `.cs` (T6:n jälkeen), T8 vain
dokumentteja. Tiedostot eivät leikkaa, ja T8:n Verify on rajattu omiin tiedostoihinsa, jotta T7:n keskeneräiset
muutokset eivät näy siinä. Note: T7 jatkaa T6:n (wave 4) luomia tiedostoja.

### T7: Moduulisivun ja alapalkin uusi asettelu
- Status: done 80ab543
- Model: fable
- Requirements: REQ-13, REQ-14, REQ-15, REQ-16, REQ-17, REQ-20
- Constraints/edge: CON-4, EDGE-5
- Files: src/Cleaner.UI/Views/MainView.xaml, src/Cleaner.UI/Views/MainView.xaml.cs
- Note: T6 (wave 4) loi nämä tiedostot; tämä tehtävä muokkaa niiden moduulisivu- ja alapalkkiosaa. Ei wave-rikkomus.
- Verify: `dotnet build src/Cleaner.UI -c Debug -p:Platform=x64 -nologo` → 0 varoitusta, 0 virhettä.
  Manual (ihminen tai asInvoker-kokeilu + `python -I tools/snap-window.py <exe> <out> 5`): ennen skannausta tyhjä tila
  keskellä Skannaa-napilla; Tiedostosiivouksen skannauksen jälkeen laatat Löytyi/Valittu ja kategoriakortit raidoilla;
  Levytilan analyysissa treemap paletin väreillä; alapalkki 56 px, kiireisenä 4 px palkki; vaalea teema (Windowsin
  asetus) näyttää vaalean paletin. Kaikki viisi palettia tarkistetaan vähintään tummassa teemassa.
- Task:
  Tausta: T6 siirsi käyttöliittymän `Views/MainView.xaml`-tiedostoon (UserControl, `ViewModel` x:Bind-juurena) ja
  toi otsikkopalkin, asetussivun ja lokipaneelin. Moduulisivu ja alapalkki ovat yhä vanhassa asussa: otsikko
  TitleTextBlockStylellä, kuvaus, caption-rivi `RiskText` + `AdminHint`, `OptInHint`, levytila-/poisto-ohjaimet, `StatusText`,
  varoitus-InfoBar, ScrollViewer jossa kategoriat `Expander`-elementteinä (`Categories`, `HighRiskCategories`,
  `VisibleItems`, `HiddenText`), alapalkki (BusyTitle/ProgressBar/ProgressMessage, SelectionText, napit). Tämä tehtävä
  muotoilee ne `## Scales`-osion mukaan. Käytettävissä olevat resurssiavaimet (kaikki `{ThemeResource}`):
  `AppAccentBrush`, `AppAccentTextBrush`, `AppOnAccentBrush`, `AppContentBackgroundBrush`, `AppCardBackgroundBrush`,
  `AppCardBorderBrush`, `AppCardHoverBrush`, `AppTextPrimaryBrush`, `AppTextSecondaryBrush`, `AppTextTertiaryBrush`,
  `AppDividerBrush`, `RiskLowBrush`/`RiskLowTintBrush`/`RiskLowTextBrush` ja samat Medium/High, `AppHeadingFontFamily`,
  `AppBodyFontFamily`, `AppNumberFontFamily`, `AppMonoFontFamily`, Color-avaimet (`AppContentBackgroundColor`).
  ViewModelissa (T5): `ModuleViewModel.FoundValue`, `SelectedValue`, `ShowTiles`, `ShowEmptyState`, `Glyph`, `RiskText`,
  `AdminHint`, `OptInHint`, `RequiresAdmin`, `SelectedByDefault`, `Module.Risk` (RiskLevel Low/Medium/High);
  `CategoryViewModel.Risk`, `RiskText`, `SummaryText`; `UiText`: `EmptyStateTitle`, `EmptyStateBody`, `FoundLabel`,
  `SelectedLabel`, `AdminBadge`, `OptInBadge`, `Scan`, `Clean`, `Cancel`, `RestoreBackup`, `Log`;
  `MainViewModel.IsIdle`, `IsBusy`, `ScanCommand`. `Appearance.CurrentPalette.Treemap` (IReadOnlyList<Color>).

  Rakenna:

  1. Otsikko-osa: moduulin nimi `AppHeadingFontFamily` 28 SemiBold `AppTextPrimaryBrush`; kuvaus 14 `AppBodyFontFamily`
     `AppTextSecondaryBrush`; merkkirivi (REQ-13) `StackPanel Orientation=Horizontal Spacing=8`: riskimerkki (Border
     CornerRadius 10, Padding 8/2, Background RiskX Tint, sisällä 8 px Ellipse RiskX Fill + `RiskText` 12 px RiskX Text),
     admin-merkki (`Visibility=RequiresAdmin`, Border CardBackground + 1 px CardBorder, FontIcon `` 12 px + `T.AdminBadge`,
     ToolTip `AdminHint`), valitaan erikseen -merkki (`Visibility=!SelectedByDefault`, sama tyyli, FontIcon ``,
     `T.OptInBadge`, ToolTip `OptInHint`). Riskin värit tulevat suoraan XAML:n `{ThemeResource Risk*Brush}`-viittauksista,
     jotta Windowsin vaalea/tumma-vaihto ajon aikana päivittää ne (REQ-5): tee kolme riskikohtaista merkkiä (Low, Medium,
     High) kiinteillä ThemeResource-väreillä ja näytä niistä yksi x:Bind-funktioilla `IsLow(RiskLevel)`, `IsMedium`,
     `IsHigh` → Visibility. Sama kuvio kategoriakortin raidalle ja riskitekstille (kolme elementtiä, yksi näkyvissä).
     Älä hae väriä `Application.Current.Resources[key]`-kutsulla koodista; se jäätyisi hakuhetken teemaan. Vanhat
     caption-tekstit poistuvat.
  2. Laatat (REQ-14): `Visibility=ShowTiles`, kaksi Borderia 160 × 64, CardBackground, 1 px CardBorder, kulmasäde 8,
     padding 14/10: arvo 22 `AppNumberFontFamily` SemiBold (`FoundValue` AppTextPrimaryBrush; `SelectedValue`
     AppAccentTextBrush), alla 12 px `T.FoundLabel`/`T.SelectedLabel` AppTextSecondaryBrush. Väli 12.
  3. Tyhjä tila (REQ-15): `Visibility = ShowEmptyState && IsIdle` (x:Bind-funktio), keskitetty pystyyn ja vaakaan jäljellä
     olevaan tilaan (ScrollViewerin rivillä, kategoriat piilossa koska niitä ei ole): FontIcon `Glyph` 40 px
     AppTextTertiaryBrush, `T.EmptyStateTitle` 18 SemiBold Heading, `T.EmptyStateBody` 13 AppTextSecondaryBrush
     TextWrapping MaxWidth 420 TextAlignment Center, Button `T.Scan` AccentButtonStyle `ScanCommand` MinWidth 140. Väli 12.
     Status-TextBlock (`StatusText`) jää näkyviin vain kun `HasScanned` tai tila on SelectProgram/ScanCanceled, eli
     `Visibility=!ShowEmptyState`.
  4. Kategoriakortit (REQ-16): `ItemsControl`-mallissa Border (CardBackground, BorderBrush CardBorder 1 px, CornerRadius 8,
     Margin 0,0,0,8) → Grid kaksi saraketta: 3 px Border `Background=RiskFillBrush(Risk)` CornerRadius 8,0,0,8 ja Expander
     (`Background=Transparent`, `BorderThickness=0`, HorizontalContentAlignment Stretch). Expander-otsikko: CheckBox,
     nimi 14 SemiBold Body, riskiteksti 12 `RiskTextBrush(Risk)`, oikealla `SummaryText` 13 `AppNumberFontFamily`
     AppTextSecondaryBrush. Rivit: Grid MinHeight 32, CheckBox, polku 13 Body AppTextPrimaryBrush TextTrimming, kuvaus
     12 AppTextSecondaryBrush, koko oikealla 12 `AppNumberFontFamily`. Sama malli korkean riskin lohkon sisällä;
     ulompi korkean riskin kortti: raita RiskHighBrush, otsikkorivin tausta RiskHighTintBrush, otsikko `T.HighRiskHeader`
     RiskHighTextBrush 14 SemiBold, vihje `T.HighRiskHint` 12. Rivien ja korttien välissä ei ylimääräisiä viivoja.
     Pidä nykyiset sidonnat (`IsChecked` TwoWay, `IsSelected` TwoWay, `HiddenText`, `HasHidden`) ennallaan.
  5. Levytila- ja poistosivun ohjaimet pysyvät otsikon alla; anna niille sama 8 px kulmasäde ja CardBorder-reunat
     (ListView, treemap-Border). Treemap (REQ-20): korvaa `Palette`-taulukko `Appearance.CurrentPalette.Treemap`-listalla
     `DrawTreemap`-metodissa. Taustaväri haetaan piirtohetkellä nykyisen teeman mukaan:
     `ActualTheme == ElementTheme.Light ? Appearance.CurrentPalette.Light.ContentBackground : Appearance.CurrentPalette.Dark.ContentBackground`
     (ei `Application.Current.Resources`-hakua, joka jäätyisi hakuhetken teemaan). Lohkon reuna on tämä väri 1 px.
     Lohkotekstin väri (REQ-20): laske näkyvä väri yhdistämällä kategoriaväri lohkon alphalla (170 kansiolle, 90 tiedostoille)
     taustavärin päälle kanavittain `c = a·fg + (1 − a)·bg`, a = alpha/255; laske sRGB-linearisoitu luminanssi
     `L = 0.2126 R' + 0.7152 G' + 0.0722 B'` (R' = (c/255 ≤ 0.04045 ? c/255/12.92 : ((c/255 + 0.055)/1.055)^2.4)) ja
     valitse #FFFFFF tai #1A1A1A sen mukaan, kummalla on suurempi kontrastisuhde `(L_vaaleampi + 0.05) / (L_tummempi + 0.05)`
     näkyvään väriin. Esimerkki ilman taustaa: amber #EF9F27 L ≈ 0,433 → #1A1A1A (8,0 vs 2,2). Kirjoita luminanssi ja
     kontrasti apufunktioihin. Tilaa `ActualThemeChanged` MainView'n juuressa ja kutsu `DrawTreemap()`, koska tausta ja
     tekstin valinta riippuvat teemasta.
  6. Alapalkki (REQ-17): Grid, korkeus 56 + 4 px ProgressBar sen yläreunassa (`Visibility=IsBusy`, `Value=ProgressPercent`,
     koko leveys, ei marginaalia, `Foreground=AppAccentBrush`), tausta AppContentBackgroundBrush, yläreuna 1 px
     AppDividerBrush, padding 24/0. Vasemmalla: kun `IsBusy` → `BusyTitle` 13 SemiBold + `ProgressMessage` 12
     AppTextSecondaryBrush pinossa; muuten `SelectionText` 13. Oikealla Spacing 8: lokinappi (FontIcon ``, ToolTip
     `T.Log`, T6:n ToggleLog), `T.RestoreBackup` (`ShowRestoreButton`), `T.Cancel` (`IsBusy`), `T.Scan` MinWidth 120,
     `T.Clean` AccentButtonStyle MinWidth 120. Napit korkeus 32.
  7. Sisältöalue: padding 32/24/32/16, `MaxWidth=1100` `HorizontalAlignment=Left` sisällön juuressa, tausta
     AppContentBackgroundBrush (T6 saattoi asettaa; varmista). Ei kovakoodattuja värejä eikä fonttinimiä (CON-4).

  Pidä kaikki olemassa olevat komennot ja sidonnat toiminnassa. Älä muuta ViewModeleja (eivät kuulu tiedostoihin); jos
  jokin tarvittava ominaisuus puuttuu, laske se x:Bind-funktiolla code-behindissä.

### T8: Dokumentaatio
- Status: done 7ddb234
- Model: opus
- Requirements: REQ-22
- Files: README.md, README.fi.md, docs/ARCHITECTURE.md, HANDOFF.md
- Verify: `git diff --stat -- README.md README.fi.md docs/ARCHITECTURE.md HANDOFF.md` → neljä tiedostoa muuttunut
  (T7 muokkaa samaan aikaan muita tiedostoja, siksi rajaus); `grep -n "palette" README.md docs/ARCHITECTURE.md` → osumia;
  `grep -n "Inter" README.md README.fi.md` → lisenssimaininta molemmissa.
- Task:
  Kirjoita dokumentteihin, mitä tässä suunnitelmassa toteutetaan (ei tulevaisuusmuotoa; tehtävät T1–T7 valmistuvat samassa
  haarassa). Lähteenä tämä suunnitelma (`## Background & scope`, `## Scales`, `## Requirements`).

  - `README.md` (englanti) ja `README.fi.md` (suomi), kumpikin omalla kielellään: lyhyt kappale "Appearance" / "Ulkoasu":
    asetussivu (rataskuvake) → viisi palettia (Mint, Indigo, Steel, Graphite, Sun), kolme fonttia (Bahnschrift + Segoe UI
    Variable oletus, Segoe UI Variable, Inter), vaalea ja tumma Windowsin asetuksen mukaan, valinnat tiedostossa
    `%LOCALAPPDATA%\PcCleaner\settings.json` kenttinä `language`, `palette`, `font`. Lisenssikohta: Inter on SIL Open Font
    License 1.1 -lisenssillä, tiedosto `src/Cleaner.UI/Assets/Fonts/LICENSE-Inter.txt`. Hakemistopuuhun `Assets/Fonts/`
    ja `tools/snap-window.py` (yhden rivin kuvaus: ikkunan kuvakaappaus PrintWindow-kutsulla). `make-icon.ps1`-rivi:
    tuottaa palettikohtaiset kuvakkeet.
  - `docs/ARCHITECTURE.md` luku 7 (otsikko "## 7. WinUI 3 -käyttöliittymä – toteutettu (ensimmäinen versio)"): päivitä
    otsikko muotoon "– toteutettu" ja lisää alaluku "7.1 Ulkoasu": `Cleaner.UI.Theming.Appearance` (paletit, fontit,
    resurssisanakirjan vaihto `MergedDictionaries`-listaan, WinUI:n korostusavainten ohitus), `MainView` luodaan
    uudelleen ulkoasun vaihdossa ja miksi (TextBlock-tyylit eivät seuraa ThemeResource-vaihtoa), resurssiavainten nimet
    (`App*Brush`, `Risk*Brush`, `App*FontFamily`), asetussivu, `settings.json`-kentät ja tuntemattoman arvon käsittely,
    kuvakkeet `PcCleaner-<id>.ico`, Mica ja oma otsikkopalkki, lokipaneeli. Päivitä luvun nykyiset rivit, jotka
    kuvaavat kielivalitsinta PaneFooterissa ja loki-Expanderia alapalkissa. Päivitä versio ja päivämäärä otsikon alla
    (0.4 · 20.10.2026).
  - `HANDOFF.md`: `src/Cleaner.UI`-rivi taulukossa mainitsee asetussivun, paletit, fontit, MainView'n ja Theming-kansion;
    lisää "Tehdyt suunnittelupäätökset" -listaan kohta: ulkoasun vaihto luo näkymän uudelleen, paletit ja fontit ovat
    `Appearance`-luokassa, oletukset mint ja bahnschrift.

  Tyyli: suomeksi (paitsi README.md), ei ajatusviivoja, ei lihavointia joka termille, tiiviisti. Älä muuta muita lukuja.

## Final verification

- AC-1 [auto]: `dotnet build PcCleaner.sln -c Debug -nologo` → `0 Warning(s)`, `0 Error(s)` (nyt 0/0). Lisäksi
  `git diff main -- "*.csproj" | grep PackageReference` → ei rivejä (CON-2).
- AC-2 [auto]: `dotnet test tests/Cleaner.Tests -nologo` → kaikki läpi, määrä vähintään 214 + T1:n uudet (nyt 214).
- AC-3 [auto]: poista `src\Cleaner.UI\bin`, `src\Cleaner.UI\obj` ja `publish\PcCleaner`, sitten
  `dotnet publish src\Cleaner.UI -c Release -o publish\PcCleaner -nologo` onnistuu ja kansiossa
  `publish\PcCleaner\Assets` on `Fonts\InterVariable.ttf`, `Fonts\LICENSE-Inter.txt` ja kuusi `.ico`-tiedostoa
  (siivous ensin, koska kokeilun jäljiltä bin-kansiossa on jo ttf).
- AC-4 [manual]: Käynnistä sovellus (käyttäjä hyväksyy UAC-kehotteen). Oletus: mint-paletti, Bahnschrift-otsikot, Mica
  paneelin takana, oma otsikkopalkki kuvakkeella, tyhjä tila Skannaa-napilla. Asetussivulta vaihda jokainen viidestä
  paletista: koko ikkuna (paneeli, otsikkopalkki, napit, valintapalkki, kortit) vaihtuu heti ja otsikkopalkin kuvake
  seuraa. Vaihda jokainen kolmesta fontista: otsikot, napit ja navigointi vaihtuvat heti. Vaihda kieli: asetussivun
  tekstit ja palettien nimet vaihtuvat. Sulje ja käynnistä uudelleen: valinnat ovat voimassa ja `settings.json` sisältää
  `language`, `palette`, `font`.
- AC-5 [manual]: Skannaa Tiedostosiivous: laatat Löytyi/Valittu päivittyvät valintoja muuttaessa, kategoriat ovat kortteja
  riskiraidoilla, korkean riskin lohko erottuu, alapalkissa näkyy 4 px edistymispalkki skannauksen aikana ja Peruuta-nappi.
  Loki avautuu oikealta ja sulkeutuu. Levytilan analyysin treemap käyttää paletin värejä ja teksti on luettavaa
  keltaisella lohkolla. Vaihda Windowsin teema vaaleaksi: paletin vaalea versio tulee käyttöön ilman uudelleenkäynnistystä.
- AC-6 [auto]: `git grep -n "asInvoker" -- src/Cleaner.UI/app.manifest` → ei osumia (CON-5), ja
  `git grep -nE "#[0-9A-Fa-f]{6}" -- "src/Cleaner.UI/*.xaml"` → ei osumia (CON-4).

# Plan: Tekoälyavustaja (Claude API)
Branch: feature/ai-assistant
Created: 2026-10-20

## Background & scope

WinUI-sovellukseen tulee tekoälyavustaja, joka käyttää Clauden API:a (NuGet `Anthropic` 12.58.0, avain
ympäristömuuttujasta `ANTHROPIC_API_KEY`). Avustaja tekee kolmea asiaa: (1) arvioi valitun moduulin skannaustulokset
kohde- ja kategoriakohtaisesti (turvallinen / varo / säilytä + perustelu), (2) arvioi koneen kunnon järjestelmätietojen
ja skannaustulosten perusteella ja (3) vastaa vapaisiin kysymyksiin samassa keskustelussa. Avustaja on oikean reunan
sivupaneeli, joka näkyy moduulisivun rinnalla. Lisäksi on Auto-tila, jossa yksi nappi skannaa oletusmoduulit, pyytää
arvion, soveltaa valinnat ja ajaa puhdistuksen yhdellä vahvistusdialogilla.

Käyttäjän päätökset (kysytty 20.10.2026):

1. Sijainti: sivupaneeli oikealla (lokipaneelin tapaan), nappi alapalkissa; moduulin tulokset näkyvät samaan aikaan.
2. Tietosuoja: kohteiden polut, koot, kategoriat ja kuvaukset lähetetään, mutta käyttäjäprofiilin polku korvataan
   merkinnällä `%USERPROFILE%` ja käyttäjänimi muissa `\Users\<nimi>\`-poluissa merkinnällä `%USERNAME%`. Suuret
   listat tiivistetään (määrä, koko, otos).
3. Valinnat: oletuksena avustaja vain neuvoo; varoitetut kohteet merkitään ja nappi "Poista varoitetut valinnasta"
   muuttaa valinnan. Lisäksi Auto-tila: nappi "Automaattinen siivous" skannaa oletusmoduulit, arvioi, soveltaa
   valinnat ja puhdistaa yhdellä vahvistusdialogilla, jossa avustajan yhteenveto näkyy. Korkean riskin kohteet ja
   opt-in-moduulit (käynnistys, levytila, rekisteri, ohjelman poisto, prosessit) jäävät aina ulkopuolelle.
4. Tilan valinta: ToggleSwitch "Auto-tila" paneelissa, tallentuu `settings.json`-kenttään `aiAutoMode`, näkyy myös
   asetussivulla. Oletus pois.
5. Laajuus: vain WinUI. CLI pysyy ennallaan. Avustajan koodi on omassa projektissa `src/Cleaner.Ai`, jota CLI voi
   käyttää myöhemmin.

Ulkopuolella: CLI-komennot; markdownin renderöinti (vastaukset pyydetään tavallisena tekstinä); keskustelun
tallennus levylle; API-avaimen syöttö käyttöliittymästä (vain ympäristömuuttuja); kustannusarviot käyttöliittymässä
(token-määrät kirjataan lokiin Debug-tasolla); palvelinpuolen refusal-fallback (beta); prompt caching.

Olemassa oleva koodi, johon tämä nojaa (lue ennen toteutusta): `docs/ARCHITECTURE.md` luvut 3, 7 ja 9 (sopimus,
UI:n rakenne, lokalisointikäytäntö `Loc.T("English", "Suomi")`), `src/Cleaner.UI/ViewModels/MainViewModel.cs`
(BeginBusy/EndBusy, ScanModulesAsync, kolmivaiheinen CleanAsync, IDialogService), `ModuleViewModel.cs`,
`CategoryViewModel.cs`, `ItemViewModel.cs`, `src/Cleaner.UI/Views/MainView.xaml` (lokipaneeli `LogSplitView`,
`CategoryCardTemplate`, alapalkki, asetussivu) ja `MainView.xaml.cs` (`Detach`), `src/Cleaner.Core/Services/AppSettings.cs`.

SDK:n todennettu käyttö (koottu 20.10.2026 scratchpad-projektissa `Anthropic` 12.58.0, net8.0, käännös 0/0 ja ajo
oikealla avaimella onnistui; älä tutki uudelleen):

```csharp
using Anthropic; using Anthropic.Exceptions; using Anthropic.Models.Messages;
AnthropicClient client = new();                       // lukee ANTHROPIC_API_KEY
var p = new MessageCreateParams {
    Model = "claude-opus-5-5", MaxTokens = 2000,
    System = new List<TextBlockParam> { new() { Text = "..." } },   // List<TextBlockParam>, ei taulukko
    OutputConfig = new OutputConfig { Effort = Effort.Low },        // myös: Format = new JsonOutputFormat { Schema = Dictionary<string, JsonElement> }
    Messages = [ new() { Role = Role.User, Content = "..." } ] };
await foreach (RawMessageStreamEvent ev in client.Messages.CreateStreaming(p)) {
    if (ev.TryPickContentBlockDelta(out var d) && d.Delta.TryPickText(out var t)) Console.Write(t.Text);
    if (ev.TryPickStart(out var s)) { /* s.Message.Usage.InputTokens */ }
    if (ev.TryPickDelta(out var md)) { /* md.Delta.StopReason, md.Usage.OutputTokens */ } }
var r = await client.Messages.Create(p);              // r.StopReason, r.Usage.InputTokens/OutputTokens, r.StopDetails?.Category
foreach (var b in r.Content) if (b.TryPickText(out var t)) { /* t.Text on JSON-skeeman mukainen teksti */ }
// Poikkeukset (Anthropic.Exceptions, luettu 12.58.0:n metadatasta): AnthropicException : Exception;
//   AnthropicServiceException : AnthropicException; AnthropicApiException : AnthropicServiceException;
//   Anthropic4xxException ja Anthropic5xxException ja AnthropicUnexpectedStatusCodeException : AnthropicApiException;
//   AnthropicUnauthorizedException (401), AnthropicRateLimitException (429), AnthropicBadRequestException,
//   AnthropicForbiddenException, AnthropicNotFoundException, AnthropicUnprocessableEntityException : Anthropic4xxException;
//   AnthropicSseException : AnthropicServiceException (suoratoiston virhetapahtuma, esim. overloaded kesken vastauksen);
//   AnthropicIOException : AnthropicException (verkko); AnthropicInvalidDataException : AnthropicException.
// HUOM: StopReason.ToString() palauttaa lainausmerkit mukaan lukien ("end_turn"); vertaa `r.StopReason == "refusal"`
// (README-esimerkki) ja jos se ei käänny, `r.StopReason?.ToString()?.Trim('"') == "refusal"`.
// Assistant-prefill ja tool_choice any/tool eivät ole käytössä Opus 5.5:llä; thinking jätetään pois (adaptiivinen oletus).
```

## Requirements

Ai-kirjasto (`src/Cleaner.Ai`, nimiavaruus `Cleaner.Ai`)

- REQ-1: Projekti `src/Cleaner.Ai/Cleaner.Ai.csproj` (net8.0, Nullable, ImplicitUsings, LangVersion latest) viittaa
  vain projektiin `Cleaner.Core` ja NuGet-pakettiin `Anthropic` versio `12.58.0`. Projekti on `PcCleaner.sln`-tiedostossa
  (GUID `{A1B2C3D4-0011-4000-8000-000000000011}`, neljä Any CPU -konfiguraatioriviä), ja `tests/Cleaner.Tests/Cleaner.Tests.csproj`
  sekä `src/Cleaner.UI/Cleaner.UI.csproj` viittaavat siihen.
- REQ-2: `PathSanitizer.Sanitize(string)` korvaa nykyisen käyttäjäprofiilin polun (esim. `C:\Users\ville`) merkinnällä
  `%USERPROFILE%` ja muualla esiintyvän `\Users\<käyttäjänimi>\`-muodon muodolla `\Users\%USERNAME%\`, kirjainkoosta
  riippumatta, myös kun polku on keskellä tekstiä. Osuma hyväksytään vain, kun sitä seuraa `\`, tekstin loppu tai
  muu kuin kirjain, numero, `_`, `-` tai `.`; siksi profiili `C:\Users\ville` ei osu polkuun `C:\Users\villeneuve\...`.
  Muiden käyttäjien profiilit ja muu teksti eivät muutu. Konstruktori ottaa profiilipolun ja käyttäjänimen
  (testattava), `PathSanitizer.ForCurrentUser()` lukee ne ympäristöstä.
- REQ-3: `ScanDigest.Build(moduleName, items, sumSizes, options, sanitizer)` tuottaa tekstin, jossa jokaisella
  kategorialla on otsikkorivi (nimi, riski, kohdemäärä, yhteiskoko, valittujen määrä) ja kohteet koon mukaan suurin
  ensin. Koot kirjoitetaan `AiFormat.Bytes(long)`-muodossa (oma invariantti englanninkielinen muotoilija Ai-projektissa:
  `B`, `KB`, `MB`, `GB`, `TB`, yksi desimaali, `InvariantCulture`), ei `ByteFormatter`-luokalla, joka seuraa
  `Loc.Current`-kieltä. Kun `sumSizes` on false (opt-in-moduulit, joiden kohteet voivat olla sisäkkäisiä kansioita),
  otsikkorivin yhteiskoko korvataan tekstillä `size not summed (nested items)`. Kategoria, jossa on enintään
  `FullListThreshold` (oletus 40) kohdetta, listataan kokonaan; suurempi listaa `SampleSize` (oletus 20) ensimmäistä
  ja rivin "(+N more, not listed)". Listattuja kohteita on koko tekstissä enintään `MaxListedItems` (oletus 400): kun
  jäljellä oleva kiintiö on pienempi kuin kategorian listattava määrä, kategoria katkaistaan kiintiöön ja saa rivin
  "(+N more, not listed)"; kiintiön loputtua loput kategoriat saavat vain otsikkorivin ja rivin "(items not listed)".
  Kohderivi: avain `i<n>` (juokseva 1..), valintamerkki `[x]`/`[ ]`, koko, siistitty polku, siistitty kuvaus katkaistuna
  120 merkkiin. `ScanDigest.KeyToItem` kuvaa avaimen `CleanItem`-olioon ja `ScanDigest.Categories` listaa kategorioiden
  nimet. Sama syöte antaa aina saman tekstin riippumatta `Loc.Current`-arvosta.
- REQ-4: `HealthDigest.Build(SystemFacts, IReadOnlyList<ModuleScanSummary>)` tuottaa tekstin: käyttöjärjestelmän
  versio, suoritinytimet, muisti (yhteensä ja vapaana, jos saatavilla), käyntiaika, jokainen asema (nimi, koko,
  vapaana, vapaana prosentteina), onko prosessi korotettu, sekä jokaisesta moduulista nimi, "not scanned" tai kohdemäärä
  ja yhteiskoko ja enintään 5 suurinta kategoriaa (nimi, määrä, koko). Kun `ModuleScanSummary.SizesSummable` on false
  (opt-in-moduulit, sisäkkäiset kohteet), moduulin ja kategorioiden koot korvataan tekstillä "size not summed (nested
  items)". Koot `AiFormat.Bytes`-muodossa. `SystemFactsCollector.Collect()` lukee tiedot
  (.NET: `Environment.OSVersion`, `ProcessorCount`, `TickCount64`, `DriveInfo.GetDrives()` kiinteät valmiit asemat,
  `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`; vapaa muisti Windowsissa `GlobalMemoryStatusEx`-kutsulla,
  muualla null) eikä heitä, vaikka jokin lähde epäonnistuu (kenttä jää null tai tyhjäksi).
- REQ-5: `AiReviewSchema.Json` on JSON-skeema (`JsonElement`-sanakirja, `additionalProperties: false` joka tasolla):
  `summary` (string), `categories[]` (`category`, `verdict`, `reason`), `items[]` (`key`, `verdict`, `reason`), kaikki
  pakollisia; `verdict` on enum `safe | caution | keep`. `AiReviewParser.Parse(json, usage, digest)` palauttaa
  `AiReview`-olion: tuntematon avain tai kategoria, jota digestissä ei ole, pudotetaan hiljaa; verdict-merkkijono
  tulkitaan kirjainkoosta riippumatta; virheellinen JSON tai puuttuva pakollinen kenttä heittää
  `AiException`-poikkeuksen tyypillä `AiErrorKind.InvalidResponse`.
- REQ-6: `VerdictApplier.Resolve(review, item)` palauttaa kohteen oman arvion, muuten kategorian arvion, muuten null.
  `VerdictApplier.AutoSelection(verdict, risk, current)` palauttaa `false`, kun arvio on Caution tai Keep; `true`, kun
  arvio on Safe ja riski ei ole High; `false`, kun riski on High (arviosta riippumatta); muuten `current`.
- REQ-7: `AiAssistant` (konstruktori `IAiBackend`, `AiAssistantOptions { Model, MaxHistory = 20, MaxHistoryChars = 120000 }`)
  pitää keskusteluhistorian `History` (`AiChatMessage(AiRole Role, string Text)`): `ReviewAsync(digest, language, ct)`
  lisää historiaan käyttäjäviestin (arvio-ohje + digestin teksti) ja avustajan viestin (palautettu JSON-teksti) ja
  palauttaa `AiReview`; `HealthCheckAsync(healthText, language, onDelta, ct)` ja `AskAsync(question, language, onDelta, ct)`
  lisäävät käyttäjäviestin ja avustajan vastauksen ja palauttavat `AiTextResult`. Historian koko on rajattu kahdella
  säännöllä: (a) kun uusi arvio onnistuu, aiempien arvioiden käyttäjäviestien digest-osa korvataan tekstillä
  "(earlier review of <module>; item list omitted)" niin, että vain uusimman arvion digest on kokonaisena;
  (b) kun viestejä on yli `MaxHistory` tai tekstien yhteispituus yli `MaxHistoryChars` merkkiä, historiaa katkaistaan
  alusta kokonaisina käyttäjä–avustaja-pareina, kunnes molemmat rajat täyttyvät (uusin pari säilyy aina).
  Mitoitus: digest on enintään 400 kohderiviä × noin 170 merkkiä ≈ 68 000 merkkiä (≈ 17 000–20 000 tokenia), joten
  120 000 merkkiä pitää yhden täyden digestin ja useita tekstivastauksia, ja yksi kutsu on enintään noin 35 000
  syötetokenia. `Reset()` tyhjentää. Arvio käyttää `CompleteJsonAsync`-kutsua (effort High, maxTokens 16000), teksti
  `StreamTextAsync`-kutsua (effort Medium, maxTokens 8000).
- REQ-8: `AiPrompts.System(Language)` palauttaa järjestelmäkehotteen (englanti ja suomi samasta rakenteesta, vastaus
  pyydetään annetulla kielellä), joka kertoo: avustaja toimii PC Cleaner -ohjelmassa (moduulit: tiedostosiivous,
  selainten välimuistit, roskakori, käynnistysohjelmat, levytilan analyysi, rekisteri, ohjelman poisto, prosessit;
  kuiva ajo oletuksena, karanteeni ja .reg-varmuuskopiot, loki), arvioiden määritelmät (safe = voi poistaa ilman
  tietojen menetystä; caution = luultavasti turvallinen, mutta tarkista; keep = ei saa poistaa: käyttäjän dokumentit,
  käytössä olevat ohjelmat, tuntemattomat kohteet), kielto suositella käyttäjän dokumenttien tai järjestelmätiedostojen
  poistoa, polkujen merkinnät `%USERPROFILE%`/`%USERNAME%` ovat tahallisia, vastaus tavallisena tekstinä ilman
  markdown-merkintöjä, lyhyt ja konkreettinen. `AiPrompts.ReviewInstruction(Language)` ja `HealthInstruction(Language)`
  ovat käyttäjäviestien alkuosat.
- REQ-9: `AnthropicBackend : IAiBackend` käyttää `AnthropicClient`-oliota ilman parametreja (avain ympäristöstä).
  `CompleteJsonAsync` asettaa `OutputConfig.Format = new JsonOutputFormat { Schema = schema }` ja palauttaa
  ensimmäisen tekstilohkon; `StreamTextAsync` kutsuu `CreateStreaming` ja välittää tekstideltat `onDelta`-kutsulla.
  Molemmat palauttavat `AiTextResult(Text, StopReason, AiUsage(InputTokens, OutputTokens), Truncated)`, jossa
  `Truncated` on tosi, kun `stop_reason` on `max_tokens`. `stop_reason == "refusal"` heittää `AiException(Refused)`
  ja `Category`-kenttään `StopDetails.Category`. SDK:n poikkeukset kuvataan: 401 → `Unauthorized`, 429 → `RateLimited`,
  muu 4xx → `BadRequest`, 5xx → `Server`, `AnthropicSseException` → `Server`, `AnthropicIOException` → `Network`,
  `AnthropicInvalidDataException` → `InvalidResponse`, muu `AnthropicException` → `Other`; mikään SDK:n poikkeus ei
  pääse läpi `AiException`-kääreen ohi. Alkuperäinen poikkeus on `InnerException`. `OperationCanceledException`
  kulkee läpi sellaisenaan.
- REQ-10: `AnthropicBackend.IsConfigured` on tosi täsmälleen silloin, kun ympäristömuuttuja `ANTHROPIC_API_KEY` ei
  ole tyhjä. Käynnistyksessä ei tehdä API-kutsua.

Asetukset

- REQ-11: `AppSettings` saa kentät `AiModel` (oletus `"claude-opus-5-5"`, JSON `aiModel`, null → oletus) ja
  `AiAutoMode` (bool, oletus false, JSON `aiAutoMode`). Vanha tiedosto ilman kenttiä latautuu oletuksin, ja
  `AppSettings.Update` säilyttää kentät muiden asetusten tallennuksessa.

Käyttöliittymä

- REQ-12: `AiAssistantViewModel` (UI, `MainViewModel.Ai`): `Messages` (`ObservableCollection<AiMessageViewModel>`),
  `IsAvailable` (= `AnthropicBackend.IsConfigured`), `UnavailableText` (nimeää ympäristömuuttujan `ANTHROPIC_API_KEY`),
  `IsThinking`, `AutoMode` (TwoWay; muutos tallennetaan `AppSettings.Update`-kutsulla, epäonnistuminen kirjataan
  varoituksena eikä estä vaihtoa), `Question`, `ModelName`, `HasFlagged`. Komennot: `ReviewCommand` (sallittu, kun
  avustaja on käytettävissä, `MainViewModel.IsBusy` on false, avustaja ei ajattele ja valitulla moduulilla on
  skannattuja kohteita), `HealthCheckCommand`, `AskCommand` (kysymys ei tyhjä), `DeselectFlaggedCommand`
  (`HasFlagged`), `ResetCommand`, `CancelCommand` (ajattelun aikana).
- REQ-13: Arvio: `ReviewCommand` rakentaa digestin valitun moduulin kaikista kohteista (valintatila
  `ItemViewModel.IsSelected`), lisää paneeliin käyttäjäviestin "Review results: <moduuli>" / "Tarkista tulokset:
  <moduuli>" (ei digestin tekstiä) ja avustajan viestin, jossa on `Summary` ja sen alla jokaisesta Caution- ja
  Keep-arviosta rivi "<arvio>: <polku tai kategoria> – <perustelu>". Arviot viedään moduulille
  (`ModuleViewModel.ApplyReview`): kategorian merkki ja kohteen merkki. Auto-tilassa lisäksi
  `ModuleViewModel.ApplyAutoSelection` ja viestiin rivi "Selected N, deselected M" / "Valittu N, poistettu valinnasta M".
  Token-määrät kirjataan lokiin Debug-tasolla.
- REQ-14: Koneen kunto: `HealthCheckCommand` kerää `SystemFactsCollector.Collect()` (`Task.Run`) ja moduulien
  yhteenvedot (`ModuleScanSummary` jokaisesta `MainViewModel.Modules`-moduulista nykyisestä skannaustuloksesta,
  `SizesSummable = SelectedByDefault`) ja suoratoistaa
  avustajan vastauksen: avustajan viesti luodaan tyhjänä ja sen `Text` kasvaa deltojen mukana UI-säikeessä.
- REQ-15: Kysymys: `AskCommand` lähettää `Question`-tekstin, tyhjentää kentän ja suoratoistaa vastauksen kuten REQ-14.
- REQ-16: Virheet: `AiException` näytetään paneelissa virheviestinä (tyyppi `AiMessageRole.Error`) lokalisoidulla
  tekstillä tyypin mukaan (MissingKey, Unauthorized "API key rejected", RateLimited, Network, Server, BadRequest, Refused
  "The assistant declined (category X)", InvalidResponse, Other) ja kirjataan lokiin varoituksena. Peruutus jättää
  jo saadun tekstin ja lisää rivin "(canceled)" / "(peruutettu)". `Truncated` lisää rivin "(answer was cut short)" /
  "(vastaus katkesi)".
- REQ-17: Merkit: `ItemViewModel` ja `CategoryViewModel` saavat `Verdict` (`AiVerdict?`), `VerdictReason`,
  `VerdictText` (nykyisellä kielellä: "safe"/"caution"/"keep" ja "turvallinen"/"varo"/"säilytä") ja näkyvyysliput
  `IsSafeVerdict`, `IsCautionVerdict`, `IsKeepVerdict`. Kortissa kategorian merkki on otsikkorivillä riskitekstin
  jälkeen ja kohteen merkki koon vasemmalla puolella; värit `RiskLowTextBrush` (safe), `RiskMediumTextBrush` (caution),
  `RiskHighTextBrush` (keep); ToolTip näyttää perustelun. `ModuleViewModel.LoadScan` ja `ClearScan` tyhjentävät
  merkit (`ClearReview`), ja `HasReview` kertoo, onko arvio voimassa.
- REQ-18: `DeselectFlaggedCommand` poistaa valinnasta jokaisen kohteen, jonka `VerdictApplier.Resolve` antaa Caution
  tai Keep, ja lisää paneeliin tiedoteviestin "Deselected N items" / "Poistettu valinnasta N kohdetta".
  `HasFlagged` on tosi, kun valitulla moduulilla on arvio ja vähintään yksi valittu kohde on Caution tai Keep.
- REQ-19: Automaattinen siivous (`MainViewModel.AutoCleanCommand`, sallittu kun `!IsBusy && !Ai.IsThinking &&
  Ai.IsAvailable && Ai.AutoMode`): vaiheet (1)–(3) ajetaan yhden `BeginBusy`-tilan ja yhden `CancellationTokenSource`-olion
  alla, joten alapalkin Peruuta on käytössä koko ajan ja keskeyttää minkä tahansa vaiheen: (1) skannaa kaikki
  `SelectedByDefault`-moduulit kuten "Skannaa kaikki"; (2) pyytää arvion jokaisesta moduulista, jolla on vähintään yksi
  kohde (paneeliin viestit kuten REQ-13, `BusyTitle` "Review: <moduuli>" / "Arvio: <moduuli>") ja soveltaa
  `ApplyAutoSelection`; (3) ajaa kuivan ajon kaikkien näiden moduulien valituille kohteille; (4) näyttää yhden
  vahvistusdialogin, jossa on jokaisen arvioidun moduulin `Summary`, kuivan ajon kohdemäärä ja koko sekä ohitetut
  syineen (enintään 4), painike "Delete permanently" / "Poista pysyvästi"; (5) ajaa oikean ajon, näyttää tulosdialogin
  ja skannaa moduulit uudelleen. Jos kuiva ajo ei poistaisi mitään, näytetään "Nothing to do" / "Ei mitään tehtävää".
  Opt-in-moduulit eivät ole mukana, High-riskin kohde ei ole koskaan valittuna. Peruutus vaiheissa 1–3 (alapalkin
  Peruuta tai paneelin Peruuta) keskeyttää ilman dialogia ja ilman puhdistusta, ja skannauksen peruutus todetaan
  skannauksen paluuarvosta, ei moduulien `HasScanned`-tilasta.
- REQ-20: Paneeli: `AiSplitView` (`SplitView`, `PanePlacement=Right`, `DisplayMode=Inline`, `OpenPaneLength=400`,
  oletuksena kiinni) ympäröi `NavigationView`-elementin `LogSplitView`-sisällön sisällä, joten moduulisivu kapenee
  paneelin auetessa ja lokipaneeli piirtyy sen päälle. Paneelissa ylhäältä alas: otsikko "AI assistant" /
  "Tekoälyavustaja" ja sulkunappi; InfoBar (`Severity=Warning`, `UnavailableText`), kun avustaja ei ole käytettävissä;
  ToggleSwitch "Auto mode" / "Auto-tila" (`Ai.AutoMode`); viestilista `AiMessageList` (käyttäjän viesti
  `AppCardHoverBrush`-taustalla ja vasen marginaali 24 px; avustajan viesti `AppCardBackgroundBrush` ja oikea
  marginaali 24 px; virhe `RiskHighTintBrush`; tiedote ilman taustaa; teksti valittavissa, rivitys),
  joka vierittää uusimman viestin näkyviin, kun viesti lisätään tai kasvaa; ProgressRing ja Peruuta ajattelun aikana;
  napit "Review results" / "Tarkista tulokset", "PC health" / "Koneen kunto", "Automatic cleanup" / "Automaattinen
  siivous" (näkyy vain Auto-tilassa), "Deselect flagged" / "Poista varoitetut valinnasta" (näkyy vain kun `HasFlagged`),
  "New conversation" / "Uusi keskustelu"; alimpana TextBox (`Question`, `UpdateSourceTrigger=PropertyChanged`,
  paikkamerkki "Ask the assistant" / "Kysy avustajalta", Enter lähettää) ja lähetysnappi. Alapalkin uusi nappi
  (FontIcon `&#xE99A;`, ToolTip "AI assistant" / "Tekoälyavustaja") lokinapin vasemmalla puolella avaa ja sulkee paneelin.
  Asetussivulle osio "AI assistant" / "Tekoälyavustaja": sama ToggleSwitch, teksti "Model: <ModelName>" ja vihje
  avaimesta (`UnavailableText`-teksti, kun avainta ei ole; muuten "API key: ANTHROPIC_API_KEY environment variable" /
  "API-avain: ympäristömuuttuja ANTHROPIC_API_KEY").
- REQ-21: `MainView.Detach` nollaa `AiMessageList.ItemsSource`-arvon ja irrottaa viestilistan
  `CollectionChanged`-tilauksen; näkymän uudelleenluonti (ulkoasun vaihto) säilyttää viestit, arviot ja Auto-tilan
  ViewModelissa. Kielen vaihto päivittää paneelin kiinteät tekstit (`UiText`), `UnavailableText`-tekstin ja merkkien
  `VerdictText`-tekstit; jo kirjoitetut viestit jäävät syntykieleensä.
- REQ-22: Dokumentit: `docs/ARCHITECTURE.md` saa luvun 7.2 "Tekoälyavustaja" (projekti, lähetettävät tiedot ja
  siistiminen, digestin rajat, arviot ja merkit, Auto-tila ja sen vahvistus, asetukset, virheet) ja luvun 2 kaavioon
  rivin `Cleaner.Ai` (viittaa vain Coreen, UI viittaa siihen); `README.md` rakennepuuhun `Cleaner.Ai/`, uusi osio
  "AI assistant" (avain ympäristömuuttujasta, mitä lähetetään, Auto-tila, `settings.json`-kentät `aiModel` ja
  `aiAutoMode`) ja `README.fi.md` vastaava suomeksi; `HANDOFF.md` taulukkoon rivi `src/Cleaner.Ai` ja päätöslistaan
  päätös 27 (tekoälyavustaja: tietosuoja, Auto-tilan rajat, NuGet vain Ai-projektissa).

## Edge cases

- EDGE-1: `ANTHROPIC_API_KEY` puuttuu → paneeli näyttää InfoBar-vihjeen, Review/Health/Ask/AutoClean eivät ole
  sallittuja, mitään poikkeusta ei synny; Auto-tilan kytkin toimii silti (tallentuu).
- EDGE-2: Moduuli skannataan uudelleen arvion jälkeen → merkit katoavat, `HasFlagged` on false, nappi "Poista
  varoitetut valinnasta" piiloutuu; keskustelu säilyy.
- EDGE-3: Vastauksen `items` sisältää avaimen, jota digestissä ei ole, tai kategorian, jota moduulilla ei ole →
  rivi pudotetaan, muut sovelletaan.
- EDGE-4: Kategoria, jossa ei ole yhtään valittua kohdetta → mukana digestissä (avustaja voi suositella valintaa).
- EDGE-5: `stop_reason == "refusal"` → paneeliin virheviesti luokan kanssa; historiaan ei lisätä avustajan viestiä,
  mutta käyttäjäviesti poistetaan myös (historia pysyy pareina).
- EDGE-6: `stop_reason == "max_tokens"` → teksti näytetään ja rivi "(vastaus katkesi)" lisätään; arviossa
  katkennut JSON → `InvalidResponse`-virhe.
- EDGE-7: Peruutus suoratoiston aikana → saatu osa jää viestiin, "(peruutettu)" perään; historiaan ei lisätä
  keskeneräistä vastausta eikä sen käyttäjäviestiä.
- EDGE-8: Automaattinen siivous, kun yhdelläkään oletusmoduulilla ei ole kohteita → arviota ei pyydetä, dialogi
  "Ei mitään tehtävää".
- EDGE-9: Automaattinen siivous, jossa kaikki arviot ovat keep/caution → 0 valittua → "Ei mitään tehtävää", mitään ei poisteta.
- EDGE-10: Ulkoasun vaihto suoratoiston aikana → viestit ovat ViewModelissa ja näkyvät uudessa näkymässä; paneeli
  on näkymän tilaa ja sulkeutuu (kuten lokipaneeli).
- EDGE-11: Skannaus tai puhdistus on käynnissä → Review/Health/Ask eivät ole sallittuja (`IsBusy`); avustajan
  ajattelun aikana Skannaa ja Puhdista ovat sallittuja, mutta arvion soveltaminen tarkistaa, että moduulin
  `ScanGeneration` (laskuri, joka kasvaa jokaisessa `LoadScan`- ja `ClearScan`-kutsussa) on sama kuin kutsun alussa;
  muuten arvio näytetään vain tekstinä eikä merkkejä tai valintoja muuteta.
- EDGE-12: Ohjelman poisto -moduuli ilman kohdetta (`HasScanned == false`) → Review ei ole sallittu.
- EDGE-13: Kohde, jonka arvio on Safe mutta riski High → ei koskaan valita automaattisesti (REQ-6); merkki näytetään.

## Constraints

- CON-1: `Cleaner.Ai` viittaa vain `Cleaner.Core`-projektiin ja `Anthropic`-pakettiin. Core, CLI ja moduulit eivät
  saa uutta NuGet-pakettia (päätös 4 HANDOFF.md:ssä); UI saa viittauksen `Cleaner.Ai`-projektiin.
- CON-2: net8.0, `Nullable` ja `ImplicitUsings` päällä, `LangVersion latest`; Windows-rajapinnat
  `[SupportedOSPlatform("windows")]`-merkinnällä; P/Invoke `[DllImport]` ja `#region P/Invoke` (RecycleBinModule-käytäntö).
  `dotnet build PcCleaner.sln -warnaserror` on lint-tarkistus.
- CON-3: Koneelta lähtee vain REQ-3:n ja REQ-4:n teksti sekä käyttäjän kirjoittama kysymys. Lokitiedostoa, asetuksia,
  API-avainta tai rekisteriarvojen sisältöä ei lähetetä. Kaikki polut kulkevat `PathSanitizer`-luokan kautta.
- CON-4: Mitään ei poisteta ilman vahvistusdialogia: tavallinen ketju säilyy ennallaan (kuiva ajo → yhteenveto →
  vahvistus), Auto-tilassa yksi dialogi, joka näyttää kuivan ajon määrät.
- CON-5: Mallin tunnus luetaan asetuksista, oletus `claude-opus-5-5` sellaisenaan (ei päivämääräliitettä).
- CON-6: API-avain luetaan vain ympäristömuuttujasta; sitä ei kirjoiteta asetuksiin, lokiin eikä paneeliin.
- CON-7: Kaikki käyttäjälle näkyvät tekstit `Loc.T("English", "Suomi")`-pareina, englanti ensin; kielen vaihto ilman
  uudelleenkäynnistystä (`UiText.Refresh`, `RefreshLanguage`).
- CON-8: Avustajan teksti näytetään `TextBlock`-elementissä (rivitys, valittavissa); markdownia ei renderöidä, ja
  kehote pyytää tavallista tekstiä.
- CON-9: UI-säie: `AiAssistant`-kutsut awaitataan UI-säikeessä (SDK on asynkroninen), `onDelta` päivittää viestin
  `Text`-arvon `DispatcherQueue.TryEnqueue`-kutsulla, koska delta voi tulla taustasäikeestä.

## Assumptions

- Malli `claude-opus-5-5` (claude-api-skillin oletus); vaihto `settings.json`-kentästä `aiModel` ilman UI:ta.
  Thinking-parametria ei lähetetä (adaptiivinen oletus), `effort` High arviolle ja Medium keskustelulle.
- Palvelinpuolen refusal-fallback (beta-otsake) jätetään pois: se vaatisi beta-nimiavaruuden koko kutsupolkuun.
  Refusal käsitellään virheviestinä (EDGE-5).
- Prompt caching jätetään pois: järjestelmäkehote on alle pienimmän välimuistitettavan etuliitteen.
- Digest listaa kohteet koon mukaan, koska suurimmat kohteet ovat arvion kannalta merkittävimmät; 14 000 tiedoston
  kategoria näkyy otsikkona ja 20 kohteen otoksena, ja arvio tulee silloin kategoriatasolla.
- Kategoriatason arvio koskee kaikkia kategorian kohteita, myös listaamattomia; kohteen oma arvio voittaa.
- Historiaan tallennetaan arvion raaka JSON-teksti, jotta jatkokysymykset ("miksi X on varo?") saavat perustelut.
- `AiAssistant`-olio luodaan kerran `AiAssistantViewModel`-oliossa; `Reset` tyhjentää historian mutta ei merkkejä.
- Paneeli on `Inline`, ei `Overlay`, koska käyttäjä halusi katsella tuloksia keskustelun aikana. Ikkunan minimileveys
  960 px antaa paneelin auki ollessa sisällölle 320 px; kortit rivittyvät.
- Koneen kunnon yhteenvetoon ei käynnistetä skannauksia; skannaamaton moduuli näkyy "not scanned", ja avustaja voi
  ehdottaa skannausta.
- `SystemFacts` ei lue asennettuja ohjelmia eikä prosesseja erikseen; ne tulevat moduulien tuloksista, jos ne on skannattu.
- Token-määrät eivät näy paneelissa (vain loki Debug-tasolla).
- `AiMessageViewModel.Text` päivittyy deltoittain `OnPropertyChanged`-kutsulla; 500 deltaa sekunnissa ei ole ongelma TextBlockille.
- Testit `AnthropicBackend`-luokalle eivät kutsu verkkoa: luokka testataan vain käännöksellä ja `IsConfigured`-tarkistuksella
  (ympäristömuuttuja asetetaan testissä `Environment.SetEnvironmentVariable`-kutsulla prosessin tasolla ja palautetaan).
- Tämän repon jokainen `dotnet build`/`dotnet test` kääntää jaetut projektit samoihin `obj/`-kansioihin, joten kahden
  tehtävän rinnakkainen käännös törmäisi. Siksi aallot ovat yhden tehtävän aaltoja, paitsi aalto 4, jossa
  dokumenttitehtävän Verify ei käännä mitään.

## Disposition

- CON-1: toteutuu rakenteella; T1:n Verify tarkistaa `grep ProjectReference` ja `grep PackageReference` Ai-csproj:stä
  ja ettei Core/Cli/Modules-csproj:issa ole `PackageReference`-rivejä.
- CON-3: toteutuu puuttumalla: vain `ScanDigest.Text`, `HealthDigest`-teksti ja `Question` päätyvät `IAiBackend`-kutsuihin
  (T4 rakentaa kutsut; AC-3 tarkistaa koodista grep-haulla, että `CompleteJsonAsync`/`StreamTextAsync`-kutsuja on vain `AiAssistant`-luokassa).
- CON-5, CON-6: T3 (`AnthropicClient` ilman parametreja, malli optioista) ja T4 (`ModelName` asetuksista); mitään
  avainta ei kirjoiteta, tarkistus AC-3.
- EDGE-4, EDGE-13: seuraavat REQ-3:n ja REQ-6:n sanamuodosta, testataan T1:ssä.
- EDGE-10: seuraa nykyisestä uudelleenluontimallista (ViewModel säilyy); T5 huolehtii `Detach`-nollauksesta (REQ-21).

## Wave 1

Yksi tehtävä: T1 luo Ai-projektin, sen rajapinnat ja puhtaan logiikan sekä asetuskentät, joihin kaikki muut nojaavat.

### T1: Cleaner.Ai-projekti, mallit, digestit, kehotteet, jäsennin ja asetuskentät
- Status: done 6477be4
- Model: fable
- Requirements: REQ-1 (ilman UI-csproj-viittausta, se on T4:ssä), REQ-2, REQ-3, REQ-4, REQ-5, REQ-6, REQ-7, REQ-8, REQ-11
- Constraints/edge: CON-1, CON-2, CON-3, CON-5, CON-7, EDGE-3, EDGE-4, EDGE-5, EDGE-7, EDGE-13
- Files: PcCleaner.sln, tests/Cleaner.Tests/Cleaner.Tests.csproj,
  src/Cleaner.Ai/Cleaner.Ai.csproj,
  src/Cleaner.Ai/AiModels.cs,
  src/Cleaner.Ai/IAiBackend.cs,
  src/Cleaner.Ai/AiException.cs,
  src/Cleaner.Ai/AiFormat.cs,
  src/Cleaner.Ai/PathSanitizer.cs,
  src/Cleaner.Ai/ScanDigest.cs,
  src/Cleaner.Ai/SystemFacts.cs,
  src/Cleaner.Ai/HealthDigest.cs,
  src/Cleaner.Ai/AiReviewSchema.cs,
  src/Cleaner.Ai/AiReviewParser.cs,
  src/Cleaner.Ai/VerdictApplier.cs,
  src/Cleaner.Ai/AiPrompts.cs,
  src/Cleaner.Ai/AiAssistant.cs,
  src/Cleaner.Core/Services/AppSettings.cs,
  tests/Cleaner.Tests/LocalizationTests.cs,
  tests/Cleaner.Tests/PathSanitizerTests.cs,
  tests/Cleaner.Tests/ScanDigestTests.cs,
  tests/Cleaner.Tests/HealthDigestTests.cs,
  tests/Cleaner.Tests/AiReviewParserTests.cs,
  tests/Cleaner.Tests/VerdictApplierTests.cs,
  tests/Cleaner.Tests/AiAssistantTests.cs
- Verify: `dotnet build PcCleaner.sln -warnaserror` → "0 Warning(s)", "0 Error(s)";
  `dotnet test PcCleaner.sln --no-build --filter "FullyQualifiedName~PathSanitizerTests|FullyQualifiedName~ScanDigestTests|FullyQualifiedName~HealthDigestTests|FullyQualifiedName~AiReviewParserTests|FullyQualifiedName~VerdictApplierTests|FullyQualifiedName~AiAssistantTests|FullyQualifiedName~LocalizationTests"` → "Passed!", 0 failed;
  `grep -c "ProjectReference" src/Cleaner.Ai/Cleaner.Ai.csproj` → 1; `grep -c 'PackageReference Include="Anthropic" Version="12.58.0"' src/Cleaner.Ai/Cleaner.Ai.csproj` → 1;
  `grep -l PackageReference src/Cleaner.Core/*.csproj src/Cleaner.Cli/*.csproj src/Cleaner.Modules.*/*.csproj` → ei tulostetta
- Task:
  Luo uusi kirjastoprojekti `src/Cleaner.Ai` (nimiavaruus `Cleaner.Ai`) PC Cleaner -sovellukseen (C# 12, .NET 8,
  WinUI 3 -siivousohjelma; moduulit tuottavat `CleanItem`-kohteita, käyttöliittymä näyttää ne kategorioittain
  valintaruuduilla). Projekti sisältää tekoälyavustajan mallit, rajapinnan taustapalveluun, polkujen siistimisen,
  skannaustulosten ja koneen tilan tiivistämisen tekstiksi, JSON-skeeman ja jäsentimen, arvioiden soveltamissäännöt,
  kehotteet ja keskustelun hallinnan. Varsinainen Anthropic-SDK:ta kutsuva toteutus (`AnthropicBackend`) ja
  käyttöliittymä tulevat myöhemmissä tehtävissä, jotka nojaavat tässä määriteltyihin tyyppeihin, joten nimet ja
  muodot alla ovat sitovia. Lue ensin `docs/ARCHITECTURE.md` luvut 3 ja 9, `src/Cleaner.Core/Models/CleanItem.cs`,
  `src/Cleaner.Core/Services/ByteFormatter.cs`, `src/Cleaner.Core/Localization/Loc.cs` ja
  `src/Cleaner.Modules.Files/RecycleBinModule.cs` (P/Invoke-käytäntö).

  Projekti: kopioi `src/Cleaner.Modules.Startup/Cleaner.Modules.Startup.csproj` pohjaksi (net8.0, Nullable, ImplicitUsings,
  LangVersion latest, viittaus `..\Cleaner.Core\Cleaner.Core.csproj`) ja lisää `<PackageReference Include="Anthropic" Version="12.58.0" />`.
  Paketti on todennettu: se kohdistuu net8.0:aan ja tuo mukanaan System.Text.Json 10.0.6:n; `dotnet restore` toimii
  ilman lisäasetuksia. Älä tutki pakettia uudelleen äläkä lisää muita paketteja. Lisää projekti `PcCleaner.sln`-tiedostoon
  GUID:lla `{A1B2C3D4-0011-4000-8000-000000000011}` (Project-lohko Processes-lohkon jälkeen, neljä
  `Debug|Any CPU` / `Release|Any CPU` -riviä `ProjectConfigurationPlatforms`-osioon Processes-rivien jälkeen; malli:
  rivit GUID:lla `{A1B2C3D4-0010-...}`). Lisää `ProjectReference` tiedostoon `tests/Cleaner.Tests/Cleaner.Tests.csproj`
  (`..\..\src\Cleaner.Ai\Cleaner.Ai.csproj`). UI-projektin viittaus lisätään myöhemmässä tehtävässä; älä koske siihen.

  Tyypit (kaikki `public`, `sealed` missä mahdollista; `AiModels.cs` sisältää pienet recordit ja enumit):

  ```csharp
  namespace Cleaner.Ai;
  public enum AiVerdict { Safe, Caution, Keep }
  public enum AiRole { User, Assistant }
  public enum AiEffort { Low, Medium, High }
  public enum AiErrorKind { MissingKey, Unauthorized, RateLimited, Network, Server, BadRequest, Refused, InvalidResponse, Other }
  public sealed record AiUsage(long InputTokens, long OutputTokens);
  public sealed record AiChatMessage(AiRole Role, string Text);
  public sealed record AiCategoryVerdict(string Category, AiVerdict Verdict, string Reason);
  public sealed record AiItemVerdict(string ItemId, AiVerdict Verdict, string Reason);   // ItemId = CleanItem.Id
  public sealed record AiReview(string Summary, IReadOnlyList<AiCategoryVerdict> Categories, IReadOnlyList<AiItemVerdict> Items, AiUsage Usage);
  public sealed record AiTextResult(string Text, string StopReason, AiUsage Usage, bool Truncated);
  public sealed record AiRequestOptions(string Model, AiEffort Effort, int MaxTokens);
  public sealed record ModuleScanSummary(string ModuleId, string DisplayName, bool Scanned, int Count, long TotalBytes,
      bool SizesSummable, IReadOnlyList<(string Name, int Count, long Bytes)> Categories);
  public static class AiFormat { public static string Bytes(long bytes); }   // invariantti: "0 B", "1.5 KB", "61.9 MB", "1.2 GB", "1.0 TB"
  public sealed record DriveFacts(string Name, string? Label, long TotalBytes, long FreeBytes);
  public sealed record SystemFacts(string OsVersion, int ProcessorCount, long? TotalMemoryBytes, long? AvailableMemoryBytes,
      TimeSpan Uptime, IReadOnlyList<DriveFacts> Drives, bool IsElevated);
  public sealed class AiAssistantOptions { public string Model { get; init; } = "claude-opus-5-5"; public int MaxHistory { get; init; } = 20; public int MaxHistoryChars { get; init; } = 120_000; }

  public sealed class AiException : Exception
  {
      public AiErrorKind Kind { get; }
      public string? Category { get; }          // refusal-luokka, muuten null
      public AiException(AiErrorKind kind, string message, Exception? inner = null, string? category = null);
  }

  public interface IAiBackend
  {
      /// JSON-skeeman mukainen vastaus. Text on skeeman mukainen JSON-teksti. Heittää AiException-poikkeuksen.
      Task<AiTextResult> CompleteJsonAsync(string system, IReadOnlyList<AiChatMessage> messages, JsonElement schema,
          AiRequestOptions options, CancellationToken ct);
      /// Suoratoistettu tekstivastaus: onDelta saa jokaisen tekstipalan (voi tulla taustasäikeestä). Palauttaa kokonaisen tekstin.
      Task<AiTextResult> StreamTextAsync(string system, IReadOnlyList<AiChatMessage> messages, Action<string> onDelta,
          AiRequestOptions options, CancellationToken ct);
  }

  public sealed class PathSanitizer
  {
      public PathSanitizer(string userProfile, string userName);
      public static PathSanitizer ForCurrentUser();   // Environment.GetFolderPath(UserProfile), Environment.UserName
      public string Sanitize(string text);
  }

  public sealed class DigestOptions { public int FullListThreshold { get; init; } = 40; public int SampleSize { get; init; } = 20; public int MaxListedItems { get; init; } = 400; }
  public sealed class ScanDigest
  {
      public string ModuleName { get; }
      public string Text { get; }
      public IReadOnlyDictionary<string, CleanItem> KeyToItem { get; }   // "i1" → kohde
      public IReadOnlyList<string> Categories { get; }
      public static ScanDigest Build(string moduleName, IEnumerable<(CleanItem Item, bool Selected)> items, bool sumSizes, DigestOptions options, PathSanitizer sanitizer);
  }
  public static class HealthDigest { public static string Build(SystemFacts facts, IReadOnlyList<ModuleScanSummary> modules); }
  public static class SystemFactsCollector { public static SystemFacts Collect(); }   // SystemFacts-record pysyy puhtaana datana
  public static class AiReviewSchema { public static JsonElement Json { get; } }
  public static class AiReviewParser { public static AiReview Parse(string json, AiUsage usage, ScanDigest digest); }
  public static class VerdictApplier
  {
      public static AiVerdict? Resolve(AiReview review, CleanItem item);
      public static bool? AutoSelection(AiVerdict? verdict, RiskLevel risk, bool current);
  }
  public static class AiPrompts
  {
      public static string System(Language language);
      public static string ReviewInstruction(Language language);
      public static string HealthInstruction(Language language);
  }
  public sealed class AiAssistant
  {
      public AiAssistant(IAiBackend backend, AiAssistantOptions options);
      public IReadOnlyList<AiChatMessage> History { get; }
      public Task<AiReview> ReviewAsync(ScanDigest digest, Language language, CancellationToken ct);
      public Task<AiTextResult> HealthCheckAsync(string healthText, Language language, Action<string> onDelta, CancellationToken ct);
      public Task<AiTextResult> AskAsync(string question, Language language, Action<string> onDelta, CancellationToken ct);
      public void Reset();
  }
  ```

  Säännöt:
  - `AiFormat.Bytes`: oma invariantti muotoilija (`InvariantCulture`, yksiköt B/KB/MB/GB/TB, jako 1024:llä, yksi
    desimaali paitsi tavuilla: "0 B", "512 B", "1.5 KB", "61.9 MB"). Älä käytä `Cleaner.Core.Services.ByteFormatter`-luokkaa
    missään Ai-projektin tekstissä: se muotoilee `Loc.Current`-kielen mukaan (suomeksi "Mt" ja desimaalipilkku), ja
    testit ajetaan oletuksena suomeksi. Lisää testi, että digest on sama `Loc.Current = Fi` ja `En` -arvoilla.
  - `PathSanitizer.Sanitize`: korvaa profiilipolun (esim. `C:\Users\ville`; vain `\`-erotin) merkinnällä
    `%USERPROFILE%` kaikkialla tekstissä kirjainkoosta riippumatta, mutta vain kun osumaa seuraa `\`, tekstin loppu tai
    muu merkki kuin kirjain, numero, `_`, `-` tai `.` (säännöllinen lauseke `(?<![\w.-])` ei tarvita alkuun, loppuun
    `(?![\w.-])`); sen jälkeen korvaa `\Users\<userName>\`-esiintymät muodolla `\Users\%USERNAME%\`. Muiden käyttäjien
    `\Users\muu\` ja etuliitteen jakava `\Users\villeneuve\` eivät muutu. Tyhjä käyttäjänimi tai profiili → kyseinen
    korvaus ohitetaan.
  - `ScanDigest.Build`: ryhmittele kategorioittain (säilytä ensiesiintymisjärjestys), kategorian sisällä koko laskevasti,
    sitten polku ordinal. Otsikkorivi: `## <Category> (risk <Low|Medium|High>, <n> items, <koko>, <m> selected)`, koko
    `AiFormat.Bytes`-muodossa; kun `sumSizes` on false, koon tilalla `size not summed (nested items)`. Kategorian
    riski on sen kohteiden suurin riski. Kohderivi: `- i<n> [x] <koko> <siistitty polku> — <siistitty kuvaus>` (kuvaus
    pois, jos tyhjä; katkaise 120 merkkiin ja lisää "…"); `[ ]` kun ei valittu. Listattava määrä on `min(n, n <= FullListThreshold ? n : SampleSize, jäljellä oleva kiintiö)`,
    kiintiö alussa `MaxListedItems` ja vähenee jokaisesta listatusta rivistä. Kun listattava määrä on pienempi kuin
    `n`, perään rivi `(+<n - listattu> more, not listed)`; kun kiintiö on 0, kategoria saa vain otsikkorivin ja rivin
    `(items not listed)`. Avaimet juoksevat koko digestin yli (`i1`, `i2`, ...) ja vastaavat täsmälleen listattuja
    rivejä. Tekstin alussa rivi `# Module: <moduleName>` ja rivi, joka selittää merkinnät
    (`[x] = currently selected for cleanup`). Englanninkielinen teksti; se ei näy käyttäjälle.
  - `HealthDigest.Build`: englanninkielinen teksti, koot `AiFormat.Bytes`. Osiot `# System` (OS, cores, memory
    total/available tai "unknown", uptime `d h min`, elevated yes/no), `# Drives` (yksi rivi per asema: nimi, label,
    koko, vapaana, vapaana %), `# Cleanup modules` (per moduuli: `- <DisplayName> (<ModuleId>): not scanned` tai
    `<n> items, <koko>` ja sen alla enintään 5 suurinta kategoriaa `  - <Name>: <count> items, <koko>`; kun
    `SizesSummable` on false, kokojen tilalla `size not summed (nested items)` ja kategoriat järjestetään määrän mukaan).
  - `SystemFactsCollector.Collect()`: ei heitä koskaan; jokainen lähde omassa try-lohkossaan. `OsVersion` =
    `Environment.OSVersion.VersionString` ja 64-bittisyys; `TotalMemoryBytes` = `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`;
    `AvailableMemoryBytes` Windowsissa `GlobalMemoryStatusEx` (kernel32, rakenne `MEMORYSTATUSEX` `dwLength`-kentällä,
    `[DllImport("kernel32.dll", SetLastError = true)]`, `#region P/Invoke`, `[SupportedOSPlatform("windows")]`
    apuluokassa), muualla null; `Uptime` = `TimeSpan.FromMilliseconds(Environment.TickCount64)`; `Drives` =
    `DriveInfo.GetDrives()` joissa `IsReady && DriveType == Fixed`; `IsElevated` = `Cleaner.Core.Services.ElevationHelper.IsElevated()`.
  - `AiReviewSchema.Json`: rakenna `JsonSerializer.SerializeToElement`-kutsulla anonyymista oliosta:
    `{ type: "object", additionalProperties: false, properties: { summary: {type:"string"}, categories: {type:"array",
    items:{type:"object", additionalProperties:false, properties:{category:{type:"string"}, verdict:{type:"string",
    enum:["safe","caution","keep"]}, reason:{type:"string"}}, required:["category","verdict","reason"]}},
    items: {type:"array", items:{... key, verdict, reason ... required:["key","verdict","reason"]}} },
    required: ["summary","categories","items"] }`. Tämä muoto on todennettu API:a vasten (vastaus tuli skeeman mukaisena).
  - `AiReviewParser.Parse`: `JsonDocument.Parse`; puuttuva `summary`/`categories`/`items` tai väärä tyyppi →
    `AiException(InvalidResponse, ...)`; `JsonException` → sama. `verdict` jäsennetään `Enum.TryParse(ignoreCase: true)`;
    tuntematon verdict → rivi pudotetaan. Kategoria otetaan mukaan vain, jos nimi on `digest.Categories`-listassa
    (ordinal-vertailu); kohde vain, jos `key` on `digest.KeyToItem`-sanakirjassa; `ItemId` on kohteen `CleanItem.Id`.
    Sama avain kahdesti → ensimmäinen voittaa.
  - `VerdictApplier.Resolve`: kohteen oma arvio (`ItemId == item.Id`) voittaa, sitten kategoria (`Category == item.Category`), muuten null.
    `AutoSelection`: `risk == High` → false; `Caution`/`Keep` → false; `Safe` → true; null → `current`.
  - `AiPrompts.System(language)`: kirjoita englanninkielinen ja suomenkielinen versio samasta sisällöstä (REQ-8:n
    luettelo: ohjelman kuvaus ja moduulit, turvamekanismit, arvioiden määritelmät, kielto suositella käyttäjän
    dokumenttien tai järjestelmätiedostojen poistoa, polkumerkintöjen selitys, tavallinen teksti ilman markdownia,
    lyhyt ja konkreettinen, vastaa kielellä X). `ReviewInstruction`: pyytää arvion jokaisesta listatusta kategoriasta ja
    kohteista, joista on jotain erityistä sanottavaa (ei jokaisesta kohteesta), `summary` 2–4 lausetta, perustelut
    yhdellä lauseella, JSON-skeeman mukaan. `HealthInstruction`: pyytää arvion koneen kunnosta ja 3–6 konkreettista
    toimenpide-ehdotusta tärkeysjärjestyksessä, mainiten mitkä moduulit kannattaa skannata tai puhdistaa.
  - `AiAssistant`: `ReviewAsync` lisää historiaan `User`-viestin `ReviewInstruction + "\n\n" + digest.Text`, kutsuu
    `CompleteJsonAsync(AiPrompts.System(language), History, AiReviewSchema.Json, new(Model, High, 16000), ct)`, lisää
    `Assistant`-viestin vastauksen tekstillä ja palauttaa `AiReviewParser.Parse(...)`. Jos taustapalvelu heittää
    (myös `OperationCanceledException`), juuri lisätty käyttäjäviesti poistetaan ennen uudelleenheittoa (EDGE-5, EDGE-7).
    `HealthCheckAsync` ja `AskAsync` toimivat samoin `StreamTextAsync`-kutsulla (`Medium`, 8000). Historian rajaus
    (REQ-7): pidä kirjaa siitä, mitkä käyttäjäviestit ovat arvioiden digestejä (esim. sisäinen lista indeksejä tai
    kääreluokka, `History` paljastaa silti vain `AiChatMessage`-oliot); onnistuneen arvion jälkeen korvaa jokaisen
    aiemman arvion käyttäjäviestin teksti muodolla `ReviewInstruction + "\n\n(earlier review of <ModuleName>; item list omitted)"`.
    Sen jälkeen katkaise: kun viestejä on yli `MaxHistory` tai tekstien yhteispituus yli `MaxHistoryChars`, poista alusta
    kaksi viestiä kerrallaan, kunnes molemmat rajat täyttyvät tai jäljellä on vain uusin pari; ensimmäinen viesti on aina
    `User`. Historia annetaan taustapalvelulle kopiona (`ToList()`).
  - `AppSettings` (Core): lisää `public string AiModel { get; set; } = "claude-opus-5-5";` ja
    `public bool AiAutoMode { get; set; }` kommentteineen samaan tyyliin kuin `Palette`; `Load`-metodiin
    `settings.AiModel ??= "claude-opus-5-5";`. Älä muuta JSON-asetuksia (camelCase antaa `aiModel`, `aiAutoMode`).

  Testit (xUnit, `tests/Cleaner.Tests`, kieli on oletuksena suomi testialustajan takia; nämä testit eivät riipu
  kielestä paitsi `AiPrompts`): `PathSanitizerTests` (profiili keskellä tekstiä, kirjainkoko, toisen käyttäjän
  polku ei muutu, tyhjä käyttäjänimi); `ScanDigestTests` (otsikkorivi, koko-järjestys, täysi lista 40:llä, otos 41:llä
  ja "+21 more", MaxListedItems-raja, avaimet juoksevat, `[x]`/`[ ]`, kuvauksen katkaisu, deterministisyys,
  siistitty polku); `HealthDigestTests` (`SystemFactsCollector.Collect()` ei heitä ja asemia ≥ 1; `Build` sisältää
  "not scanned" ja top-5-rajan); `AiReviewParserTests` (kelvollinen, tuntematon avain pois, tuntematon kategoria pois,
  verdict-kirjainkoko, rikkinäinen JSON → `AiException(InvalidResponse)`, puuttuva kenttä → sama);
  `VerdictApplierTests` (Resolve-järjestys, AutoSelection kaikki haarat ml. High+Safe → false);
  `AiAssistantTests` väärennetyllä `IAiBackend`-toteutuksella (ReviewAsync lisää 2 viestiä ja palauttaa arvion;
  AskAsync välittää deltat ja lisää 2 viestiä; backendin poikkeus poistaa käyttäjäviestin; katkaisu 20:een pareina;
  katkaisu `MaxHistoryChars`-rajalla pareina niin, että uusin pari säilyy vaikka ylittäisi rajan; toisen arvion jälkeen
  ensimmäisen arvion käyttäjäviesti sisältää "item list omitted" eikä digestin rivejä; Reset tyhjentää;
  `AiPrompts.System(Language.Fi)` sisältää "suomeksi" ja `System(Language.En)` sisältää "English");
  `ScanDigestTests`-luokassa lisäksi: `AiFormat.Bytes`-arvot ("0 B", "512 B", "1.5 KB", "61.9 MB") ja digest sama
  molemmilla kielillä (`Loc.Current` asetetaan ja palautetaan `finally`-lohkossa; luokka kokoelmaan `TestLanguage.Collection`).
  `LocalizationTests`: lisää testi `AppSettings_ai_fields_round_trip` (tallennus ja lataus `AiModel = "x"`,
  `AiAutoMode = true`; vanha JSON `{ "language": "fi" }` antaa oletukset) olemassa olevien `AppSettings_*`-testien
  viereen samaan tyyliin (väliaikainen polku, siivous).

## Wave 2

Yksi tehtävä: T3 toteuttaa SDK-kutsut T1:n rajapintaan (kääntää saman solution-puun).

### T3: AnthropicBackend
- Status: done 441f56d
- Model: opus
- Requirements: REQ-9, REQ-10
- Constraints/edge: CON-1, CON-2, CON-5, CON-6, EDGE-5, EDGE-6
- Files: src/Cleaner.Ai/AnthropicBackend.cs, tests/Cleaner.Tests/AnthropicBackendTests.cs
- Verify: `dotnet build PcCleaner.sln -warnaserror` → "0 Warning(s)", "0 Error(s)";
  `dotnet test PcCleaner.sln --no-build --filter "FullyQualifiedName~AnthropicBackendTests"` → "Passed!", 0 failed
- Task:
  Toteuta `Cleaner.Ai.AnthropicBackend : IAiBackend` projektissa `src/Cleaner.Ai` (PC Cleaner, C# 12, .NET 8).
  Projekti viittaa NuGet-pakettiin `Anthropic` 12.58.0, ja rajapinta `IAiBackend` sekä tyypit `AiTextResult`,
  `AiUsage`, `AiChatMessage`, `AiRole`, `AiEffort`, `AiRequestOptions`, `AiException`, `AiErrorKind` ovat jo
  olemassa tiedostoissa `src/Cleaner.Ai/IAiBackend.cs` ja `src/Cleaner.Ai/AiModels.cs` (lue ne; älä muuta niitä).

  Rajapinta, johon toteutus konformoi:

  ```csharp
  public interface IAiBackend
  {
      Task<AiTextResult> CompleteJsonAsync(string system, IReadOnlyList<AiChatMessage> messages, JsonElement schema, AiRequestOptions options, CancellationToken ct);
      Task<AiTextResult> StreamTextAsync(string system, IReadOnlyList<AiChatMessage> messages, Action<string> onDelta, AiRequestOptions options, CancellationToken ct);
  }
  public sealed record AiTextResult(string Text, string StopReason, AiUsage Usage, bool Truncated);
  public sealed record AiRequestOptions(string Model, AiEffort Effort, int MaxTokens);   // AiEffort { Low, Medium, High }
  ```

  Todennettu SDK-käyttö (scratchpad-käännös 0/0 ja ajo oikealla avaimella 20.10.2026; älä tutki uudelleen):

  ```csharp
  using Anthropic; using Anthropic.Exceptions; using Anthropic.Models.Messages;
  AnthropicClient client = new();                                   // ANTHROPIC_API_KEY ympäristöstä
  var p = new MessageCreateParams {
      Model = options.Model, MaxTokens = options.MaxTokens,
      System = new List<TextBlockParam> { new() { Text = system } },   // List<TextBlockParam>, ei taulukko
      OutputConfig = new OutputConfig { Effort = Effort.High },        // Effort.Low/Medium/High; JSON: Format = new JsonOutputFormat { Schema = dict }
      Messages = [ new() { Role = Role.User, Content = "..." }, new() { Role = Role.Assistant, Content = "..." } ] };
  await foreach (RawMessageStreamEvent ev in client.Messages.CreateStreaming(p)) {
      if (ev.TryPickStart(out var s)) { /* s.Message.Usage.InputTokens */ }
      if (ev.TryPickContentBlockDelta(out var d) && d.Delta.TryPickText(out var t)) onDelta(t.Text);
      if (ev.TryPickDelta(out var md)) { /* md.Delta.StopReason, md.Usage.OutputTokens */ } }
  var r = await client.Messages.Create(p);   // r.StopReason, r.Usage.InputTokens, r.Usage.OutputTokens, r.StopDetails?.Category
  foreach (var b in r.Content) if (b.TryPickText(out var t)) text = t.Text;
  // JsonOutputFormat.Schema on Dictionary<string, JsonElement>: muunna annettu JsonElement (object) sanakirjaksi
  // EnumerateObject-kutsulla: dict[prop.Name] = prop.Value.Clone().
  // HUOM: StopReason.ToString() palauttaa lainausmerkit ("end_turn"). Vertailu: `r.StopReason == "refusal"` kääntyy
  // README-esimerkin mukaan; jos ei, käytä `r.StopReason?.ToString()?.Trim('"')`. Tallenna AiTextResult.StopReason ilman lainausmerkkejä.
  ```

  Vaatimukset:
  - `CompleteJsonAsync`: `JsonOutputFormat` skeemalla, ei suoratoistoa, `Text` = ensimmäinen tekstilohko (tyhjä, jos
    ei ole). `StreamTextAsync`: `CreateStreaming`, deltat `onDelta`-kutsuun sitä mukaa, `Text` = deltojen
    yhdistelmä (`StringBuilder`); syöttötokenit `message_start`-tapahtumasta, tulostokenit `message_delta`-tapahtumasta.
  - `AiEffort` → `Effort.Low/Medium/High`. Thinking-parametria ei aseteta. `Role`-kuvaus: `AiRole.User → Role.User`,
    `AiRole.Assistant → Role.Assistant`.
  - `Truncated = StopReason == "max_tokens"`. `StopReason == "refusal"` → `throw new AiException(AiErrorKind.Refused,
    "...", category: r.StopDetails?.Category?.ToString()?.Trim('"'))` (suoratoistossa `md.Delta.StopReason`; StopDetails ei
    ole saatavilla suoratoistossa → category null, ellei se löydy `md.Delta`-oliosta; kokeile kääntämällä, älä arvaa).
  - Poikkeusten kuvaus `catch`-ketjuna tarkimmasta yleisimpään: `AnthropicUnauthorizedException` → `Unauthorized`;
    `AnthropicRateLimitException` → `RateLimited`; `Anthropic4xxException` → `BadRequest`; `Anthropic5xxException` →
    `Server`; `AnthropicSseException` → `Server`; `AnthropicIOException` → `Network`; `AnthropicInvalidDataException`
    → `InvalidResponse`; viimeisenä `AnthropicException` → `Other`. Luokkahierarkia (luettu 12.58.0:n metadatasta,
    nimiavaruus `Anthropic.Exceptions`): `AnthropicException : Exception`; `AnthropicServiceException : AnthropicException`;
    `AnthropicApiException : AnthropicServiceException`; `Anthropic4xxException`, `Anthropic5xxException`,
    `AnthropicUnexpectedStatusCodeException : AnthropicApiException`; `AnthropicUnauthorizedException`,
    `AnthropicRateLimitException`, `AnthropicBadRequestException`, `AnthropicForbiddenException`,
    `AnthropicNotFoundException`, `AnthropicUnprocessableEntityException : Anthropic4xxException`;
    `AnthropicSseException : AnthropicServiceException`; `AnthropicIOException : AnthropicException`;
    `AnthropicInvalidDataException : AnthropicException`. Nämä nimet ovat varmistettuja; älä etsi niitä uudelleen
    äläkä kirjoita omaa HTTP-koodin tulkintaa. Viesti = SDK:n viesti, `InnerException` = alkuperäinen.
    `OperationCanceledException` kulkee läpi. Sieppaa myös `onDelta`-kutsun ulkopuolelta tuleva `JsonException`
    (tai muu `Exception`, joka ei ole `OperationCanceledException`) ja kääri se `Other`-tyypiksi, jotta kutsuja voi
    luottaa siihen, että taustapalvelu heittää vain `AiException`- tai `OperationCanceledException`-poikkeuksia.
  - `public static bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));`
    Konstruktori heittää `AiException(MissingKey)`, jos avainta ei ole. Avainta ei lueta muualta eikä kirjata minnekään.
  - Luokka ei saa viitata UI:hin eikä `Loc`-kieleen; viestit englanniksi (UI lokalisoi tyypin mukaan).

  Testit `AnthropicBackendTests` ilman verkkoa: `IsConfigured` on false, kun muuttuja on tyhjä, ja true, kun se on
  asetettu (`Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", ...)` prosessin tasolla, alkuperäinen arvo
  palautetaan `finally`-lohkossa; luokka xUnit-kokoelmaan `TestLanguage.Collection`, jotta se ei aja rinnakkain muiden
  ympäristöä muuttavien kanssa); konstruktori ilman avainta heittää `AiException` tyypillä `MissingKey`.

## Wave 3

Yksi tehtävä: T4 tuo avustajan ViewModelit ja kytkee ne `MainViewModel`-luokkaan (keskeinen tiedosto).

### T4: UI:n ViewModelit, Auto-tila ja automaattinen siivous
- Status: done 8609a18
- Model: fable
- Requirements: REQ-1 (UI-csproj-viittaus), REQ-12, REQ-13, REQ-14, REQ-15, REQ-16, REQ-17 (ViewModel-osuus), REQ-18, REQ-19, REQ-21 (RefreshLanguage-osuus)
- Constraints/edge: CON-3, CON-4, CON-5, CON-6, CON-7, CON-9, EDGE-1, EDGE-2, EDGE-5, EDGE-6, EDGE-7, EDGE-8, EDGE-9, EDGE-11, EDGE-12, EDGE-13
- Files: src/Cleaner.UI/Cleaner.UI.csproj,
  src/Cleaner.UI/ViewModels/AiAssistantViewModel.cs,
  src/Cleaner.UI/ViewModels/AiMessageViewModel.cs,
  src/Cleaner.UI/ViewModels/ItemViewModel.cs,
  src/Cleaner.UI/ViewModels/CategoryViewModel.cs,
  src/Cleaner.UI/ViewModels/ModuleViewModel.cs,
  src/Cleaner.UI/ViewModels/MainViewModel.cs,
  src/Cleaner.UI/Services/UiText.cs
- Verify: `dotnet build PcCleaner.sln -warnaserror` → "0 Warning(s)", "0 Error(s)" (UI-projekti kääntyy x64:nä
  solutionin kautta); `dotnet test PcCleaner.sln --no-build` → "Passed!", 0 failed
- Task:
  Lisää tekoälyavustajan ViewModel-kerros PC Cleanerin WinUI 3 -sovellukseen (`src/Cleaner.UI`, CommunityToolkit.Mvvm 8.4,
  partial-ominaisuudet `[ObservableProperty]`, `[RelayCommand]`). Kirjasto `src/Cleaner.Ai` on valmis: lue
  `AiModels.cs`, `IAiBackend.cs`, `AiException.cs`, `ScanDigest.cs`, `HealthDigest.cs`, `SystemFacts.cs`,
  `AiReviewParser.cs`, `VerdictApplier.cs`, `AiAssistant.cs`, `AnthropicBackend.cs` ennen kirjoittamista. Lue myös
  `MainViewModel.cs` kokonaan (BeginBusy/EndBusy, `ScanModulesAsync`, `CleanAsync`-dialogiketju, `RefreshLanguage`,
  `Dispose`), `ModuleViewModel.cs`, `CategoryViewModel.cs`, `ItemViewModel.cs`, `UiText.cs` ja `docs/ARCHITECTURE.md`
  luku 7 ja 9 (lokalisointi: jokainen teksti `Loc.T("English", "Suomi")`, englanti ensin; `UiText`-ominaisuudet ovat
  laskettuja ja `Refresh()` nostaa tyhjän PropertyChangedin). XAML tulee seuraavassa tehtävässä ja sitoo alla
  lueteltuihin jäseniin täsmälleen näillä nimillä; älä muuta `MainView.xaml`-tiedostoa.

  Lisää `src/Cleaner.UI/Cleaner.UI.csproj`-tiedostoon `<ProjectReference Include="..\Cleaner.Ai\Cleaner.Ai.csproj" />`.

  Sitovat jäsenet:

  ```csharp
  public enum AiMessageRole { User, Assistant, Error, Info }
  public sealed partial class AiMessageViewModel : ObservableObject
  {
      public AiMessageRole Role { get; }
      [ObservableProperty] public partial string Text { get; set; }
      public bool IsUser, IsAssistant, IsError, IsInfo;   // laskettuja Role-arvosta
      public void Append(string delta);                   // Text += delta (kutsutaan UI-säikeessä)
  }

  public sealed partial class AiAssistantViewModel : ObservableObject
  {
      public AiAssistantViewModel(MainViewModel main, ICleanerLogger log, DispatcherQueue dispatcher, Func<IAiBackend> backendFactory, bool isAvailable, AppSettings settings);
      public ObservableCollection<AiMessageViewModel> Messages { get; }
      public bool IsAvailable { get; }
      public string UnavailableText { get; }               // Loc.T, nimeää ANTHROPIC_API_KEY
      public string ModelName { get; }
      public string KeyHint { get; }                       // asetussivun teksti (REQ-20)
      [ObservableProperty] public partial bool IsThinking { get; set; }
      public bool AutoMode { get; set; }                   // tallentaa AppSettings.Update(s => s.AiAutoMode = value), nostaa PropertyChanged, ilmoittaa MainViewModel.AutoCleanCommand.NotifyCanExecuteChanged
      [ObservableProperty] public partial string Question { get; set; }
      public bool HasFlagged { get; }                      // laskettu valitusta moduulista; OnPropertyChanged kun arvio tai valinta muuttuu
      public IAsyncRelayCommand ReviewCommand, HealthCheckCommand, AskCommand; public IRelayCommand DeselectFlaggedCommand, ResetCommand, CancelCommand;
      public Task<AiReview?> ReviewModuleAsync(ModuleViewModel module, CancellationToken ct);  // yhteinen Review-komennolle ja automaattiselle siivoukselle; palauttaa null virheessä (virhe näytetty paneelissa); ct linkitetään omaan CTS:ään, joten paneelin Peruuta toimii myös automaattisessa siivouksessa
      public void RefreshLanguage();                       // OnPropertyChanged(UnavailableText, KeyHint, HasFlagged) ja komentojen tila
  }

  // ItemViewModel ja CategoryViewModel, molemmat:
  public AiVerdict? Verdict { get; }  public string VerdictReason { get; }  public string VerdictText { get; }
  public bool HasVerdict, IsSafeVerdict, IsCautionVerdict, IsKeepVerdict;
  public void SetVerdict(AiVerdict? verdict, string reason);   // nostaa kaikkien yllä olevien PropertyChangedin
  // ModuleViewModel:
  public AiReview? Review { get; }  public bool HasReview { get; }
  public int ScanGeneration { get; }            // kasvaa jokaisessa LoadScan- ja ClearScan-kutsussa (EDGE-11)
  public void ApplyReview(AiReview review);     // kategoriat ja kohteet (kohteelle vain oma arvio; kategorialle kategorian arvio), nostaa HasReview
  public void ClearReview();                    // kutsutaan LoadScan- ja ClearScan-metodeista
  public int DeselectFlagged();                 // VerdictApplier.Resolve == Caution|Keep → IsSelected = false; palauttaa määrän
  public (int Selected, int Deselected) ApplyAutoSelection();   // VerdictApplier.AutoSelection jokaiselle kohteelle; palauttaa muutosten määrät
  // MainViewModel:
  public AiAssistantViewModel Ai { get; }
  public IAsyncRelayCommand AutoCleanCommand { get; }   // [RelayCommand(CanExecute = nameof(CanAutoClean))], CanAutoClean => !IsBusy && !Ai.IsThinking && Ai.IsAvailable && Ai.AutoMode
  private Task<bool> ScanModulesCoreAsync(IReadOnlyList<ModuleViewModel> targets, CancellationTokenSource cts);  // nykyisen ScanModulesAsync-metodin runko ilman BeginBusy/EndBusy; palauttaa false peruutuksessa tai virheessä
  ```

  Käyttäytyminen:
  - `MainViewModel`-konstruktori luo `Ai = new AiAssistantViewModel(this, _log, dispatcher, () => new AnthropicBackend(),
    AnthropicBackend.IsConfigured, AppSettings.Load())`. `AiAssistant`-olio luodaan laiskasti ensimmäisellä
    kutsulla `backendFactory`-tehtaasta (`AiAssistantOptions { Model = settings.AiModel }`); `ModelName = settings.AiModel`;
    `AutoMode` alkuarvo `settings.AiAutoMode`. `RefreshLanguage` kutsuu `Ai.RefreshLanguage()`; `Dispose` peruu
    avustajan mahdollisen kutsun (`Ai.CancelCommand`).
  - `IsBusy`-muutos ilmoittaa `Ai`-komentojen `NotifyCanExecuteChanged` (lisää `partial void OnIsBusyChanged`;
    `[NotifyCanExecuteChangedFor]` ei riitä, koska komennot ovat toisessa oliossa). `Ai.IsThinking`-muutos ilmoittaa
    `AutoCleanCommand.NotifyCanExecuteChanged()` (lisää `partial void OnIsThinkingChanged`). `SelectedModule`-muutos
    ilmoittaa `ReviewCommand` ja `HasFlagged`; moduulin `SelectedCount`/`HasScanned`-muutos samoin (laajenna `OnModulePropertyChanged`).
  - Refaktoroi `ScanModulesAsync` kahtia: `ScanModulesCoreAsync(targets, cts)` sisältää nykyisen rungon (silmukka,
    `OperationCanceledException`- ja `Exception`-käsittely dialogeineen) ja palauttaa `true` vain, kun kaikki
    kohteet skannattiin loppuun; `ScanModulesAsync` tekee `BeginBusy`, kutsuu ydintä ja `EndBusy`. Nykyinen käytös
    säilyy merkki merkiltä (lokitekstit, dialogit, `ScanCanceled`-tila).
  - `ReviewCommand`: `module = main.SelectedModule`; CanExecute: `IsAvailable && !main.IsBusy && !IsThinking &&
    module is { HasScanned: true } && module.HasResults`. Suoritus: `ReviewModuleAsync(module, cts.Token)`.
  - `ReviewModuleAsync(module, ct)`: luo oman `CancellationTokenSource`-olion linkitettynä `ct`-tokeniin
    (`CreateLinkedTokenSource(ct)`) ja tallentaa sen `CancelCommand`-komentoa varten; lisää `User`-viestin
    `Loc.T($"Review results: {module.DisplayName}", $"Tarkista tulokset: {module.DisplayName}")`; `IsThinking = true`;
    tallentaa `generation = module.ScanGeneration`; rakentaa `ScanDigest.Build(module.DisplayName, kaikki kategoriat
    (Categories + HighRiskCategories) → (ItemViewModel.Item, ItemViewModel.IsSelected), sumSizes: module.SelectedByDefault,
    new DigestOptions(), PathSanitizer.ForCurrentUser())` `Task.Run`-kutsussa (lue ItemViewModel-arvot UI-säikeessä
    listaksi ennen `Task.Run`-kutsua); `await assistant.ReviewAsync(digest, Loc.Current, token)`; jos
    `module.ScanGeneration == generation` (EDGE-11), `module.ApplyReview(review)`; muuten vain teksti. Auto-tilassa
    (ja vain kun arvio sovellettiin) lisäksi `module.ApplyAutoSelection()` ja rivi
    `Loc.T($"Selected {s}, deselected {d}", $"Valittu {s}, poistettu valinnasta {d}")`. `Assistant`-viesti: `Summary`,
    tyhjä rivi, sitten jokaisesta Caution/Keep-arviosta rivi `"<VerdictText>: <polku tai kategoria> – <Reason>"`
    (kohteen arviolla polku `item.Path`, kategorian arviolla kategorian nimi). Loki Debug: `"AI review: {in} in / {out} out tokens"`.
    Virhe: `AiException` → `Error`-viesti `ErrorText(ex)` + `_log.Warning`; `OperationCanceledException` → `Info`-viesti
    `Loc.T("(canceled)", "(peruutettu)")`; `finally IsThinking = false`; palauttaa arvion tai null. `HasFlagged` päivitetään.
  - `HealthCheckCommand`: `User`-viesti `Loc.T("PC health check", "Koneen kunnon tarkistus")`; facts `Task.Run(SystemFactsCollector.Collect)`;
    `ModuleScanSummary` jokaisesta `main.Modules`-moduulista (`Scanned = HasScanned`, `Count` ja `TotalBytes` kaikista
    kategorioista, `SizesSummable = SelectedByDefault` (opt-in-moduulien kohteet voivat olla sisäkkäisiä, ks.
    `ModuleViewModel.LoadScan`-kommentti), kategoriat (Name, Items.Count, TotalBytes)); tyhjä `Assistant`-viesti lisätään ennen kutsua;
    `onDelta` → `dispatcher.TryEnqueue(() => message.Append(delta))`; tulos `Truncated` → rivi
    `Loc.T("\n(answer was cut short)", "\n(vastaus katkesi)")`. Virheet ja peruutus kuten yllä (peruutuksessa teksti jää,
    rivi "(peruutettu)" perään). Loki Debug token-määrillä.
  - `AskCommand`: CanExecute `IsAvailable && !main.IsBusy && !IsThinking && !string.IsNullOrWhiteSpace(Question)`;
    `User`-viesti = kysymys; `Question = ""`; suoratoisto kuten yllä `assistant.AskAsync`.
  - `ErrorText(AiException)`: MissingKey → `UnavailableText`; Unauthorized → `Loc.T("The API key was rejected (401).", "API-avain hylättiin (401).")`;
    RateLimited → `Loc.T("Rate limit reached, try again in a moment.", "Pyyntöraja täynnä, yritä hetken kuluttua.")`;
    Network → `Loc.T("Network error: ...", "Verkkovirhe: ...")` + viesti; Server → `Loc.T("Server error: ...", "Palvelinvirhe: ...")`;
    BadRequest → `Loc.T("Request rejected: ...", "Pyyntö hylättiin: ...")`; Refused → `Loc.T($"The assistant declined (category {c}).", $"Avustaja kieltäytyi (luokka {c}).")`
    (c = Category ?? "unknown"); InvalidResponse → `Loc.T("The answer could not be interpreted.", "Vastausta ei voitu tulkita.")`;
    Other → viesti. Virheviestin alkuun ei lisätä avainta eikä pyynnön sisältöä.
  - `DeselectFlaggedCommand`: `n = module.DeselectFlagged()`; `Info`-viesti `Loc.T($"Deselected {n} items", $"Poistettu valinnasta {n} kohdetta")`;
    `main.NotifySelectionChanged()`.
  - `ResetCommand`: `assistant.Reset()` (jos luotu), `Messages.Clear()`. `CancelCommand`: peruu käynnissä olevan
    kutsun `CancellationTokenSource`-olion (Review/Health/Ask luovat sen itse; `ReviewModuleAsync` linkittää sen
    annettuun `ct`-tokeniin, joten automaattisessa siivouksessa sekä alapalkin Peruuta että paneelin Peruuta
    keskeyttävät arvion). `AiAssistant`-kutsuja ei koskaan ajeta kahta yhtä aikaa: kaikki komennot ja `ReviewModuleAsync`
    tarkistavat `IsThinking`-lipun ja palaavat heti, jos kutsu on jo käynnissä.
  - `AutoMode`-setteri: ei tee mitään samalla arvolla; asettaa kentän, `AppSettings.Update(s => s.AiAutoMode = value)`
    try/catch → `_log.Warning(Loc.T($"Saving the auto mode setting failed: {m}", $"Auto-tilan tallennus epäonnistui: {m}"))`;
    `OnPropertyChanged`; `main.AutoCleanCommand.NotifyCanExecuteChanged()`.
  - `ItemViewModel`/`CategoryViewModel` `VerdictText`: Safe → `Loc.T("safe", "turvallinen")`, Caution → `Loc.T("caution", "varo")`,
    Keep → `Loc.T("keep", "säilytä")`, null → "". `RefreshLanguage` nostaa `VerdictText`-muutoksen (CategoryViewModel.RefreshLanguage
    kutsuu kohteiden `RefreshLanguage`-metodia; lisää `ItemViewModel.RefreshLanguage()`).
  - `ModuleViewModel.ScanGeneration`: `int`, kasvatetaan `LoadScan`- ja `ClearScan`-metodien alussa (ei
    PropertyChanged-tapahtumaa tarvita). `ModuleViewModel.ApplyReview`: `Review = review`; kategorialle `SetVerdict` kategorian arviolla (nimi ordinal);
    kohteelle vain kohteen oma arvio (`AiItemVerdict.ItemId == Item.Id`), ei periytettyä, jotta merkki kertoo mistä
    avustaja sanoi jotain erityistä. `DeselectFlagged` ja `ApplyAutoSelection` käyttävät `VerdictApplier.Resolve`-kutsua
    (periytyminen kategoriasta) ja kiertävät `Categories` + `HighRiskCategories` kaikki `Items` (myös piilotetut yli 500).
    Valinnan muutos tehdään `ItemViewModel.IsSelected`-asetuksella, jolloin kategoria laskee summat itse.
  - `MainViewModel.AutoCleanAsync` (REQ-19): targets = `Modules.Where(m => m.SelectedByDefault)`;
    `var cts = BeginBusy(Loc.T("Automatic cleanup", "Automaattinen siivous"))` ja koko vaiheet 1–3 yhden
    `try/finally EndBusy()` -lohkon sisällä: (1) `if (!await ScanModulesCoreAsync(targets, cts)) return;` (peruutus tai
    virhe todetaan paluuarvosta, ei `HasScanned`-tilasta, koska peruutetun skannauksen jäljelle jääneet moduulit
    säilyttävät vanhan tuloksen); (2) jokaisesta moduulista, jolla `HasResults`: `BusyTitle = Loc.T($"Review: {m.DisplayName}", $"Arvio: {m.DisplayName}")`,
    `var review = await Ai.ReviewModuleAsync(module, cts.Token)` (Auto-tila on päällä, joten se soveltaa valinnat);
    null (virhe tai peruutus) → paluu (virhe näkyy paneelissa); (3) kerää `items` kaikkien targetsien `SelectedItems()`;
    jos tyhjä → `EndBusy` ja `InfoAsync(Loc.T("Nothing to do", "Ei mitään tehtävää"), Loc.T("The assistant did not select anything to delete.", "Avustaja ei valinnut mitään poistettavaksi."))`;
    `BusyTitle = Loc.T("Dry run: automatic cleanup", "Kuiva ajo: automaattinen siivous")` → `_engine.CleanAsync(items, CreateContext(true, cts), cts.Token)`;
    `OperationCanceledException` → loki "Dry run canceled." kuten `CleanAsync` ja paluu. Busy-tila päättyy ennen dialogia.
    dryRemoved == 0 → "Ei mitään tehtävää" kuten `CleanAsync`-metodissa. Yksi dialogi `ConfirmAsync(Loc.T("Automatic cleanup", "Automaattinen siivous"), text, Loc.T("Delete permanently", "Poista pysyvästi"))`,
    jossa `text` = jokaisesta arvioidusta moduulista `"<DisplayName>: <Summary>"` tyhjällä rivillä eroteltuna, sitten
    `Loc.T($"The dry run would delete {n} items ({size}).", $"Kuivassa ajossa poistetaan {n} kohdetta ({size}).")`,
    ohitetut kuten `CleanAsync` (enintään 4 syytä) ja rivi `Loc.T("Files do not go to the Recycle Bin. All deletions are written to the log.", "Tiedostot eivät mene roskakoriin. Kaikki poistot kirjataan lokiin.")`.
    Vahvistuksen jälkeen oikea ajo, tulosdialogi (`Loc.T($"Deleted {removed} items", $"Poistettu {removed} kohdetta")` + vapautettu koko + ohitetut) ja
    `ScanModulesAsync(targets)`. Peruutus kuivan ajon aikana → paluu ilman dialogia; peruutus oikean ajon aikana
    käsitellään kuten `CleanAsync`-metodissa (ilmoitus + uudelleenskannaus). Käytä olemassa olevia apureita; älä
    kopioi `CleanAsync`-metodin tekstejä tarpeettomasti, mutta älä myöskään muuta sen käytöstä.
  - `UiText`: lisää ominaisuudet `AiTitle` ("AI assistant"/"Tekoälyavustaja"), `AiAutoMode` ("Auto mode"/"Auto-tila"),
    `AiAutoModeHint` ("The assistant selects items itself and Automatic cleanup runs scan, review and cleanup with one confirmation. High-risk items and opt-in modules are never included." / "Avustaja valitsee kohteet itse, ja Automaattinen siivous skannaa, arvioi ja puhdistaa yhdellä vahvistuksella. Korkean riskin kohteet ja erikseen valittavat moduulit eivät ole koskaan mukana."),
    `AiReview` ("Review results"/"Tarkista tulokset"), `AiHealth` ("PC health"/"Koneen kunto"), `AiAutoClean`
    ("Automatic cleanup"/"Automaattinen siivous"), `AiDeselectFlagged` ("Deselect flagged"/"Poista varoitetut valinnasta"),
    `AiReset` ("New conversation"/"Uusi keskustelu"), `AiAskPlaceholder` ("Ask the assistant"/"Kysy avustajalta"),
    `AiSend` ("Send"/"Lähetä"), `AiThinking` ("Thinking..."/"Ajattelee..."), `AiSettingsHeader` ("AI assistant"/"Tekoälyavustaja"),
    `AiModelLabel` ("Model"/"Malli"), `AiPrivacyNote` ("Paths are sent with the user name hidden. Large lists are summarized." / "Polut lähetetään käyttäjänimi piilotettuna. Suuret listat tiivistetään.").
  - Kielen vaihto: `MainViewModel.RefreshLanguage` → `Ai.RefreshLanguage()`; moduulien `RefreshLanguage` kattaa merkit.
  - Älä lisää uusia tiedostoja XAML-puolelle; `MainView.xaml` ja `.xaml.cs` kuuluvat seuraavaan tehtävään.

## Wave 4

T5 (XAML ja code-behind) ja T6 (dokumentit) ovat riippumattomia: eri tiedostot, ja T6:n Verify ei käännä mitään,
joten ne voidaan ajaa rinnakkain.

### T5: Paneeli, merkit, alapalkin nappi ja asetussivun osio (XAML)
- Status: done 95e2a76
- Model: opus
- Requirements: REQ-17 (XAML-osuus), REQ-20, REQ-21 (Detach-osuus)
- Constraints/edge: CON-7, CON-8, EDGE-1, EDGE-10
- Files: src/Cleaner.UI/Views/MainView.xaml, src/Cleaner.UI/Views/MainView.xaml.cs
- Verify: `dotnet build PcCleaner.sln -warnaserror` → "0 Warning(s)", "0 Error(s)"
  Manual (human): käynnistä `src\Cleaner.UI\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\PcCleaner.exe`
  (UAC) ja tarkista: alapalkin robotti-nappi avaa 400 px paneelin oikealle, moduulisivu kapenee; ilman avainta
  InfoBar näkyy ja napit ovat harmaana; Auto-tila-kytkin pysyy päällä uudelleenkäynnistyksessä; lokipaneeli piirtyy
  paneelin päälle; paletin vaihto säilyttää viestit.
- Task:
  Toteuta tekoälyavustajan käyttöliittymä PC Cleanerin WinUI 3 -näkymään `src/Cleaner.UI/Views/MainView.xaml` ja
  `MainView.xaml.cs`. ViewModel-kerros on valmis: lue `src/Cleaner.UI/ViewModels/AiAssistantViewModel.cs`,
  `AiMessageViewModel.cs`, `ItemViewModel.cs`, `CategoryViewModel.cs`, `MainViewModel.cs` (`Ai`, `AutoCleanCommand`)
  ja `Services/UiText.cs` (`Ai*`-ominaisuudet) ennen kirjoittamista; sido täsmälleen niiden jäseniin. Lue koko
  `MainView.xaml` (lokipaneeli `LogSplitView`, `CategoryCardTemplate`, alapalkki, asetussivu) ja `MainView.xaml.cs`
  (`Detach`, `ToggleLog`, `Nav_*`) ja noudata samaa tyyliä: `{ThemeResource App*Brush}`-värit, `{ThemeResource App*FontFamily}`-fontit,
  `x:Bind ... Mode=OneWay` tekstit `ViewModel.T.*`-ominaisuuksista, ei kovakoodattuja värejä tai fontteja.
  Katso `docs/ARCHITECTURE.md` luku 7 ja 7.1.

  Sitovat ViewModel-jäsenet: `ViewModel.Ai` (`AiAssistantViewModel`): `Messages`, `IsAvailable`, `UnavailableText`,
  `ModelName`, `KeyHint`, `IsThinking`, `AutoMode`, `Question`, `HasFlagged`, `ReviewCommand`, `HealthCheckCommand`,
  `AskCommand`, `DeselectFlaggedCommand`, `ResetCommand`, `CancelCommand`; `ViewModel.AutoCleanCommand`;
  `AiMessageViewModel`: `Text`, `IsUser`, `IsAssistant`, `IsError`, `IsInfo`; `ItemViewModel` ja `CategoryViewModel`:
  `VerdictText`, `VerdictReason`, `HasVerdict`, `IsSafeVerdict`, `IsCautionVerdict`, `IsKeepVerdict`;
  `ViewModel.T`: `AiTitle`, `AiAutoMode`, `AiAutoModeHint`, `AiReview`, `AiHealth`, `AiAutoClean`, `AiDeselectFlagged`,
  `AiReset`, `AiAskPlaceholder`, `AiSend`, `AiThinking`, `AiSettingsHeader`, `AiModelLabel`, `AiPrivacyNote`, `Close`, `Cancel`.

  Rakenne:
  - Kääri `NavigationView` (nyt `LogSplitView.Content`) uuteen `SplitView x:Name="AiSplitView"` -elementtiin:
    `PanePlacement="Right"`, `DisplayMode="Inline"`, `OpenPaneLength="400"`, `IsPaneOpen="False"`,
    `PaneBackground="{ThemeResource AppContentBackgroundBrush}"`. Paneelin sisältö `Grid Padding="16"` vasemmalla
    1 px `AppDividerBrush`-reunalla, rivit: otsikko (`T.AiTitle`, kuten lokipaneelin otsikko) + sulkunappi
    (`Click="CloseAi_Click"`); `InfoBar` (`IsOpen="{x:Bind Not(ViewModel.Ai.IsAvailable)}"`, lisää apumetodi
    `private bool Not(bool v)`, `Severity="Warning"`, `IsClosable="False"`, `Message="{x:Bind ViewModel.Ai.UnavailableText, Mode=OneWay}"`);
    `ToggleSwitch` (`Header="{x:Bind ViewModel.T.AiAutoMode, Mode=OneWay}"`, `IsOn="{x:Bind ViewModel.Ai.AutoMode, Mode=TwoWay}"`,
    `OnContent`/`OffContent` tyhjät) ja sen alla `T.AiAutoModeHint` 12 px `AppTextSecondaryBrush`; viestilista
    `ListView x:Name="AiMessageList"` (`ItemsSource="{x:Bind ViewModel.Ai.Messages}"`, `SelectionMode="None"`,
    `IsItemClickEnabled="False"`, rivi `*`), jonka `ItemTemplate` (`x:DataType="vm:AiMessageViewModel"`) on `Border`
    (`CornerRadius="8"`, `Padding="10,8"`, `Margin="0,0,0,8"`) ja sen sisällä `TextBlock Text="{x:Bind Text, Mode=OneWay}"`
    rivityksellä, `IsTextSelectionEnabled="True"`, 13 px `AppBodyFontFamily`. Roolit neljänä Border-elementtinä samassa
    Gridissä, joista yksi näkyy (`Visibility="{x:Bind IsUser}"` jne., bool → Visibility toimii x:Bindissä): käyttäjä
    `AppCardHoverBrush` (avaimet `App*Brush` ja `Risk*Brush` määritellään `Theming/Appearance.cs`-tiedoston
    `AddAppColors`-metodissa; `AppAccentTint`-avainta ei ole, älä keksi uusia avaimia)
    ja `Margin="24,0,0,8"`, avustaja `AppCardBackgroundBrush` ja `Margin="0,0,24,8"`, virhe `RiskHighTintBrush` ja
    teksti `RiskHighTextBrush`, tiedote ilman taustaa 12 px `AppTextSecondaryBrush`. `ItemContainerStyle` kuten lokilistassa
    (MinHeight 0, Padding 0). Ajattelurivi: `StackPanel Orientation="Horizontal"` (`Visibility="{x:Bind ViewModel.Ai.IsThinking, Mode=OneWay}"`)
    jossa `ProgressRing IsActive="True" Width="16" Height="16"`, `T.AiThinking` ja `Button Content="{x:Bind ViewModel.T.Cancel}" Command="{x:Bind ViewModel.Ai.CancelCommand}"`.
    Nappirivi `WrapPanel`-tyyppisesti (WinUI:ssa ei ole WrapPanelia ilman Toolkitia: käytä kahta `StackPanel Orientation="Horizontal" Spacing="8"`
    -riviä): `T.AiReview` → `Ai.ReviewCommand`; `T.AiHealth` → `Ai.HealthCheckCommand`; `T.AiAutoClean` →
    `ViewModel.AutoCleanCommand`, `Style="{StaticResource AccentButtonStyle}"`, `Visibility="{x:Bind ViewModel.Ai.AutoMode, Mode=OneWay}"`;
    `T.AiDeselectFlagged` → `Ai.DeselectFlaggedCommand`, `Visibility="{x:Bind ViewModel.Ai.HasFlagged, Mode=OneWay}"`;
    `T.AiReset` → `Ai.ResetCommand`. Alimpana `Grid` jossa `TextBox x:Name="AiQuestionBox"`
    (`Text="{x:Bind ViewModel.Ai.Question, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"`,
    `PlaceholderText="{x:Bind ViewModel.T.AiAskPlaceholder, Mode=OneWay}"`, `KeyDown="AiQuestionBox_KeyDown"`,
    `IsEnabled="{x:Bind ViewModel.Ai.IsAvailable}"`) ja `Button` (`FontIcon &#xE724;`, `Command="{x:Bind ViewModel.Ai.AskCommand}"`,
    ToolTip `T.AiSend`).
  - Alapalkki: uusi `Button` lokinapin vasemmalle puolelle (lisää sarake; siirrä muiden `Grid.Column`-indeksit),
    `FontIcon Glyph="&#xE99A;" FontSize="16"`, `Click="AiButton_Click"`, ToolTip `T.AiTitle`, sama koko kuin lokinapilla.
  - `CategoryCardTemplate`: otsikkorivin `StackPanel`-elementtiin riskitekstien jälkeen kolme `TextBlock`-merkkiä
    (`Text="{x:Bind VerdictText, Mode=OneWay}"`, `Visibility="{x:Bind IsSafeVerdict, Mode=OneWay}"` / Caution / Keep,
    12 px, `Foreground` `RiskLowTextBrush` / `RiskMediumTextBrush` / `RiskHighTextBrush`,
    `ToolTipService.ToolTip="{x:Bind VerdictReason, Mode=OneWay}"`). Kohderiville uusi sarake koon vasemmalle
    puolelle samoilla kolmella merkillä (`VerticalAlignment="Center"`).
  - Asetussivu: kieliosion jälkeen otsikko `T.AiSettingsHeader` (sama tyyli kuin `AppearanceHeader`), `ToggleSwitch`
    kuten paneelissa, `TextBlock` `"{x:Bind ViewModel.T.AiModelLabel, Mode=OneWay}"` + `": "` + `ModelName`
    (kaksi TextBlockia vaakarivillä tai `Run`-elementit), `TextBlock Text="{x:Bind ViewModel.Ai.KeyHint, Mode=OneWay}"`
    ja `T.AiPrivacyNote`, 12 px `AppTextSecondaryBrush`, rivitys. Kytkin pois käytöstä kiireisenä (`IsEnabled="{x:Bind ViewModel.IsIdle, Mode=OneWay}"`).
  - Code-behind: `AiButton_Click` → `AiSplitView.IsPaneOpen = !AiSplitView.IsPaneOpen`; `CloseAi_Click` sulkee;
    `AiQuestionBox_KeyDown`: `VirtualKey.Enter` → `if (ViewModel.Ai.AskCommand.CanExecute(null)) ViewModel.Ai.AskCommand.Execute(null); e.Handled = true;`.
    Automaattinen vieritys: konstruktorissa `ViewModel.Ai.Messages.CollectionChanged += OnAiMessagesChanged`, jossa
    uuden viestin lisäys tilaa viestin `PropertyChanged`-tapahtuman (`Text`-muutos → `AiMessageList.ScrollIntoView(viimeinen)`)
    ja vierittää heti; `Detach` irrottaa `CollectionChanged`-tilauksen ja kaikkien viestien `PropertyChanged`-tilaukset
    (pidä `HashSet<AiMessageViewModel>` tilatuista) ja nollaa `AiMessageList.ItemsSource = null` samaan kohtaan kuin
    muut nollaukset (`StopTracking` ensin, kuten kommentti selittää).
  - Kommentoi XAML suomeksi samaan tyyliin kuin muu tiedosto (REQ-numerot tästä suunnitelmasta).

### T6: Dokumentit
- Status: done 0489f98
- Model: opus
- Requirements: REQ-22
- Files: docs/ARCHITECTURE.md, README.md, README.fi.md, HANDOFF.md
- Verify: `grep -c "Tekoälyavustaja" docs/ARCHITECTURE.md` → vähintään 2; `grep -c "Cleaner.Ai" README.md README.fi.md HANDOFF.md docs/ARCHITECTURE.md` → jokaisessa vähintään 1;
  `grep -c "aiAutoMode" README.md docs/ARCHITECTURE.md` → jokaisessa vähintään 1
- Task:
  Päivitä PC Cleanerin dokumentit kuvaamaan tekoälyavustaja. Lue ensin `docs/plans/2026-10-20-ai-assistant.md`
  kokonaan (Background & scope, Requirements, Assumptions) ja koodin nykytila: `src/Cleaner.Ai/*.cs`
  (erityisesti `ScanDigest.cs`, `PathSanitizer.cs`, `AiPrompts.cs`, `AnthropicBackend.cs`),
  `src/Cleaner.UI/ViewModels/AiAssistantViewModel.cs` ja `MainViewModel.cs` (`AutoCleanAsync`). Kuvaa toteutus,
  älä suunnitelmaa: jos koodi poikkeaa suunnitelmasta, koodi voittaa. Tyyli: kuten `docs/ARCHITECTURE.md` luku 7.1
  (tiivis, konkreettiset nimet ja luvut, ei markkinointia, ei ajatusviivoja).

  - `docs/ARCHITECTURE.md`: versio 0.5 · 20.10.2026; luvun 2 kaavioon ja riippuvuussääntöön `Cleaner.Ai`
    (viittaa vain Coreen ja NuGet-pakettiin `Anthropic` 12.58.0; UI viittaa siihen; CLI ei); uusi luku 7.2
    "Tekoälyavustaja": projekti ja luokat, API-avain ympäristömuuttujasta `ANTHROPIC_API_KEY` (ei koskaan asetuksiin
    tai lokiin), malli `settings.json`-kentästä `aiModel` (oletus `claude-opus-5-5`), mitä lähetetään
    (`ScanDigest`: otsikkorivit, kohderivit, rajat 40/20/400, `PathSanitizer`-korvaukset `%USERPROFILE%` ja
    `%USERNAME%`; `HealthDigest`: järjestelmätiedot ja moduulien yhteenvedot) ja mitä ei (loki, asetukset, avain,
    rekisteriarvojen sisältö), arviot safe/caution/keep ja niiden merkit kortissa, kategoriatason periytyminen,
    "Poista varoitetut valinnasta", Auto-tila (`aiAutoMode`, kytkin paneelissa ja asetussivulla) ja Automaattinen
    siivous (vaiheet, yksi vahvistusdialogi, High ja opt-in-moduulit aina ulkona), virheiden näyttö, keskustelun
    historia (20 viestiä, ei tallenneta levylle), paneelin rakenne (SplitView Inline 400 px). Luvun 8 testaukseen
    rivi Ai-testeistä. Luvun 9 konventioihin: "Ulkoiset NuGet-paketit vain `Cleaner.Ai`- ja `Cleaner.UI`-projekteissa".
  - `README.md`: rakennepuuhun `Cleaner.Ai/` (yksi rivi, englanniksi); uusi osio "## AI assistant" osion "Appearance"
    jälkeen: mitä avustaja tekee, avaimen asetus (`setx ANTHROPIC_API_KEY sk-ant-...` tai Windowsin ympäristömuuttujat;
    sovellus on käynnistettävä uudelleen), mitä tietoja lähetetään Anthropicille ja mitä ei, Auto-tila ja sen rajat,
    `settings.json`-kentät `aiModel` ja `aiAutoMode`, kustannushuomautus (API-käyttö laskutetaan Anthropicin
    hinnaston mukaan). `README.fi.md`: sama suomeksi vastaavaan kohtaan (lue tiedoston rakenne ensin).
  - `HANDOFF.md`: taulukkoon rivi `src/Cleaner.Ai` (sisältö, tila) ja `src/Cleaner.UI`-riville maininta
    avustajapaneelista; päätöslistaan päätös 27: tekoälyavustaja (tietosuoja: polut siistittyinä; Auto-tilan rajat:
    High ja opt-in-moduulit eivät koskaan mukana, aina vähintään yksi vahvistusdialogi; NuGet `Anthropic` vain
    Ai-projektissa, päätös 4 pysyy muuten voimassa; refusal-fallback ja prompt caching jätetty pois).
    Päivitä päiväysrivi.

## Final verification

- AC-1 [auto]: `dotnet build PcCleaner.sln -warnaserror` → "0 Warning(s)", "0 Error(s)".
- AC-2 [auto]: `dotnet test PcCleaner.sln --no-build` → "Passed!", 0 failed (254 vanhaa + uudet Ai-testit).
- AC-3 [auto]: `grep -rlE "\.(CompleteJsonAsync|StreamTextAsync)\(" src/ --include=*.cs` → täsmälleen
  `src/Cleaner.Ai/AiAssistant.cs` (`AnthropicBackend` määrittelee metodit, ei kutsu niitä; testiprojekti ei kuulu
  hakuun); `grep -rn "ANTHROPIC_API_KEY" src/ --include=*.cs` osuu vain `AnthropicBackend.cs`-tiedostoon
  (`IsConfigured`) ja `Loc.T`-teksteihin (ei lokikutsuihin).
- AC-4 [manual]: avain asetettuna: Tiedostosiivous → Skannaa → paneeli → Tarkista tulokset: avustajan viesti
  sisältää yhteenvedon ja varo/säilytä-rivit, kortteihin tulee merkit ja ToolTip näyttää perustelun; "Poista
  varoitetut valinnasta" pudottaa valintaa ja ilmoittaa määrän; uusi skannaus tyhjentää merkit.
- AC-5 [manual]: Koneen kunto: vastaus ilmestyy pala kerrallaan, mainitsee asemat ja skannaamattomat moduulit;
  jatkokysymys "Mikä kannattaa tehdä ensin?" saa vastauksen samassa keskustelussa; Peruuta kesken jättää osan ja "(peruutettu)".
- AC-6 [manual]: Auto-tila päälle → Automaattinen siivous: skannaus, arviot paneeliin, yksi dialogi, jossa moduulien
  yhteenvedot ja kuivan ajon määrät; Peruuta dialogissa ei poista mitään (loki ei näytä poistoja). Vahvistus vasta,
  kun dialogin lista on luettu läpi.
- AC-7 [manual]: ilman avainta (poista ympäristömuuttuja väliaikaisesti): paneelin InfoBar näkyy, napit harmaana,
  ei virhedialogia, Auto-tilan kytkin tallentuu.
- AC-8 [manual]: `dotnet publish src\Cleaner.UI -c Release -o publish\PcCleaner` (sovellus suljettuna) onnistuu
  ja `publish\PcCleaner\Anthropic.dll` on olemassa; julkaistu exe avaa paneelin.

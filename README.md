<div align="center">

<img src="assets/banner-readme-plugin.png" alt="KometaThemes for Jellyfin" width="100%" />

<h1>KometaThemes for Jellyfin</h1>

<p><em>Anime openings and endings, downloaded automatically into your Jellyfin library.</em></p>

<p>
  <a href="https://github.com/iCosiSenpai/KometaThemes/releases"><img src="https://img.shields.io/github/v/release/iCosiSenpai/KometaThemes?style=for-the-badge&color=00a4dc&labelColor=1a1a2e" alt="Latest release" /></a>
  <a href="https://jellyfin.org/"><img src="https://img.shields.io/badge/Jellyfin-10.11.x-7c5cff?style=for-the-badge&labelColor=1a1a2e" alt="Jellyfin 10.11.x" /></a>
  <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-9.0-512bd4?style=for-the-badge&labelColor=1a1a2e" alt=".NET 9" /></a>
</p>

<p>
  <a href="https://github.com/iCosiSenpai/KometaThemes/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/iCosiSenpai/KometaThemes/ci.yml?branch=main&style=flat-square&label=build%20%26%20tests&labelColor=1a1a2e" alt="Build and tests" /></a>
  <img src="https://img.shields.io/badge/tests-213%20unit%20%2B%2015%20browser-2ea043?style=flat-square&labelColor=1a1a2e" alt="213 unit and 15 browser tests" />
  <a href="LICENSE"><img src="https://img.shields.io/github/license/iCosiSenpai/KometaThemes?style=flat-square&labelColor=1a1a2e" alt="GPL v3" /></a>
</p>

</div>

<!-- ════════════════════════ LANGUAGE SWITCH ════════════════════════ -->

<div align="center">

### 🌍 Choose your language · Scegli la lingua

**Click a language to switch to it.** Opening one closes the other, and you never leave this page.

<sub>Uses the native HTML exclusive-accordion behaviour — no JavaScript, since GitHub strips it.<br />
On browsers older than Chrome&nbsp;120 / Safari&nbsp;17.2 / Firefox&nbsp;130 both simply stay expandable.</sub>

</div>

<br />

<!-- ══════════════════════════════ ENGLISH ══════════════════════════════ -->

<a id="english"></a>

<details name="readme-language" open>
<summary>
  <picture><img src="https://img.shields.io/badge/%F0%9F%87%AC%F0%9F%87%A7%20English-Read%20the%20docs-00a4dc?style=for-the-badge&labelColor=1a1a2e" alt="English documentation" /></picture>
  <kbd>&nbsp;Read in English&nbsp;</kbd>
</summary>

<br />

KometaThemes finds and downloads anime openings and endings from [AnimeThemes](https://animethemes.moe/) for your Jellyfin library, and fills the gaps from YouTube when AnimeThemes does not have a theme.

It combines multi-provider matching, season-aware selection, a guided Theme Finder, per-item controls, resilient downloads, and a bilingual administration interface.

### ✨ At a glance

|  | Feature |
|:--:|---|
| 🎵 | Download **OP/ED audio themes and video backdrops** for series and movies |
| 🔍 | Match titles through **AniDB, AniList, MyAnimeList, Kitsu and AniSearch**, with a fuzzy title fallback |
| 📺 | Handle **multi-season anime** — pick the single best theme, every theme, or every theme per season |
| 🎯 | **Theme Finder**: search manually, preview media, filter results, and create persistent item bindings |
| ▶️ | **Add from a YouTube link** what AnimeThemes is missing — asks OP or ED, and audio, video or both |
| 📋 | Track **unresolved and excluded** items without repeatedly querying known misses |
| 🔧 | **Repair Jellyfin 10.11.x theme links** and build a global M3U playlist |
| ♿ | Accessible, responsive **English/Italian** frontend with no separate web build step |

### 📦 Requirements

| Requirement | Details |
|---|---|
| **Jellyfin** | `10.11.x` · catalog ABI `10.11.8.0` |
| **Runtime** | .NET 9, provided by the supported Jellyfin release |
| **File Transformation** | Optional — only for the ♪ shortcut on Jellyfin item detail pages |
| **yt-dlp** | Optional — only for the YouTube import feature |
| **Network** | HTTPS access to AnimeThemes and the metadata providers you enable |
| **Permissions** | An administrator account is required for configuration and plugin actions |

### 🚀 Installation

**Recommended: the Jellyfin Plugin Catalog.**

1. Open **Dashboard → Plugins → Repositories**.
2. Add this repository URL:

   ```text
   https://raw.githubusercontent.com/iCosiSenpai/iCosiSenpai-Plugins/main/manifest.json
   ```

3. Open **Catalog**, select **KometaThemes**, and install the latest version.
4. Restart Jellyfin when prompted, then hard-refresh the web client (`Ctrl+Shift+R`).
5. *Optional:* install **File Transformation** from the Jellyfin catalog to enable the ♪ item-page shortcut.

Updates arrive through the same catalog. You never need to copy DLL files into the container by hand.

### ⚙️ First setup

1. Open **Dashboard → Plugins → KometaThemes**.
2. In **General**, set the UI language and check the library name pattern. The default `Anime` limits automatic work and UI injection to matching libraries.
3. In **Themes & Download**, choose audio/video fetch modes separately for series and movies.
4. In **Providers & Matching**, order the metadata providers and review the title fallback and cache settings.
5. Use **Sync now** for an incremental run, or **Force sync** to re-evaluate everything.
6. In your Jellyfin user profile, confirm **Settings → Display → Play theme songs** is enabled.

> **💡 Tip** — If themes download but never play, that last checkbox is the usual cause. Jellyfin upgrades sometimes reset it.

### 🎬 Main workflows

<details>
<summary><strong>Automatic and on-demand sync</strong></summary>

<br />

KometaThemes can run on a schedule, shortly after a new item enters a matching library, or manually.

- **Incremental sync** only processes missing or unsatisfied items.
- **Force sync** removes outdated themes and performs a full re-evaluation.
- **Dry-run mode** resolves and logs without writing any media.

</details>

<details>
<summary><strong>Theme Finder — the guided search</strong></summary>

<br />

1. **Search** using the Jellyfin title and year, both editable.
2. **Choose an anime** from strong and broad matches, with pointer or keyboard navigation.
3. **Review themes** by season: filter Audio/Video and OP/ED, optionally require creditless media, preview a source, and select individually or in bulk.
4. **Download** the chosen themes, or save only the manual binding for future automatic syncs.

The ♪ shortcut is admin-only and appears on eligible series/movie pages whose library matches `Library Pattern`. Without File Transformation, the Theme Finder is still reachable from the plugin dashboard.

</details>

<details>
<summary><strong>▶️ Adding a theme from a YouTube link</strong></summary>

<br />

For the openings and endings AnimeThemes simply does not have.

1. Open the **Theme Finder** for an item.
2. Paste a YouTube link into **Add from a YouTube link**.
3. You are asked whether it is an **opening or an ending**, its **number** (OP2, ED1…), and whether you want **audio only, video only, or both**.
4. The files are written with the same naming and folder layout as a normal sync, and are marked as imported so a later sync never deletes them as orphans.

**Setup.** The feature is **off by default**. Enable it in **Settings → Themes & Download → YouTube import**, which also reports whether `yt-dlp` was found and lets you set an explicit path.

`yt-dlp` must be installed in the Jellyfin environment. An external program is used deliberately: the plugin ships as a single DLL with no side-by-side dependencies, and YouTube's player changes far more often than this plugin releases — `yt-dlp` is maintained against those changes continuously.

Accepted links: `watch`, `youtu.be`, Shorts, YouTube Music and embed URLs. Playlists are ignored; only the linked video is imported.

> **⚠️ Before you enable this** — check the terms of use and the copyright rules that apply where you are.

</details>

<details>
<summary><strong>Item management</strong></summary>

<br />

The item page shows downloaded themes, disk and registration status, missing files, manual bindings, and library presets. From there you can:

- sync one item;
- delete one theme, or all themes for the item;
- open the Theme Finder;
- repair detached theme links after a Jellyfin library scan;
- launch background presets across matching libraries.

</details>

<details>
<summary><strong>Unresolved, bindings and exclusions</strong></summary>

<br />

- **Unresolved** records matching and download failures, with attempt counts and the last error.
- **Bindings** stores manual item-to-anime associations and gives them priority over automatic resolution.
- **Excluded** holds items deliberately blacklisted from future matching; each can be restored later.

</details>

### 🎛️ Configuration reference

The section names below are the tab labels you actually see in the plugin.

| Tab | Purpose |
|---|---|
| **General** | Interface language, library filter, schedule, auto-sync, cleanup, notifications |
| **Themes & Download** | Series/movie media modes, volume, OP/ED and credit filters, season behaviour, download parallelism, dry-run, YouTube import |
| **Providers & Matching** | Provider order, fuzzy title threshold, API rate, positive/negative cache TTLs, cache controls |
| **Excluded** | Blacklisted items and restore controls, global playlist configuration, M3U export |
| **Bindings** | Persistent manual matches, unlock/recalculate, optional removal of downloaded files |
| **Unresolved** | Retry, manual resolution, blacklist, dismiss, clear |

**Fetch modes**

| Stored value | Shown in the UI as | Behaviour |
|---|---|---|
| `None` | *None* | Do not download this media type |
| `Single` | *Best theme only* | Download the best eligible theme |
| `All` | *All themes* | Download all eligible themes |
| `AllPerSeason` | *All, per season* | Keep eligible themes grouped and named per detected season |

**Typical output**

```text
Series folder/
├── theme-music/
│   ├── OP1 - Guren no Yumiya__50.mp3
│   └── ED1 - Utsukushiki Zankoku na Sekai__50.mp3
└── backdrops/
    └── OP1 - Guren no Yumiya__50.webm
```

The volume suffix is generated for Jellyfin playback. Media selection stays controlled by the per-type configuration. Imported themes keep the container the extractor returned, so a `.mp4` alongside `.webm` is expected and fine.

### 🔒 Reliability and security

- API calls use the current Jellyfin `MediaBrowser` token and same-origin credentials.
- Remote media is accepted only from same-origin HTTP(S) or HTTPS AnimeThemes domains; credentials embedded in URLs are rejected.
- YouTube links are reduced to a canonical video ID server-side, so the pasted text never reaches the extractor's command line.
- Theme files are published atomically, so a failed conversion cannot leave a broken file behind.
- Deletion is driven by what the plugin recorded, so it never removes files it did not create — including your own artwork in `backdrops/`.
- Resolution results are cached in atomic JSON files with separate positive and negative TTLs, and a damaged file is quarantined rather than silently emptied.
- Requests use rate limiting, retries and circuit-breaker behaviour; `ffmpeg` concurrency is capped across the whole plugin.
- Stale async responses are discarded when navigation changes context, and every mutating action guards against double submission.

### ♿ Accessibility and frontend

The frontend is three small HTML shells plus embedded JavaScript/CSS. Shared modules provide sequential versioned asset loading with visible failure handling, keyboard tab navigation, focus-trapped dialogs with Escape and focus restoration, live status regions and `aria-busy` states, listbox navigation in the Theme Finder, and responsive dark/light design tokens.

The browser suite loads the real embedded shells against a local Jellyfin API fixture: Playwright covers the critical flows, and axe-core checks WCAG A/AA serious and critical violations.

### 🔌 REST API

<details>
<summary><strong>Endpoint reference</strong> — all require an elevated Jellyfin token unless noted</summary>

<br />

All paths are relative to `/Plugins/KometaThemes`. Everything requires an elevated Jellyfin token except the two anonymous web assets at the bottom.

| Endpoint | Method | Purpose |
|---|:---:|---|
| **Health and sync** | | |
| `/Health` | GET | Version, health, metrics, current sync summary |
| `/Sync/status` | GET | Live sync progress |
| `/Sync/sync` | POST | Start an incremental sync |
| `/Sync/force` | POST | Start a server-side forced sync |
| `/Sync/run` | POST | Start a library preset |
| **Per item** | | |
| `/Items/{id}/info` | GET | Item and theme-registration context |
| `/Items/{id}/eligible` | GET | Whether the item is in a matching library |
| `/Items/{id}/binding` | GET | The item's manual binding, if any |
| `/Items/{id}/themes` | GET · DELETE | List themes, or delete them |
| `/Items/{id}/sync` | POST | Sync one eligible item |
| `/Items/{id}/preview` | POST | Resolve without downloading anything |
| `/Items/{id}/repair` | POST | Repair Jellyfin theme links |
| `/Items/{id}/download` | POST | Download selected AnimeThemes media |
| `/Items/{id}/youtube` | POST | Import a theme from a YouTube link |
| **Search and YouTube** | | |
| `/Search` | GET | Search AnimeThemes candidates |
| `/Anime/{id}/themes` | GET | Retrieve themes and season groups |
| `/YouTube/status` | GET | Whether YouTube import is enabled and available |
| **Bindings** | | |
| `/Bindings` | GET | List every manual binding |
| `/Bindings/{id}` | POST · DELETE | Save or remove a binding |
| `/Bindings/{id}/unlock` | POST | Drop the binding but keep the files |
| **Unresolved** | | |
| `/Failed/items` | GET | List unresolved items |
| `/Failed/count` | GET | Unresolved count |
| `/Failed/items/{id}` | DELETE | Dismiss one entry |
| `/Failed/items/{id}/resolve-manually` | POST | Mark as handled by hand |
| `/Failed/clear` | POST | Clear the list |
| **Excluded** | | |
| `/Skipped/items` | GET | List excluded items |
| `/Skipped/count` | GET | Excluded count |
| `/Skipped/{id}` | POST | Add to the blacklist |
| `/Skipped/{id}/remove` | POST | Restore one item |
| `/Skipped/clear` | POST | Clear the blacklist |
| **Cache, logs, playlist** | | |
| `/Cache/stats` | GET | Resolution-cache statistics |
| `/Cache/clear` | POST | Clear the resolution cache |
| `/Logs?lines=200` | GET | Read current plugin log entries |
| `/Playlist/refresh` | POST | Rebuild the global playlist |
| `/Playlist/export` | GET | Download the M3U playlist |
| **Web assets** | | |
| `/ItemButton.js` | GET | Item-button injector script — anonymous |
| `/InjectButton` | POST | File Transformation hook — anonymous |

</details>

### 🩺 Troubleshooting

<details>
<summary><strong>Themes download but do not play</strong></summary>

<br />

Open the item's KometaThemes page and check its registration banner. Run a Jellyfin library scan, then use **Repair links**. Also verify **Settings → Display → Play theme songs** in the affected user's profile — Jellyfin upgrades can reset it.

</details>

<details>
<summary><strong>The ♪ shortcut does not appear</strong></summary>

<br />

Install and enable File Transformation, restart Jellyfin, and hard-refresh the client. The shortcut is visible only to administrators, only on series/movie pages, and only when the owning library matches `Library Pattern`.

</details>

<details>
<summary><strong>YouTube import says yt-dlp was not found</strong></summary>

<br />

Install `yt-dlp` inside the Jellyfin environment. In a Docker setup that means the Jellyfin container, not the host. The settings page reports where it looked; if your install lives somewhere unusual, set the full path in **yt-dlp path**.

The usual locations are searched automatically, along with `PATH`.

</details>

<details>
<summary><strong>An item never resolves</strong></summary>

<br />

Open **Unresolved** to retry, or search and bind it manually. If the title does not exist on AnimeThemes, blacklist it to stop repeated searches — or add the theme from a YouTube link instead. Review provider IDs, year, title threshold and the live activity log before lowering matching safeguards.

</details>

<details>
<summary><strong>The whole Jellyfin web UI fails after a plugin update</strong></summary>

<br />

Check the Jellyfin log for web-injection plugins throwing `ObjectDisposedException`. This can happen when another injector retains a disposed service provider during a plugin reload. Perform a full Jellyfin restart and test injectors one at a time. Do not replace the KometaThemes DLL by hand.

</details>

### 🏗️ Architecture

```text
Jellyfin library
      │
      ▼
LibrarySelection ──► CompositeResolver ──► AnimeThemes API
  pattern/type          │ provider IDs        │ themes + seasons
  eligibility           └ title fallback      ▼
      │                                   Download engine
      │                                 ffmpeg + resilience
      ▼                                         │
Item sync / scheduler ──────────────────────────┤
      │                                         │
      │            YouTube import ──────────────┤
      │              yt-dlp                     │
      ▼                                         ▼
      ├── JsonResolutionCache            Theme files + repair
      ├── FailedItemsStore                      │
      ├── manual bindings                       ▼
      └── excluded items                 Global M3U playlist
```

```text
Jellyfin page shells
  └── kometa-loader.js
      ├── kometa-core.js       API, i18n, dialogs, sync, preview, lifecycle
      ├── kometa-a11y.js       tabs, listbox, busy state, announcements
      ├── config.js
      ├── search.js
      └── item.js
```

### 🧪 Development and validation

Requirements: .NET 9 SDK, Node.js 20, npm.

```bash
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build

npm ci
npx playwright install chromium
npm run test:browser
```

Focused commands:

```bash
npm run test:e2e     # Playwright end-to-end only
npm run test:a11y    # axe accessibility audit only
```

CI runs the .NET build and tests, the Playwright suite, the axe audit, then builds a DLL-only ZIP and prints its MD5. Releases and catalog updates stay explicit publication steps, and deploying to a Jellyfin server is intentionally left to the administrator through the Plugin Catalog.

### 📌 Release policy

KometaThemes uses `Major.Minor.Build.Revision`. Feature work increments Minor or Build; focused fixes increment Revision. Every published version includes synchronized assembly and frontend versions, a Release build with automated tests, a DLL-only `KometaThemes.zip` with an MD5 checksum, a GitHub Release with features/fixes/breaking changes, and a new top entry in the Jellyfin catalog manifest.

### 🙏 Credits and license

- Theme metadata and media: [AnimeThemes](https://animethemes.moe/)
- Media server: [Jellyfin](https://jellyfin.org/)
- Author and maintainer: [iCosiSenpai](https://github.com/iCosiSenpai)
- License: [GNU GPL v3](LICENSE)

</details>

<br />

<!-- ══════════════════════════════ ITALIANO ══════════════════════════════ -->

<a id="italiano"></a>

<details name="readme-language">
<summary>
  <picture><img src="https://img.shields.io/badge/%F0%9F%87%AE%F0%9F%87%B9%20Italiano-Leggi%20la%20documentazione-7c5cff?style=for-the-badge&labelColor=1a1a2e" alt="Documentazione in italiano" /></picture>
  <kbd>&nbsp;Leggi in italiano&nbsp;</kbd>
</summary>

<br />

KometaThemes trova e scarica automaticamente le opening e le ending degli anime da [AnimeThemes](https://animethemes.moe/) per la tua libreria Jellyfin, e riempie i buchi da YouTube quando AnimeThemes non ha un tema.

Combina il riconoscimento su più provider, la selezione consapevole delle stagioni, un Theme Finder guidato, controlli per singolo elemento, download resilienti e un'interfaccia di amministrazione bilingue.

### ✨ In breve

|  | Funzione |
|:--:|---|
| 🎵 | Scarica **temi audio OP/ED e sfondi video** per serie e film |
| 🔍 | Riconosce i titoli tramite **AniDB, AniList, MyAnimeList, Kitsu e AniSearch**, con ricerca per titolo come riserva |
| 📺 | Gestisce gli **anime multi-stagione** — un solo tema migliore, tutti i temi, o tutti i temi per stagione |
| 🎯 | **Theme Finder**: cerca a mano, ascolta l'anteprima, filtra i risultati e crea associazioni permanenti |
| ▶️ | **Aggiungi da un link YouTube** quello che manca su AnimeThemes — chiede se è OP o ED, e se vuoi audio, video o entrambi |
| 📋 | Tiene traccia degli elementi **non risolti ed esclusi** senza ripetere ricerche già fallite |
| 🔧 | **Ripara i collegamenti dei temi** di Jellyfin 10.11.x e crea una playlist M3U globale |
| ♿ | Interfaccia accessibile e responsive in **italiano e inglese**, senza passaggi di build separati |

### 📦 Requisiti

| Requisito | Dettagli |
|---|---|
| **Jellyfin** | `10.11.x` · ABI del catalogo `10.11.8.0` |
| **Runtime** | .NET 9, fornito dalla versione di Jellyfin supportata |
| **File Transformation** | Opzionale — serve solo per il pulsante ♪ nelle pagine di dettaglio |
| **yt-dlp** | Opzionale — serve solo per l'import da YouTube |
| **Rete** | Accesso HTTPS ad AnimeThemes e ai provider di metadati che attivi |
| **Permessi** | Serve un account amministratore per la configurazione e le azioni del plugin |

### 🚀 Installazione

**Consigliata: il catalogo plugin di Jellyfin.**

1. Apri **Dashboard → Plugin → Repository**.
2. Aggiungi questo indirizzo:

   ```text
   https://raw.githubusercontent.com/iCosiSenpai/iCosiSenpai-Plugins/main/manifest.json
   ```

3. Apri **Catalogo**, seleziona **KometaThemes** e installa l'ultima versione.
4. Riavvia Jellyfin quando richiesto, poi ricarica il client con `Ctrl+Shift+R`.
5. *Opzionale:* installa **File Transformation** dal catalogo per abilitare il pulsante ♪.

Gli aggiornamenti arrivano dallo stesso catalogo. Non serve mai copiare a mano file DLL nel container.

### ⚙️ Prima configurazione

1. Apri **Dashboard → Plugin → KometaThemes**.
2. In **Generale**, imposta la lingua e controlla il pattern del nome libreria. Il valore predefinito `Anime` limita il lavoro automatico alle librerie corrispondenti.
3. In **Temi e download**, scegli le modalità audio/video separatamente per serie e film.
4. In **Provider e riconoscimento**, ordina i provider e rivedi la ricerca per titolo e le impostazioni di cache.
5. Usa **Sync ora** per un giro incrementale, o **Force sync** per rivalutare tutto.
6. Nel tuo profilo Jellyfin, verifica che **Impostazioni → Visualizzazione → Riproduci sigle** sia attivo.

> **💡 Suggerimento** — Se i temi vengono scaricati ma non partono, quasi sempre è quest'ultima casella: gli aggiornamenti di Jellyfin a volte la reimpostano.

### 🎬 Come si usa

<details>
<summary><strong>Sync automatico e su richiesta</strong></summary>

<br />

KometaThemes può girare a intervalli regolari, poco dopo che un nuovo elemento entra in una libreria corrispondente, oppure a mano.

- Il **sync incrementale** elabora solo gli elementi mancanti o incompleti.
- Il **force sync** rimuove i temi superati e rivaluta tutto da capo.
- La **modalità dry-run** risolve e registra nel log senza scrivere alcun file.

</details>

<details>
<summary><strong>Theme Finder — la ricerca guidata</strong></summary>

<br />

1. **Cerca** usando titolo e anno di Jellyfin, entrambi modificabili.
2. **Scegli l'anime** tra i risultati affidabili ed estesi, con mouse o tastiera.
3. **Esamina i temi** per stagione: filtra audio/video e OP/ED, richiedi eventualmente solo materiale creditless, ascolta un'anteprima e seleziona singolarmente o in blocco.
4. **Scarica** i temi scelti, oppure salva solo l'associazione per i sync automatici futuri.

Il pulsante ♪ è riservato agli amministratori e compare nelle pagine di serie e film la cui libreria corrisponde a `Library Pattern`. Senza File Transformation, il Theme Finder resta raggiungibile dalla dashboard del plugin.

</details>

<details>
<summary><strong>▶️ Aggiungere un tema da un link YouTube</strong></summary>

<br />

Per le opening e le ending che su AnimeThemes semplicemente non ci sono.

1. Apri il **Theme Finder** per un elemento.
2. Incolla un link YouTube in **Aggiungi da un link YouTube**.
3. Ti viene chiesto se è una **opening o una ending**, il suo **numero** (OP2, ED1…), e se vuoi **solo audio, solo video o entrambi**.
4. I file vengono scritti con la stessa struttura e gli stessi nomi di un sync normale, e vengono marcati come importati, così un sync successivo non li cancella mai come orfani.

**Preparazione.** La funzione è **disattivata per impostazione predefinita**. Attivala in **Impostazioni → Temi e download → Import da YouTube**, dove viene anche indicato se `yt-dlp` è stato trovato e dove puoi specificarne il percorso.

`yt-dlp` deve essere installato nell'ambiente di Jellyfin. La scelta di usare un programma esterno è voluta: il plugin viene distribuito come singola DLL senza dipendenze affiancate, e il player di YouTube cambia molto più spesso di quanto questo plugin rilasci nuove versioni — `yt-dlp` viene mantenuto aggiornato proprio rispetto a quei cambiamenti.

Link accettati: `watch`, `youtu.be`, Shorts, YouTube Music ed embed. Le playlist vengono ignorate: viene importato solo il video collegato.

> **⚠️ Prima di attivarlo** — verifica i termini di utilizzo e le regole sul diritto d'autore applicabili nel tuo paese.

</details>

<details>
<summary><strong>Gestione del singolo elemento</strong></summary>

<br />

La pagina dell'elemento mostra i temi scaricati, lo stato su disco e di registrazione, i file mancanti, le associazioni manuali e i preset di libreria. Da lì puoi:

- sincronizzare un singolo elemento;
- eliminare un tema, o tutti i temi dell'elemento;
- aprire il Theme Finder;
- riparare i collegamenti dei temi dopo una scansione della libreria;
- avviare preset in background su tutte le librerie corrispondenti.

</details>

<details>
<summary><strong>Non risolti, associazioni ed esclusi</strong></summary>

<br />

- **Non risolti** registra i fallimenti di riconoscimento e di download, con numero di tentativi e ultimo errore.
- **Associazioni** conserva i collegamenti manuali elemento-anime e li fa valere prima della risoluzione automatica.
- **Esclusi** contiene gli elementi volutamente messi in blacklist; ognuno può essere ripristinato in seguito.

</details>

### 🎛️ Riferimento della configurazione

I nomi delle sezioni qui sotto sono quelli che vedi davvero nelle schede del plugin.

| Scheda | Scopo |
|---|---|
| **Generale** | Lingua dell'interfaccia, filtro libreria, pianificazione, sync automatico, pulizia, notifiche |
| **Temi & Download** | Modalità per serie e film, volume, filtri OP/ED e credits, comportamento stagioni, parallelismo, dry-run, import da YouTube |
| **Provider & Matching** | Ordine dei provider, soglia di somiglianza, limite di richieste, TTL di cache, controlli della cache |
| **Esclusi** | Elementi in blacklist e ripristino, configurazione della playlist globale, export M3U |
| **Binding** | Corrispondenze manuali permanenti, sblocco/ricalcolo, rimozione opzionale dei file scaricati |
| **Non risolti** | Riprova, risoluzione manuale, blacklist, ignora, svuota |

**Modalità di download**

| Valore salvato | Nell'interfaccia | Comportamento |
|---|---|---|
| `None` | *Niente* | Non scaricare questo tipo di media |
| `Single` | *Solo il migliore* | Scarica il miglior tema disponibile |
| `All` | *Tutti i temi* | Scarica tutti i temi disponibili |
| `AllPerSeason` | *Tutti, per stagione* | Mantiene i temi raggruppati e nominati per stagione rilevata |

**Risultato tipico**

```text
Cartella della serie/
├── theme-music/
│   ├── OP1 - Guren no Yumiya__50.mp3
│   └── ED1 - Utsukushiki Zankoku na Sekai__50.mp3
└── backdrops/
    └── OP1 - Guren no Yumiya__50.webm
```

Il suffisso del volume viene generato per la riproduzione in Jellyfin. La selezione dei media resta governata dalla configurazione per tipo. I temi importati mantengono il contenitore restituito dall'estrattore, quindi trovare un `.mp4` accanto a un `.webm` è normale.

### 🔒 Affidabilità e sicurezza

- Le chiamate API usano il token `MediaBrowser` corrente di Jellyfin e credenziali same-origin.
- I media remoti sono accettati solo da domini same-origin in HTTP(S) o da AnimeThemes in HTTPS; le credenziali dentro gli URL vengono rifiutate.
- I link YouTube vengono ridotti lato server a un identificativo video canonico, così il testo incollato non raggiunge mai la riga di comando dell'estrattore.
- I file dei temi vengono pubblicati in modo atomico: una conversione fallita non può lasciare dietro di sé un file danneggiato.
- L'eliminazione si basa su quello che il plugin ha registrato, quindi non rimuove mai file che non ha creato lui — comprese le tue immagini in `backdrops/`.
- I risultati della risoluzione sono in cache in file JSON atomici con TTL separati per esiti positivi e negativi, e un file danneggiato viene messo da parte invece di essere svuotato in silenzio.
- Le richieste usano limitazione di frequenza, ritentativi e circuit breaker; le esecuzioni di `ffmpeg` sono limitate a livello di plugin.
- Le risposte asincrone obsolete vengono scartate quando il contesto cambia, e ogni azione che modifica qualcosa è protetta dal doppio invio.

### ♿ Accessibilità e frontend

Il frontend è composto da tre piccole pagine HTML più JavaScript e CSS incorporati. I moduli condivisi forniscono caricamento sequenziale e versionato degli asset con gestione visibile degli errori, navigazione a schede da tastiera, finestre di conferma con focus intrappolato, Escape e ripristino del focus, aree di stato live e stati `aria-busy`, navigazione a listbox nel Theme Finder e temi chiari/scuri responsive.

La suite di test carica le vere pagine incorporate contro un finto server Jellyfin locale: Playwright copre i percorsi critici e axe-core verifica le violazioni gravi e critiche WCAG A/AA.

### 🔌 API REST

<details>
<summary><strong>Elenco degli endpoint</strong> — richiedono tutti un token Jellyfin con privilegi, salvo dove indicato</summary>

<br />

Tutti i percorsi sono relativi a `/Plugins/KometaThemes`. Richiedono tutti un token Jellyfin con privilegi, tranne le due risorse web anonime in fondo.

| Endpoint | Metodo | Scopo |
|---|:---:|---|
| **Stato e sync** | | |
| `/Health` | GET | Versione, stato, metriche, riepilogo dell'ultimo sync |
| `/Sync/status` | GET | Avanzamento del sync in corso |
| `/Sync/sync` | POST | Avvia un sync incrementale |
| `/Sync/force` | POST | Avvia un sync forzato lato server |
| `/Sync/run` | POST | Avvia un preset di libreria |
| **Per elemento** | | |
| `/Items/{id}/info` | GET | Contesto dell'elemento e registrazione dei temi |
| `/Items/{id}/eligible` | GET | Se l'elemento è in una libreria corrispondente |
| `/Items/{id}/binding` | GET | L'associazione manuale dell'elemento, se presente |
| `/Items/{id}/themes` | GET · DELETE | Elenca o elimina i temi |
| `/Items/{id}/sync` | POST | Sincronizza un singolo elemento |
| `/Items/{id}/preview` | POST | Risolve senza scaricare nulla |
| `/Items/{id}/repair` | POST | Ripara i collegamenti dei temi |
| `/Items/{id}/download` | POST | Scarica i media selezionati da AnimeThemes |
| `/Items/{id}/youtube` | POST | Importa un tema da un link YouTube |
| **Ricerca e YouTube** | | |
| `/Search` | GET | Cerca candidati su AnimeThemes |
| `/Anime/{id}/themes` | GET | Recupera temi e gruppi di stagione |
| `/YouTube/status` | GET | Se l'import da YouTube è attivo e disponibile |
| **Binding** | | |
| `/Bindings` | GET | Elenca tutte le associazioni manuali |
| `/Bindings/{id}` | POST · DELETE | Salva o rimuove un'associazione |
| `/Bindings/{id}/unlock` | POST | Rimuove l'associazione ma conserva i file |
| **Non risolti** | | |
| `/Failed/items` | GET | Elenca gli elementi non risolti |
| `/Failed/count` | GET | Quantità di non risolti |
| `/Failed/items/{id}` | DELETE | Ignora una voce |
| `/Failed/items/{id}/resolve-manually` | POST | Segna come gestito a mano |
| `/Failed/clear` | POST | Svuota l'elenco |
| **Esclusi** | | |
| `/Skipped/items` | GET | Elenca gli elementi esclusi |
| `/Skipped/count` | GET | Quantità di esclusi |
| `/Skipped/{id}` | POST | Aggiunge alla blacklist |
| `/Skipped/{id}/remove` | POST | Ripristina un elemento |
| `/Skipped/clear` | POST | Svuota la blacklist |
| **Cache, log, playlist** | | |
| `/Cache/stats` | GET | Statistiche della cache di risoluzione |
| `/Cache/clear` | POST | Svuota la cache di risoluzione |
| `/Logs?lines=200` | GET | Legge le voci di log del plugin |
| `/Playlist/refresh` | POST | Rigenera la playlist globale |
| `/Playlist/export` | GET | Scarica la playlist M3U |
| **Risorse web** | | |
| `/ItemButton.js` | GET | Script del pulsante — anonimo |
| `/InjectButton` | POST | Hook di File Transformation — anonimo |

</details>

### 🩺 Risoluzione dei problemi

<details>
<summary><strong>I temi vengono scaricati ma non partono</strong></summary>

<br />

Apri la pagina KometaThemes dell'elemento e guarda il banner di registrazione. Esegui una scansione della libreria Jellyfin, poi usa **Ripara collegamenti**. Verifica anche **Impostazioni → Visualizzazione → Riproduci sigle** nel profilo dell'utente interessato: gli aggiornamenti di Jellyfin possono reimpostarla.

</details>

<details>
<summary><strong>Il pulsante ♪ non compare</strong></summary>

<br />

Installa e attiva File Transformation, riavvia Jellyfin e ricarica il client. Il pulsante è visibile solo agli amministratori, solo nelle pagine di serie e film, e solo quando la libreria di appartenenza corrisponde a `Library Pattern`.

</details>

<details>
<summary><strong>L'import da YouTube dice che yt-dlp non è stato trovato</strong></summary>

<br />

Installa `yt-dlp` dentro l'ambiente di Jellyfin. In un'installazione Docker questo significa dentro il container di Jellyfin, non sull'host. La pagina delle impostazioni indica dove ha cercato; se la tua installazione si trova in una posizione inusuale, specifica il percorso completo in **Percorso di yt-dlp**.

Le posizioni consuete e `PATH` vengono controllate automaticamente.

</details>

<details>
<summary><strong>Un elemento non viene mai risolto</strong></summary>

<br />

Apri **Non risolti** per riprovare, oppure cercalo e associalo a mano. Se il titolo non esiste su AnimeThemes, mettilo in blacklist per fermare le ricerche ripetute — oppure aggiungi il tema da un link YouTube. Controlla gli ID dei provider, l'anno, la soglia del titolo e il log delle attività prima di abbassare le protezioni sul riconoscimento.

</details>

<details>
<summary><strong>Tutta l'interfaccia web di Jellyfin si rompe dopo un aggiornamento</strong></summary>

<br />

Controlla nel log di Jellyfin se qualche plugin di iniezione web lancia `ObjectDisposedException`. Può succedere quando un altro iniettore trattiene un service provider già smaltito durante il ricaricamento dei plugin. Riavvia completamente Jellyfin e prova gli iniettori uno alla volta. Non sostituire la DLL di KometaThemes a mano.

</details>

### 🏗️ Architettura

```text
Libreria Jellyfin
      │
      ▼
LibrarySelection ──► CompositeResolver ──► API AnimeThemes
  pattern/tipo          │ ID provider         │ temi + stagioni
  idoneità              └ riserva su titolo   ▼
      │                                   Motore di download
      │                                 ffmpeg + resilienza
      ▼                                         │
Sync elemento / scheduler ──────────────────────┤
      │                                         │
      │            Import YouTube ──────────────┤
      │              yt-dlp                     │
      ▼                                         ▼
      ├── JsonResolutionCache            File dei temi + riparazione
      ├── FailedItemsStore                      │
      ├── associazioni manuali                  ▼
      └── elementi esclusi               Playlist M3U globale
```

### 🧪 Sviluppo e verifica

Requisiti: SDK .NET 9, Node.js 20, npm.

```bash
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build

npm ci
npx playwright install chromium
npm run test:browser
```

Comandi specifici:

```bash
npm run test:e2e     # solo i test end-to-end Playwright
npm run test:a11y    # solo il controllo di accessibilità axe
```

La CI esegue build e test .NET, la suite Playwright, il controllo axe, poi crea uno ZIP con la sola DLL e ne stampa l'MD5. Release e aggiornamento del catalogo restano passaggi di pubblicazione espliciti, e il deploy su un server Jellyfin è volutamente lasciato all'amministratore tramite il catalogo plugin.

### 📌 Politica di rilascio

KometaThemes usa `Major.Minor.Build.Revision`. Le nuove funzioni incrementano Minor o Build; le correzioni puntuali incrementano Revision. Ogni versione pubblicata comprende versioni allineate tra assembly e frontend, una build Release con test automatici, uno `KometaThemes.zip` con la sola DLL e il suo checksum MD5, una GitHub Release con funzioni, correzioni e breaking change, e una nuova voce in testa al manifest del catalogo.

### 🙏 Crediti e licenza

- Metadati e media dei temi: [AnimeThemes](https://animethemes.moe/)
- Media server: [Jellyfin](https://jellyfin.org/)
- Autore e manutentore: [iCosiSenpai](https://github.com/iCosiSenpai)
- Licenza: [GNU GPL v3](LICENSE)

</details>

<br />

<!-- ══════════════════════════════ FOOTER ══════════════════════════════ -->

<div align="center">

### 💛 Support the project · Sostieni il progetto

KometaThemes is free and open source.<br />
KometaThemes è gratuito e open source.

<a href="https://buymeacoffee.com/iCosiSenpai">
  <img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy me a coffee" height="46" />
</a>
&nbsp;&nbsp;
<a href="https://www.paypal.com/donate/?hosted_button_id=5A4E26XC45GLQ">
  <img src="https://www.paypalobjects.com/en_US/i/btn/btn_donateCC_LG.gif" alt="Donate with PayPal" height="44" />
</a>

<br /><br />

<a href="https://github.com/iCosiSenpai/KometaThemes/issues"><img src="https://img.shields.io/badge/Report%20a%20bug-Issues-d73a49?style=for-the-badge&logo=github&labelColor=1a1a2e" alt="Report a bug" /></a>
&nbsp;
<a href="https://github.com/iCosiSenpai/KometaThemes"><img src="https://img.shields.io/badge/Star%20the%20repo-KometaThemes-181717?style=for-the-badge&logo=github&labelColor=1a1a2e" alt="Star the repository" /></a>

<br /><br />

<sub>For bugs and feature requests, open an issue with anonymized logs, your Jellyfin version, the plugin version and reproducible steps.</sub><br />
<sub>Per segnalare bug o proporre funzioni, apri una issue con i log anonimizzati, la tua versione di Jellyfin, la versione del plugin e i passaggi per riprodurre il problema.</sub>

</div>

<div align="center">

<img src="assets/banner-readme-plugin.png" alt="KometaThemes for Jellyfin" width="100%" />

<h1>KometaThemes</h1>

<p>Anime openings and endings, downloaded automatically into your Jellyfin library.</p>

<p>
  <a href="https://github.com/iCosiSenpai/KometaThemes/releases"><img src="https://img.shields.io/github/v/release/iCosiSenpai/KometaThemes?color=00a4dc&labelColor=22272e" alt="Latest release" /></a>
  <a href="https://github.com/iCosiSenpai/KometaThemes/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/iCosiSenpai/KometaThemes/ci.yml?branch=main&label=build&labelColor=22272e" alt="Build status" /></a>
  <img src="https://img.shields.io/badge/Jellyfin-10.11.x-7c5cff?labelColor=22272e" alt="Jellyfin 10.11.x" />
  <a href="LICENSE"><img src="https://img.shields.io/github/license/iCosiSenpai/KometaThemes?labelColor=22272e" alt="GPL v3" /></a>
</p>

<p><a href="#english">English</a> · <a href="#italiano">Italiano</a></p>

</div>

---

<a id="english"></a>

## English

KometaThemes downloads anime openings and endings from [AnimeThemes](https://animethemes.moe/)
into your Jellyfin library, so series and movies play their real theme music instead of
silence. When AnimeThemes has no theme for a title, you can add one from a YouTube link.

It matches titles through the metadata providers your library already uses, handles
multi-season shows without attaching the wrong opening to the wrong season, and keeps
track of what it could not resolve so it does not search for it forever.

### What it does

- Downloads **OP/ED audio themes and video backdrops**, configured separately for series and movies.
- Matches titles through **AniDB, AniList, MyAnimeList, Kitsu and AniSearch**, with a fuzzy title search as a last resort.
- Handles **multi-season anime**: one best theme, every theme, or every theme grouped per season.
- **Theme Finder** for manual work: search, preview, filter, and save a permanent binding for future syncs.
- **Adds themes from a YouTube link** when AnimeThemes has none, asking whether it is an OP or ED and whether you want audio, video or both.
- Records **unresolved and excluded** items, so known misses are not re-queried on every run.
- **Repairs Jellyfin 10.11.x theme links** and can export a global M3U playlist.
- Accessible, responsive admin interface in **English and Italian**.

### Requirements

| | |
|---|---|
| **Jellyfin** | `10.11.x` — catalog ABI `10.11.8.0` |
| **Runtime** | .NET 9, provided by the supported Jellyfin release |
| **Administrator** | Required for configuration and all plugin actions |
| **File Transformation** | Optional — only for the ♪ shortcut on item pages |
| **yt-dlp** | Optional — only for YouTube import |

### Installation

Install through the Jellyfin plugin catalog. You never need to copy DLL files into the
container by hand, and updates arrive the same way.

1. Open **Dashboard → Plugins → Repositories** and add:

   ```text
   https://raw.githubusercontent.com/iCosiSenpai/iCosiSenpai-Plugins/main/manifest.json
   ```

2. Open **Catalog**, select **KometaThemes**, and install the latest version.
3. Restart Jellyfin when prompted, then hard-refresh the web client with `Ctrl+Shift+R`.

Optionally install **File Transformation** from the Jellyfin catalog to enable the ♪
shortcut on series and movie pages.

### First setup

1. Open **Dashboard → Plugins → KometaThemes**.
2. In **General**, set the interface language and check the library name pattern. The
   default `Anime` keeps all automatic work scoped to matching libraries.
3. In **Themes & Download**, choose the audio and video modes for series and movies.
4. In **Providers & Matching**, order the metadata providers.
5. Run **Sync now** for an incremental pass, or **Force sync** to re-evaluate everything.
6. In your Jellyfin user profile, enable **Settings → Display → Play theme songs**.

That last step matters: if themes download but never play, it is almost always this
checkbox, and Jellyfin upgrades sometimes reset it.

### How you use it

**Syncing.** KometaThemes runs on a schedule, shortly after a new item enters a matching
library, or on demand. An incremental sync only touches items that are missing or
incomplete; a force sync removes outdated themes and re-evaluates everything. Dry-run
mode resolves and logs without writing any files.

**Theme Finder.** For anything automatic matching gets wrong or cannot find. Search with
the Jellyfin title and year, both editable, pick the anime, then review its themes by
season: filter audio/video and OP/ED, require creditless media, preview a source, and
select individually or in bulk. You can download the result, or save only the binding so
future automatic syncs use it. It is reachable from the plugin dashboard, and from the ♪
shortcut on item pages when File Transformation is installed.

**Adding a theme from YouTube.** For openings and endings that simply are not on
AnimeThemes. Paste a link in the Theme Finder and you are asked whether it is an OP or an
ED, its number, and whether you want audio, video or both. Files are written with the same
layout and naming as a normal sync and are marked as imported, so a later sync never
deletes them as orphans.

This feature is **off by default**. Enable it in **Themes & Download → YouTube import**,
which also reports whether `yt-dlp` was found and lets you set its path. `yt-dlp` must be
installed inside the Jellyfin environment — in Docker that means the container, not the
host. Accepted links: `watch`, `youtu.be`, Shorts, YouTube Music and embeds; playlists are
ignored and only the linked video is imported. Check the terms of service and the
copyright rules that apply where you live before enabling it.

**Per item.** Each item's page shows its downloaded themes, on-disk and registration
state, missing files and manual binding. From there you can sync it, delete one theme or
all of them, open the Theme Finder, repair theme links after a library scan, or run
presets across every matching library.

**Unresolved, bindings and excluded.** Unresolved records matching and download failures
with attempt counts and the last error. Bindings holds manual item-to-anime links and
applies them before automatic resolution. Excluded holds items you blacklisted on purpose,
each restorable later.

### What you get on disk

```text
Series folder/
├── theme-music/
│   ├── OP1 - Guren no Yumiya__50.mp3
│   └── ED1 - Utsukushiki Zankoku na Sekai__50.mp3
└── backdrops/
    └── OP1 - Guren no Yumiya__50.webm
```

The volume suffix is generated for Jellyfin playback. Imported themes keep whatever
container the extractor returned, so an `.mp4` next to a `.webm` is expected.

Deletion only ever touches files the plugin recorded as its own, so your own artwork in
`backdrops/` is safe.

### Documentation

- [Configuration reference](docs/configuration.md) — every tab, fetch modes, matching, security behaviour
- [Troubleshooting](docs/troubleshooting.md) — themes not playing, missing ♪ shortcut, yt-dlp not found
- [REST API](docs/api.md) — every endpoint
- [Development](docs/development.md) — build, tests, architecture, release flow

### Credits and license

Theme metadata and media come from [AnimeThemes](https://animethemes.moe/).
Built for [Jellyfin](https://jellyfin.org/) by [iCosiSenpai](https://github.com/iCosiSenpai),
released under the [GNU GPL v3](LICENSE).

---

<a id="italiano"></a>

## Italiano

KometaThemes scarica le opening e le ending degli anime da [AnimeThemes](https://animethemes.moe/)
nella tua libreria Jellyfin, così serie e film partono con la loro sigla invece che in
silenzio. Quando AnimeThemes non ha il tema di un titolo, puoi aggiungerlo da un link
YouTube.

Riconosce i titoli tramite i provider di metadati che la tua libreria già usa, gestisce le
serie multi-stagione senza attaccare l'opening sbagliata alla stagione sbagliata, e tiene
traccia di ciò che non ha risolto per non cercarlo all'infinito.

### Cosa fa

- Scarica **temi audio OP/ED e sfondi video**, configurabili separatamente per serie e film.
- Riconosce i titoli tramite **AniDB, AniList, MyAnimeList, Kitsu e AniSearch**, con la ricerca per titolo come ultima risorsa.
- Gestisce gli **anime multi-stagione**: solo il tema migliore, tutti i temi, o tutti raggruppati per stagione.
- **Theme Finder** per il lavoro manuale: cerca, ascolta l'anteprima, filtra e salva un'associazione permanente per i sync futuri.
- **Aggiunge temi da un link YouTube** quando su AnimeThemes non ci sono, chiedendo se è una OP o una ED e se vuoi audio, video o entrambi.
- Registra gli elementi **non risolti ed esclusi**, così le ricerche già fallite non vengono ripetute a ogni giro.
- **Ripara i collegamenti dei temi** di Jellyfin 10.11.x e può esportare una playlist M3U globale.
- Interfaccia di amministrazione accessibile e responsive, in **italiano e inglese**.

### Requisiti

| | |
|---|---|
| **Jellyfin** | `10.11.x` — ABI del catalogo `10.11.8.0` |
| **Runtime** | .NET 9, fornito dalla versione di Jellyfin supportata |
| **Amministratore** | Necessario per la configurazione e per tutte le azioni del plugin |
| **File Transformation** | Opzionale — solo per il pulsante ♪ nelle pagine degli elementi |
| **yt-dlp** | Opzionale — solo per l'import da YouTube |

### Installazione

Si installa dal catalogo plugin di Jellyfin. Non serve mai copiare a mano file DLL nel
container, e gli aggiornamenti arrivano dalla stessa strada.

1. Apri **Dashboard → Plugin → Repository** e aggiungi:

   ```text
   https://raw.githubusercontent.com/iCosiSenpai/iCosiSenpai-Plugins/main/manifest.json
   ```

2. Apri **Catalogo**, seleziona **KometaThemes** e installa l'ultima versione.
3. Riavvia Jellyfin quando richiesto, poi ricarica il client con `Ctrl+Shift+R`.

Se vuoi il pulsante ♪ nelle pagine di serie e film, installa anche **File Transformation**
dal catalogo di Jellyfin.

### Prima configurazione

1. Apri **Dashboard → Plugin → KometaThemes**.
2. In **Generale**, imposta la lingua dell'interfaccia e controlla il pattern del nome
   libreria. Il valore predefinito `Anime` limita tutto il lavoro automatico alle librerie
   corrispondenti.
3. In **Temi e download**, scegli le modalità audio e video per serie e film.
4. In **Provider e riconoscimento**, ordina i provider di metadati.
5. Avvia **Sync ora** per un giro incrementale, o **Force sync** per rivalutare tutto.
6. Nel tuo profilo Jellyfin, attiva **Impostazioni → Visualizzazione → Riproduci sigle**.

Quest'ultimo punto conta: se i temi vengono scaricati ma non partono, quasi sempre è questa
casella, e gli aggiornamenti di Jellyfin a volte la reimpostano.

### Come si usa

**Sincronizzazione.** KometaThemes gira a intervalli regolari, poco dopo che un nuovo
elemento entra in una libreria corrispondente, oppure su richiesta. Il sync incrementale
tocca solo gli elementi mancanti o incompleti; il force sync rimuove i temi superati e
rivaluta tutto. La modalità dry-run risolve e scrive nel log senza creare alcun file.

**Theme Finder.** Per tutto quello che il riconoscimento automatico sbaglia o non trova.
Cerchi con titolo e anno di Jellyfin, entrambi modificabili, scegli l'anime, poi esamini i
suoi temi per stagione: filtri audio/video e OP/ED, richiedi solo materiale creditless,
ascolti un'anteprima e selezioni singolarmente o in blocco. Puoi scaricare il risultato, o
salvare solo l'associazione perché la usino i sync automatici futuri. È raggiungibile dalla
dashboard del plugin e dal pulsante ♪ nelle pagine degli elementi, se File Transformation è
installato.

**Aggiungere un tema da YouTube.** Per le opening e le ending che su AnimeThemes
semplicemente non ci sono. Incolli un link nel Theme Finder e ti viene chiesto se è una OP o
una ED, il suo numero, e se vuoi solo audio, solo video o entrambi. I file vengono scritti
con la stessa struttura e gli stessi nomi di un sync normale e vengono marcati come
importati, così un sync successivo non li cancella mai come orfani.

La funzione è **disattivata per impostazione predefinita**. Si attiva in **Temi e download →
Import da YouTube**, dove viene anche indicato se `yt-dlp` è stato trovato e dove puoi
specificarne il percorso. `yt-dlp` deve essere installato nell'ambiente di Jellyfin: con
Docker significa dentro il container, non sull'host. Link accettati: `watch`, `youtu.be`,
Shorts, YouTube Music ed embed; le playlist vengono ignorate e viene importato solo il video
collegato. Prima di attivarla, verifica i termini di servizio e le regole sul diritto
d'autore applicabili nel tuo paese.

**Singolo elemento.** La pagina di ogni elemento mostra i temi scaricati, lo stato su disco
e di registrazione, i file mancanti e l'associazione manuale. Da lì puoi sincronizzarlo,
eliminare un tema o tutti, aprire il Theme Finder, riparare i collegamenti dopo una
scansione della libreria, o avviare preset su tutte le librerie corrispondenti.

**Non risolti, associazioni ed esclusi.** Non risolti registra i fallimenti di
riconoscimento e download, con numero di tentativi e ultimo errore. Associazioni conserva i
collegamenti manuali elemento-anime e li applica prima della risoluzione automatica. Esclusi
contiene gli elementi messi in blacklist volutamente, ognuno ripristinabile in seguito.

### Cosa trovi su disco

```text
Cartella della serie/
├── theme-music/
│   ├── OP1 - Guren no Yumiya__50.mp3
│   └── ED1 - Utsukushiki Zankoku na Sekai__50.mp3
└── backdrops/
    └── OP1 - Guren no Yumiya__50.webm
```

Il suffisso del volume viene generato per la riproduzione in Jellyfin. I temi importati
mantengono il contenitore restituito dall'estrattore, quindi trovare un `.mp4` accanto a un
`.webm` è normale.

L'eliminazione tocca solo i file che il plugin ha registrato come propri, quindi le tue
immagini in `backdrops/` non vengono mai rimosse.

### Documentazione

- [Riferimento della configurazione](docs/configuration.md) — tutte le schede, modalità di download, riconoscimento, sicurezza
- [Risoluzione dei problemi](docs/troubleshooting.md) — temi che non partono, pulsante ♪ assente, yt-dlp non trovato
- [API REST](docs/api.md) — tutti gli endpoint
- [Sviluppo](docs/development.md) — build, test, architettura, procedura di rilascio

I documenti di approfondimento sono in inglese.

### Crediti e licenza

I metadati e i media dei temi vengono da [AnimeThemes](https://animethemes.moe/).
Realizzato per [Jellyfin](https://jellyfin.org/) da [iCosiSenpai](https://github.com/iCosiSenpai),
distribuito con licenza [GNU GPL v3](LICENSE).

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

</div>

KometaThemes downloads anime openings and endings from [AnimeThemes](https://animethemes.moe/)
into your Jellyfin library, so series and movies play their real theme music instead of
silence. When AnimeThemes has no theme for a title, you can add one from a YouTube link.

It matches titles through the metadata providers your library already uses, handles
multi-season shows without attaching the wrong opening to the wrong season, and remembers
what it could not resolve so it does not keep searching for it on every run.

## What it does

- Downloads **OP/ED audio themes and video backdrops**, configured separately for series and movies.
- Matches titles through **AniDB, AniList, MyAnimeList, Kitsu and AniSearch**, with a fuzzy title search as a last resort.
- Handles **multi-season anime**: one best theme, every theme, or every theme grouped per season.
- **Theme Finder** for manual work: search, preview, filter, and save a permanent binding for future syncs.
- **Adds themes from a YouTube link** when AnimeThemes has none, asking whether it is an OP or ED and whether you want audio, video or both.
- Records **unresolved and excluded** items, so known misses are not re-queried on every run.
- **Repairs Jellyfin 10.11.x theme links** and can export a global M3U playlist.
- Accessible, responsive admin interface, in English or Italian.

## Requirements

| Requirement | Details |
|---|---|
| **Jellyfin** | `10.11.x` — catalog ABI `10.11.8.0` |
| **Runtime** | .NET 9, provided by the supported Jellyfin release |
| **Administrator account** | Required for configuration and every plugin action |
| **File Transformation** | Optional — only for the ♪ shortcut on item pages |
| **yt-dlp** | Optional — YouTube import works without it; installing it makes the extractor more resilient |

## Installation

Install through the Jellyfin plugin catalog. You never need to copy DLL files into the
container by hand, and updates arrive the same way.

1. Open **Dashboard → Plugins → Repositories** and add:

   ```text
   https://raw.githubusercontent.com/iCosiSenpai/iCosiSenpai-Plugins/main/manifest.json
   ```

2. Open **Catalog**, select **KometaThemes**, and install the latest version.
3. Restart Jellyfin when prompted, then hard-refresh the web client with `Ctrl+Shift+R`.

To get the ♪ shortcut on series and movie pages, also install **File Transformation** from
the Jellyfin catalog. Everything else works without it.

## First setup

1. Open **Dashboard → Plugins → KometaThemes**.
2. In **General**, set the interface language and check the library name pattern. The
   default `Anime` keeps all automatic work scoped to libraries whose name matches.
3. In **Themes & Download**, choose the audio and video modes for series and movies.
4. In **Providers & Matching**, order the metadata providers.
5. Run **Sync now** for an incremental pass, or **Force sync** to re-evaluate everything.
6. In your Jellyfin user profile, enable **Settings → Display → Play theme songs**.

That last step matters more than it looks. If themes download but never play, it is almost
always this checkbox, and Jellyfin upgrades sometimes reset it.

## Using it

### Syncing

KometaThemes runs on a schedule, shortly after a new item enters a matching library, or on
demand. An incremental sync only touches items that are missing or incomplete. A force sync
removes outdated themes and re-evaluates everything. Dry-run mode resolves and logs without
writing any files, which is the safe way to see what a configuration change would do.

### Theme Finder

For anything automatic matching gets wrong or cannot find. Search with the Jellyfin title
and year, both editable, pick the anime, then review its themes by season: filter audio and
video or OP and ED, require creditless media, preview a source before committing, and select
individually or in bulk.

You can download the result, or save only the binding so future automatic syncs use your
choice. The Theme Finder is reachable from the plugin dashboard, and from the ♪ shortcut on
item pages when File Transformation is installed.

### Adding a theme from YouTube

For openings and endings that simply are not on AnimeThemes.

Paste a link in the Theme Finder and you are asked whether it is an OP or an ED, its number,
and whether you want audio, video or both. Files are written with the same layout and naming
as a normal sync and are marked as imported, so a later sync never deletes them as orphans.

This feature is **off by default**. Enable it in **Themes & Download → YouTube import**. Nothing
needs to be installed: the extractor ships inside the plugin package, so import works on a stock
Jellyfin server.

If `yt-dlp` happens to be installed in the Jellyfin environment it is detected and used instead,
with no configuration. That is worth doing on a server you maintain: yt-dlp is updated continuously
against YouTube's changes, while the bundled extractor is pinned and only moves when this plugin is
released. The settings page reports which of the two is in use.

Accepted links are `watch`, `youtu.be`, Shorts, YouTube Music and embeds. Playlists are
ignored: only the linked video is imported. Check the terms of service and the copyright
rules that apply where you live before enabling this.

### Per-item controls

Each item's page shows its downloaded themes, on-disk and registration state, missing files
and manual binding. From there you can sync that item, delete one theme or all of them, open
the Theme Finder, repair theme links after a library scan, or run presets across every
matching library.

### Unresolved, bindings and excluded

**Unresolved** records matching and download failures with attempt counts and the last
error, so a failure is diagnosable instead of silent. **Bindings** holds manual
item-to-anime links and applies them before automatic resolution. **Excluded** holds items
you blacklisted on purpose, each restorable later.

## What you get on disk

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
`backdrops/` is never removed.

## Documentation

- [Configuration reference](docs/configuration.md) — every tab, fetch modes, matching, security behaviour
- [Troubleshooting](docs/troubleshooting.md) — themes not playing, missing ♪ shortcut, YouTube import not offered
- [REST API](docs/api.md) — every endpoint
- [Development](docs/development.md) — build, tests, architecture, release flow

## Credits and license

Theme metadata and media come from [AnimeThemes](https://animethemes.moe/), whose service
makes this plugin possible.

Built for [Jellyfin](https://jellyfin.org/) by [iCosiSenpai](https://github.com/iCosiSenpai),
released under the [GNU GPL v3](LICENSE).

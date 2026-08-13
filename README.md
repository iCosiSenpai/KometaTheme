<div align="center">

<img src="Jellyfin.Plugin.KometaThemes/Web/assets/kometathemes-icon.png" alt="KometaThemes anime mascot" width="168" />

# KometaThemes

**Anime openings and endings, automatically brought into Jellyfin.**

[![Latest release](https://img.shields.io/github/v/release/iCosiSenpai/KometaThemes?color=00a4dc&labelColor=171a2b)](https://github.com/iCosiSenpai/KometaThemes/releases/latest)
[![Build](https://img.shields.io/github/actions/workflow/status/iCosiSenpai/KometaThemes/ci.yml?branch=main&label=build&labelColor=171a2b)](https://github.com/iCosiSenpai/KometaThemes/actions/workflows/ci.yml)
![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11.x-7c5cff?labelColor=171a2b)
[![License](https://img.shields.io/github/license/iCosiSenpai/KometaThemes?labelColor=171a2b)](LICENSE)

[Install](#installation) · [Set up](#first-time-setup) · [Use](#everyday-use) · [Troubleshoot](docs/troubleshooting.md) · [API](docs/api.md)

</div>

KometaThemes downloads the real opening and ending themes for anime series and movies from
[AnimeThemes](https://animethemes.moe/) and places them where Jellyfin expects theme music and
video backdrops. It resolves titles from the metadata already attached to your library, keeps
seasons separate when the source data supports it, and gives you manual controls for everything
automatic matching cannot decide safely.

No API key is required. YouTube import also works on a stock Jellyfin installation through the
extractor included with the plugin; `yt-dlp` remains an optional, automatically detected upgrade.

## Highlights

- **Audio and video themes** — configure OP/ED audio and video independently for series and movies.
- **Metadata-aware matching** — resolves AniDB, AniList, MyAnimeList, Kitsu and AniSearch IDs before
  falling back to a guarded title search.
- **Multi-season support** — download one best theme, every theme, or themes grouped by season
  without confidently assigning an opening to the wrong season.
- **Theme Finder** — search AnimeThemes manually, preview sources, filter OP/ED and audio/video,
  select across seasons, download, and save a permanent Jellyfin-item binding.
- **YouTube fallback** — paste a supported YouTube link for a theme missing from AnimeThemes and
  choose OP or ED plus audio, video or both.
- **Per-item tools** — inspect files and registrations, sync one title, remove plugin-owned themes,
  repair links, blacklist an item or open Theme Finder directly.
- **Useful failure state** — unresolved items retain their error and attempt history instead of
  being retried silently on every pass.
- **Jellyfin integration** — scheduled and event-driven sync, a global M3U playlist, theme-link
  repair for Jellyfin 10.11.x, and an optional ♪ shortcut on item pages.

## Requirements

| Requirement | Status | Notes |
|---|---:|---|
| Jellyfin `10.11.x` | Required | Catalog target ABI: `10.11.8.0`; .NET 9 is supplied by Jellyfin. |
| Administrator account | Required | Configuration and plugin actions are admin-only. |
| File Transformation plugin | Optional | Adds the ♪ KometaThemes shortcut to series and movie pages. |
| `yt-dlp` | Optional | Preferred automatically when installed; the bundled extractor is the fallback. |

The plugin administration interface is currently in English. Italian season names such as
`2ª Stagione` are still understood when matching library folders and titles.

## Installation

KometaThemes is distributed through the Jellyfin plugin catalog. Do not copy DLLs into the
Jellyfin plugin directory manually.

1. Open **Dashboard → Plugins → Repositories**.
2. Add this repository URL:

   ```text
   https://raw.githubusercontent.com/iCosiSenpai/iCosiSenpai-Plugins/main/manifest.json
   ```

3. Open **Catalog**, choose **KometaThemes**, and install the latest version.
4. Restart Jellyfin when prompted.
5. Hard-refresh the web client with `Ctrl+Shift+R` so versioned frontend assets are reloaded.

Updates are delivered through the same catalog. File Transformation is only needed if you want
the ♪ shortcut; the dashboard, scheduler, Theme Finder and API work without it.

## First-time setup

1. Open **Dashboard → Plugins → KometaThemes**.
2. Under **General**, verify the library-name pattern. The default `Anime` limits all automatic
   work and item-page integration to libraries whose names match it.
3. Under **Themes & Download**, choose the audio and video modes separately for series and movies.
4. Under **Providers & Matching**, arrange the metadata providers used by your libraries.
5. Run **Sync now** for an incremental pass. Use **Dry run** first if you want resolution and logs
   without writing media files.
6. For each Jellyfin user who should hear themes, enable
   **User menu → Settings → Display → Play theme songs**.

If files download correctly but nothing plays, check step 6 first. Jellyfin upgrades can reset
that per-user preference.

## Everyday use

### Automatic sync

The scheduled task and library events process matching anime libraries. A normal sync only fills
missing or incomplete results. **Force sync** removes outdated plugin-owned themes and resolves
the selected scope again; it does not delete unrelated artwork or media.

### Theme Finder

Use Theme Finder when a title has no match, the match is wrong, or you want exact control. Search
by title and year, choose an AnimeThemes result, preview its sources, and select themes individually
or in bulk. Saving a binding makes that choice win over automatic matching on future syncs.

With File Transformation installed, the ♪ button opens the same workflow from a series or movie
page. Otherwise, open it from the KometaThemes dashboard.

### Import from YouTube

Enable **Themes & Download → YouTube import**, then paste a `watch`, `youtu.be`, Shorts, YouTube
Music or embed URL into Theme Finder. The plugin reduces it to a canonical video ID, ignores
playlist context, and asks how the result should be named and imported.

The managed extractor ships in the release archive, so there is nothing else to install. When
`yt-dlp` exists in the Jellyfin environment, KometaThemes uses it automatically because it can be
updated independently as YouTube changes. The settings page reports which backend is active.

Only import media you are allowed to use. You remain responsible for the applicable copyright
rules and service terms.

### Unresolved, bindings and excluded items

- **Unresolved** explains matching and download failures and lets you retry, search, dismiss or
  blacklist them.
- **Bindings** contains permanent Jellyfin-item to AnimeThemes matches and lets you unlock them.
- **Excluded** contains intentionally skipped items, which can be restored later.

## Files created in the library

```text
Series folder/
├── theme-music/
│   ├── OP1 - Guren no Yumiya__50.mp3
│   └── ED1 - Utsukushiki Zankoku na Sekai__50.mp3
└── backdrops/
    └── OP1 - Guren no Yumiya__50.webm
```

The numeric suffix controls Jellyfin's playback volume. YouTube imports preserve a compatible
source container, so `.mp4` beside `.webm` is normal. Cleanup is based on the files KometaThemes
recorded as its own; your existing artwork in `backdrops/` is left alone.

## Reliability and security

- Downloads are written atomically, so an interruption cannot publish a half-written theme.
- Caches, unresolved records, bindings and exclusions are bounded and survive restarts.
- Remote URLs are restricted, API calls use Jellyfin's authenticated same-origin context, and
  mutating endpoints require an administrator.
- The pasted YouTube URL is validated and reduced to its video ID before an extractor sees it.
- Network requests use rate limiting, retries and circuit breaking; conversion concurrency is
  capped across the plugin.

See the [configuration reference](docs/configuration.md) for the exact controls and behaviour.

## Documentation

- [Configuration reference](docs/configuration.md) — settings, fetch modes, matching and safeguards
- [Troubleshooting](docs/troubleshooting.md) — playback, permissions, the ♪ shortcut and imports
- [REST API](docs/api.md) — endpoints and request shapes
- [Development](docs/development.md) — architecture, tests, packaging and release flow

When reporting a problem, include the KometaThemes entries from the plugin's **Activity** panel or
from Jellyfin's current server log, plus the plugin and Jellyfin versions. Please remove tokens,
paths or other private information before posting logs.

## Building from source

The backend targets .NET 9 and the browser regression suite uses Node.js 20:

```bash
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build

npm ci
npx playwright install chromium
npm run test:browser
```

The release archive intentionally contains exactly four assemblies: KometaThemes, YoutubeExplode,
AngleSharp and JsonExtensions. See [Development](docs/development.md) before changing dependencies
or packaging.

## Credits and license

Theme metadata and media are provided by [AnimeThemes](https://animethemes.moe/). KometaThemes is
built for [Jellyfin](https://jellyfin.org/) by [iCosiSenpai](https://github.com/iCosiSenpai) and is
released under the [GNU General Public License v3](LICENSE).

If the plugin is useful to you, you can support its maintenance through
[Buy Me a Coffee](https://www.buymeacoffee.com/iCosiSenpai) or
[PayPal](https://www.paypal.com/donate/?hosted_button_id=5A4E26XC45GLQ).

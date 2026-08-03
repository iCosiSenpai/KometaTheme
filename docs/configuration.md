# Configuration reference

The section names below are the tab labels as they appear in the plugin.

| Tab | Purpose |
|---|---|
| **General** | Interface language, library filter, schedule, auto-sync, cleanup, notifications |
| **Themes & Download** | Series/movie media modes, volume, OP/ED and credit filters, season behaviour, download parallelism, dry-run, YouTube import |
| **Providers & Matching** | Provider order, fuzzy title threshold, API rate, positive/negative cache TTLs, cache controls |
| **Excluded** | Blacklisted items and restore controls, global playlist configuration, M3U export |
| **Bindings** | Persistent manual matches, unlock/recalculate, optional removal of downloaded files |
| **Unresolved** | Retry, manual resolution, blacklist, dismiss, clear |

## Fetch modes

Audio and video are configured separately, and series and movies are configured
separately, so a library can download audio themes for series only.

| Stored value | Shown in the UI as | Behaviour |
|---|---|---|
| `None` | *None* | Do not download this media type |
| `Single` | *Best theme only* | Download the best eligible theme |
| `All` | *All themes* | Download all eligible themes |
| `AllPerSeason` | *All, per season* | Keep eligible themes grouped and named per detected season |

`AllPerSeason` only groups by season when the detected theme groups actually partition
the seasons. When they do not, the plugin does not guess: it falls back rather than
attaching a theme to the wrong season.

## Library filter

`Library Pattern` decides which libraries the plugin touches, both for automatic work
and for UI injection. The default `Anime` keeps everything scoped to libraries whose
name matches, which is why an item outside them reports as not eligible.

## Matching

Titles are resolved through provider IDs first — AniDB, AniList, MyAnimeList, Kitsu
and AniSearch, in the order you configure — and only then through a fuzzy title
search.

The similarity threshold guards that last step. Lowering it produces confident wrong
matches, which are harder to notice than a missing theme, so raise it if you see bad
matches rather than lowering it to force results.

Resolution results are cached with separate TTLs for hits and misses, so a title that
genuinely has no themes is not re-queried on every sync.

## Reliability and security behaviour

- API calls use the current Jellyfin `MediaBrowser` token and same-origin credentials.
- Remote media is accepted only from same-origin HTTP(S) or HTTPS AnimeThemes domains.
  Credentials embedded in URLs are rejected.
- YouTube links are reduced to a canonical video ID server-side, so the pasted text
  never reaches the extractor's command line.
- Theme files are published atomically, so a failed conversion cannot leave a broken
  file behind.
- Deletion is driven by what the plugin recorded, so it never removes files it did not
  create, including your own artwork in `backdrops/`.
- A damaged cache file is quarantined rather than silently emptied.
- Requests use rate limiting, retries and circuit-breaker behaviour, and `ffmpeg`
  concurrency is capped across the whole plugin.
- Stale async responses are discarded when navigation changes context, and every
  mutating action guards against double submission.

## Accessibility

The frontend ships keyboard tab navigation, focus-trapped dialogs with Escape and
focus restoration, live status regions and `aria-busy` states, listbox navigation in
the Theme Finder, and responsive dark/light design tokens.

Asset loading is sequential and versioned, with visible failure handling rather than a
silently broken page.

# REST API

All paths are relative to `/Plugins/KometaThemes`.

Every endpoint requires an elevated Jellyfin token, except the two web assets at the
bottom of the table, which must stay anonymous because the Jellyfin web client loads
them before authentication.

## Health and sync

| Endpoint | Method | Purpose |
|---|:---:|---|
| `/Health` | GET | Version, health, metrics, current sync summary |
| `/Sync/status` | GET | Live sync progress |
| `/Sync/sync` | POST | Start an incremental sync |
| `/Sync/force` | POST | Start a server-side forced sync |
| `/Sync/run` | POST | Start a library preset |

## Per item

| Endpoint | Method | Purpose |
|---|:---:|---|
| `/Items/{id}/info` | GET | Item and theme-registration context |
| `/Items/{id}/eligible` | GET | Whether the item is in a matching library |
| `/Items/{id}/binding` | GET | The item's manual binding, if any |
| `/Items/{id}/themes` | GET · DELETE | List themes, or delete them |
| `/Items/{id}/sync` | POST | Sync one eligible item |
| `/Items/{id}/preview` | POST | Resolve without downloading anything |
| `/Items/{id}/repair` | POST | Repair Jellyfin theme links |
| `/Items/{id}/download` | POST | Download selected AnimeThemes media |
| `/Items/{id}/youtube` | POST | Import a theme from a YouTube link |

## Search and YouTube

| Endpoint | Method | Purpose |
|---|:---:|---|
| `/Search` | GET | Search AnimeThemes candidates |
| `/Anime/{id}/themes` | GET | Retrieve themes and season groups |
| `/YouTube/status` | GET | Whether YouTube import is enabled and available |

## Bindings

| Endpoint | Method | Purpose |
|---|:---:|---|
| `/Bindings` | GET | List every manual binding |
| `/Bindings/{id}` | POST · DELETE | Save or remove a binding |
| `/Bindings/{id}/unlock` | POST | Drop the binding but keep the files |

## Unresolved items

| Endpoint | Method | Purpose |
|---|:---:|---|
| `/Failed/items` | GET | List unresolved items |
| `/Failed/count` | GET | Unresolved count |
| `/Failed/items/{id}` | DELETE | Dismiss one entry |
| `/Failed/items/{id}/resolve-manually` | POST | Mark as handled by hand |
| `/Failed/clear` | POST | Clear the list |

## Excluded items

| Endpoint | Method | Purpose |
|---|:---:|---|
| `/Skipped/items` | GET | List excluded items |
| `/Skipped/count` | GET | Excluded count |
| `/Skipped/{id}` | POST | Add to the blacklist |
| `/Skipped/{id}/remove` | POST | Restore one item |
| `/Skipped/clear` | POST | Clear the blacklist |

## Cache, logs and playlist

| Endpoint | Method | Purpose |
|---|:---:|---|
| `/Cache/stats` | GET | Resolution-cache statistics |
| `/Cache/clear` | POST | Clear the resolution cache |
| `/Logs?lines=200` | GET | Read current plugin log entries |
| `/Playlist/refresh` | POST | Rebuild the global playlist |
| `/Playlist/export` | GET | Download the M3U playlist |

## Web assets

| Endpoint | Method | Purpose |
|---|:---:|---|
| `/ItemButton.js` | GET | Item-button injector script — anonymous |
| `/InjectButton` | POST | File Transformation hook — anonymous |

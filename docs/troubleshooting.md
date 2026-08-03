# Troubleshooting

## Themes download but do not play

Check `Settings → Display → Play theme songs` in the affected user's profile first.
Jellyfin upgrades sometimes reset it, and it is by far the most common cause.

If it is already enabled, open the item's KometaThemes page and read its registration
banner, run a Jellyfin library scan, then use **Repair links**.

## The ♪ shortcut does not appear on item pages

Install and enable **File Transformation**, restart Jellyfin, then hard-refresh the
web client with `Ctrl+Shift+R`.

The shortcut is deliberately narrow: it is visible only to administrators, only on
series and movie pages, and only when the owning library matches `Library Pattern`.

## YouTube import reports that yt-dlp was not found

Install `yt-dlp` inside the Jellyfin environment. In a Docker setup that means inside
the Jellyfin container, not on the host.

The settings page reports every location it searched, which includes the usual install
paths and everything on `PATH`. If your install lives somewhere unusual, set the full
path in **yt-dlp path**.

## An item never resolves

Open **Unresolved** to retry, or search and bind the item by hand.

If the title genuinely does not exist on AnimeThemes, either add the theme from a
YouTube link, or blacklist the item so it stops being searched on every sync.

Before lowering the matching safeguards, review the item's provider IDs, its year, the
title-similarity threshold and the live activity log. A too-low threshold produces
confident wrong matches, which are harder to notice than a missing theme.

## The whole Jellyfin web UI breaks after a plugin update

Look in the Jellyfin log for a web-injection plugin throwing
`ObjectDisposedException`. This happens when an injector holds on to a disposed
service provider across a plugin reload, and it is not specific to this plugin.

Perform a full Jellyfin restart, then re-enable web injectors one at a time to find
the one at fault. Do not replace the KometaThemes DLL by hand: install through the
plugin catalog so the version and the manifest stay consistent.

## Where the logs are

Plugin activity is readable from the plugin dashboard, and through
`GET /Plugins/KometaThemes/Logs?lines=200` with an elevated token.

The Jellyfin server log itself is the place to look for injection and startup
problems.
